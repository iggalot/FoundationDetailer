using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using FoundationDetailsLibraryAutoCAD.AutoCAD.NOD;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD
{
    public static class GradeBeamBuilder
    {
        public static void CreateGradeBeams(FoundationContext context, double halfWidth)
        {
            if (context == null || halfWidth <= 0) return;

            var doc = context.Document;
            var db = doc.Database;
            if (doc == null || db == null) return;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // --- Enumerate beams
                var beams = GradeBeamNOD.EnumerateGradeBeams(context, tr).ToList();
                if (beams.Count < 2) return;

                // --- Remove old edges
                // --- Remove old edges
                foreach (var (handle, gbDict) in beams)
                {
                    if (GradeBeamNOD.TryGetEdges(context, tr, gbDict,
                        out ObjectId[] lefts, out ObjectId[] rights))
                    {
                        foreach (var id in lefts.Concat(rights))
                        {
                            if (id.IsValid && !id.IsErased)
                                tr.GetObject(id, OpenMode.ForWrite).Erase();
                        }

                        // IMPORTANT: pass HANDLE, not gbDict
                        var dict = GradeBeamNOD.GetEdgesDictionary(tr, db, handle, true);
                        foreach (var k in NODCore.EnumerateDictionary(dict).Select(e => e.Key).ToList())
                            dict.Remove(k);
                    }
                }


                // ============================================================
                // 1) CREATE ALL UNTRIMMED EDGES (GLOBAL SET)
                // ============================================================
                var allTempEdges = new List<(ObjectId centerlineId, bool isLeft, Polyline edge)>();

                foreach (var (_, gbDict) in beams)
                {
                    if (!GradeBeamNOD.TryGetCenterline(context, tr, gbDict, out ObjectId clId))
                        continue;

                    var cl = tr.GetObject(clId, OpenMode.ForRead) as Polyline;
                    if (cl == null) continue;

                    var left = ManualOffset(cl, halfWidth, true);
                    var right = ManualOffset(cl, halfWidth, false);

                    if (left != null)
                    {
                        btr.AppendEntity(left);
                        tr.AddNewlyCreatedDBObject(left, true);
                        allTempEdges.Add((clId, true, left));
                    }

                    if (right != null)
                    {
                        btr.AppendEntity(right);
                        tr.AddNewlyCreatedDBObject(right, true);
                        allTempEdges.Add((clId, false, right));
                    }
                }

                // ============================================================
                // 2) TRIM AGAINST ALL OTHER EDGES
                // ============================================================
                var trimmedSegments = new List<(ObjectId clId, bool isLeft, Polyline seg)>();

                foreach (var edgeData in allTempEdges)
                {
                    var edge = edgeData.edge;
                    var splitPts = new List<Point3d>();

                    foreach (var other in allTempEdges)
                    {
                        if (other.edge == edge) continue;
                        splitPts.AddRange(GetIntersectionPoints(edge, other.edge));
                    }

                    var pieces = SplitPolylineAtPoints(edge, splitPts, btr, tr);

                    foreach (var seg in pieces)
                    {
                        if (seg.NumberOfVertices < 2)
                            continue;

                        Point3d mid = GetMidpoint(
                            seg.GetPoint3dAt(0),
                            seg.GetPoint3dAt(seg.NumberOfVertices - 1)
                        );

                        bool insideOther =
                            allTempEdges.Any(o =>
                                o.edge != edge &&
                                IsPointNearPolyline(mid, o.edge, halfWidth * 0.45));

                        if (!insideOther)
                            trimmedSegments.Add((edgeData.centerlineId, edgeData.isLeft, seg));
                        else
                            seg.Erase();
                    }
                }

                // ============================================================
                // 3) DELETE ALL UNTRIMMED EDGES
                // ============================================================
                foreach (var e in allTempEdges)
                {
                    if (!e.edge.IsErased)
                        e.edge.Erase();
                }

                // ============================================================
                // 4) FILTER, JOIN + STORE PER BEAM
                // ============================================================
                foreach (var group in trimmedSegments.GroupBy(e => e.clId))
                {
                    // --- Separate left/right segments
                    var leftSegs = group.Where(g => g.isLeft).Select(g => g.seg).ToList();
                    var rightSegs = group.Where(g => !g.isLeft).Select(g => g.seg).ToList();

                    // --- Filter out segments that lie inside OTHER grade beams
                    string centerlineHandle = group.Key.Handle.ToString();
                    leftSegs = FilterSegmentsOutsideOtherBeams(doc, leftSegs, beams, context, tr, centerlineHandle, 1e-6);
                    rightSegs = FilterSegmentsOutsideOtherBeams(doc, rightSegs, beams, context, tr, centerlineHandle, 1e-6);


                    // --- Optional: Join consecutive segments here if desired
                    // var finalLeft = JoinEdgeSegments(context, leftSegs);
                    // var finalRight = JoinEdgeSegments(context, rightSegs);

                    var finalLeft = leftSegs;
                    var finalRight = rightSegs;

                    // --- Store filtered segments in NOD
                    GradeBeamNOD.StoreEdgeObjects(
                        context,
                        tr,
                        group.Key,
                        finalLeft.Select(p => p.ObjectId).ToArray(),
                        finalRight.Select(p => p.ObjectId).ToArray()
                    );

                    // --- Debug
                    doc.Editor.WriteMessage(
                        $"\n[DEBUG] Beam {group.Key}: stored {finalLeft.Count} LEFT and {finalRight.Count} RIGHT edges."
                    );
                }


                tr.Commit();
                doc.Editor.Regen();
            }
        }

        /// <summary>
        /// Returns only the segments that do NOT lie on or inside any other grade beam.
        /// Deletes segments that intersect other beams.
        /// </summary>
        private static List<Polyline> FilterSegmentsOutsideOtherBeams(
            Document doc,
            List<Polyline> segments,
            List<(string Handle, DBDictionary gbDict)> allBeams,
            FoundationContext context,
            Transaction tr,
            string thisBeamHandle,
            double tol = 1e-6)
        {
            var result = new List<Polyline>();
            var tolerance = new Tolerance(tol, tol);

            foreach (var seg in segments)
            {
                bool isInsideOther = false;

                var start = seg.StartPoint;
                var end = seg.EndPoint;

                foreach (var beam in allBeams)
                {
                    if (beam.Handle == thisBeamHandle)
                        continue;

                    // Get the grade beam dictionary from the handle
                    var gbDict = beam.gbDict; // You already stored the DBDictionary in allBeams

                    if (!GradeBeamNOD.TryGetEdges(context, tr, gbDict, out ObjectId[] lefts, out ObjectId[] rights))
                    {
                        doc.Editor.WriteMessage($"\nCould not get edges for beam {beam.Handle} -- returned false");
                        continue;
                    } else
                    {
                        doc.Editor.WriteMessage($"\nRetrieved edges for beam {beam.Handle}");

                    }

                    foreach (var oid in lefts.Concat(rights))
                        {
                            var pl = tr.GetObject(oid, OpenMode.ForRead) as Polyline;
                            if (pl == null) continue;

                            for (int i = 0; i < pl.NumberOfVertices - 1; i++)
                            {
                                var a = pl.GetPoint3dAt(i);
                                var b = pl.GetPoint3dAt(i + 1);

                                if (PointOnLineSegment(start, a, b, tolerance) ||
                                    PointOnLineSegment(end, a, b, tolerance))
                                {
                                    isInsideOther = true;
                                    break;
                                }
                            }

                            if (isInsideOther)
                                break;
                        }

                    if (isInsideOther)
                        break;
                }

                if (!isInsideOther)
                {
                    result.Add(seg);
                }
                else
                {
                    if (!seg.IsErased && seg.Database != null)
                    {
                        if (!seg.IsWriteEnabled)
                            seg.UpgradeOpen();
                        seg.Erase();
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Checks if a point lies on a line segment (with tolerance).
        /// </summary>
        private static bool PointOnLineSegment(Point3d pt, Point3d a, Point3d b, Tolerance tol)
        {
            var ab = b - a;   // Vector from a to b
            var ap = pt - a;  // Vector from a to point

            double abLengthSq = ab.Length * ab.Length;  // manually square the length
            if (abLengthSq < double.Epsilon)
                return false; // degenerate segment

            // Project point onto line
            double t = ap.DotProduct(ab) / abLengthSq;

            // t between 0..1 => point lies within segment
            if (t < 0.0 - tol.EqualPoint || t > 1.0 + tol.EqualPoint)
                return false;

            // Closest point on segment
            Point3d closest = a + ab * t;

            // Check distance from original point
            return closest.DistanceTo(pt) <= tol.EqualPoint;
        }



        /// <summary>
        /// Determines if a given polyline segment lies inside another polyline (grade beam edge),
        /// using a simple midpoint test and tolerance.
        /// </summary>
        /// <param name="seg">The segment to test (typically a 2-vertex polyline)</param>
        /// <param name="pl">The polyline representing another grade beam edge</param>
        /// <param name="tol">Tolerance distance for considering "inside"</param>
        /// <returns>True if the segment is inside the polyline, false otherwise</returns>
        private static bool IsPolylineSegmentInside(Polyline seg, Polyline pl, double tol)
        {
            if (seg == null || pl == null || seg.NumberOfVertices < 2)
                return false;

            // Compute midpoint of the segment
            var start = seg.GetPoint3dAt(0);
            var end = seg.GetPoint3dAt(1);
            var mid = new Point3d(
                (start.X + end.X) / 2.0,
                (start.Y + end.Y) / 2.0,
                (start.Z + end.Z) / 2.0);

            // Check distance from midpoint to each segment of the polyline
            for (int i = 0; i < pl.NumberOfVertices - 1; i++)
            {
                var a = pl.GetPoint3dAt(i);
                var b = pl.GetPoint3dAt(i + 1);

                // Vector from a to b
                var ab = b - a;

                // Manually compute squared length
                double abLenSq = ab.X * ab.X + ab.Y * ab.Y + ab.Z * ab.Z;
                if (abLenSq < 1e-12) continue; // avoid zero-length segment

                // Project midpoint onto segment vector
                double t = ((mid - a).DotProduct(ab)) / abLenSq;
                t = Math.Max(0.0, Math.Min(1.0, t)); // clamp to segment
                var closest = a + (ab * t);

                if (closest.DistanceTo(mid) <= tol)
                    return true;
            }

            return false;
        }






        private static Point3d GetMidpoint(Point3d a, Point3d b)
        {
            return new Point3d(
                (a.X + b.X) * 0.5,
                (a.Y + b.Y) * 0.5,
                (a.Z + b.Z) * 0.5
            );
        }


        private static List<Point3d> GetIntersectionPoints(Polyline a, Polyline b)
        {
            var pts = new List<Point3d>();

            for (int i = 0; i < a.NumberOfVertices - 1; i++)
                using (var la = new Line(a.GetPoint3dAt(i), a.GetPoint3dAt(i + 1)))
                {
                    for (int j = 0; j < b.NumberOfVertices - 1; j++)
                        using (var lb = new Line(b.GetPoint3dAt(j), b.GetPoint3dAt(j + 1)))
                        {
                            var col = new Point3dCollection();
                            la.IntersectWith(lb, Intersect.OnBothOperands, col, IntPtr.Zero, IntPtr.Zero);
                            foreach (Point3d p in col) pts.Add(p);
                        }
                }

            return pts;
        }

        private static List<Polyline> SplitPolylineAtPoints(
    Polyline edge,
    List<Point3d> splitPts,
    BlockTableRecord btr,
    Transaction tr)
        {
            var pts = new List<Point3d> { edge.StartPoint };
            for (int i = 1; i < edge.NumberOfVertices; i++)
                pts.Add(edge.GetPoint3dAt(i));

            pts.AddRange(splitPts);

            pts = pts
                .Distinct(new Point3dEqualityComparer(new Tolerance(1e-6, 1e-6)))
                .OrderBy(p => p.DistanceTo(edge.StartPoint))
                .ToList();

            var segs = new List<Polyline>();

            for (int i = 0; i < pts.Count - 1; i++)
            {
                if (pts[i].DistanceTo(pts[i + 1]) < 1e-6) continue;

                var pl = new Polyline();
                pl.AddVertexAt(0, new Point2d(pts[i].X, pts[i].Y), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(pts[i + 1].X, pts[i + 1].Y), 0, 0, 0);

                btr.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                segs.Add(pl);
            }

            return segs;
        }

        private static bool IsPointNearPolyline(Point3d pt, Polyline pl, double tol)
        {
            for (int i = 0; i < pl.NumberOfVertices - 1; i++)
            {
                Point3d a = pl.GetPoint3dAt(i);
                Point3d b = pl.GetPoint3dAt(i + 1);

                Vector3d ab = b - a;
                Vector3d ap = pt - a;

                double t = ap.DotProduct(ab) / ab.LengthSqrd;
                t = Math.Max(0.0, Math.Min(1.0, t)); // clamp t to segment

                Point3d closest = a + ab * t;

                if (pt.DistanceTo(closest) < tol)
                    return true;
            }

            return false;
        }





        #region --- Step 1 ---

        #endregion

        #region --- Step 2 Create Offset Edges ---
        /// <summary>
        /// Offsets a single centerline polyline to both sides and adds the new edges to ModelSpace.
        /// Returns the created edge polylines.
        /// </summary>
        private static List<Polyline> CreateOffsetEdges(Polyline centerline, BlockTableRecord btr, Transaction tr, double halfWidth)
        {
            var edges = new List<Polyline>();
            if (centerline == null || btr == null || tr == null || halfWidth <= 0)
                return edges;

            // --- Offset left (-halfWidth)
            //DBObjectCollection leftOffset = centerline.GetOffsetCurves(-halfWidth);
            DBObjectCollection leftOffset = centerline.GetOffsetCurves(-halfWidth);

            foreach (Entity ent in leftOffset)
            {
                var pl = ent as Polyline;
                if (pl != null)
                {
                    btr.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);
                    edges.Add(pl);
                }
            }

            // --- Offset right (+halfWidth)
            DBObjectCollection rightOffset = centerline.GetOffsetCurves(halfWidth);
            foreach (Entity ent in rightOffset)
            {
                var pl = ent as Polyline;
                if (pl != null)
                {
                    btr.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);
                    edges.Add(pl);
                }
            }

            return edges;
        }

        private static Polyline ManualOffset(Polyline pl, double offsetDistance, bool toLeft)
        {
            if (pl == null || pl.NumberOfVertices < 2)
                return null;

            var offsetPts = new List<Point2d>();

            for (int i = 0; i < pl.NumberOfVertices; i++)
            {
                Point3d pt = pl.GetPoint3dAt(i);

                // Compute tangent direction for this vertex
                Vector2d tangent;
                if (i == 0)
                {
                    var vec = pl.GetPoint3dAt(1) - pt;
                    tangent = new Vector2d(vec.X, vec.Y);
                }
                else if (i == pl.NumberOfVertices - 1)
                {
                    var vec = pt - pl.GetPoint3dAt(i - 1);
                    tangent = new Vector2d(vec.X, vec.Y);
                }
                else
                {
                    var vPrev3d = pt - pl.GetPoint3dAt(i - 1);
                    var vNext3d = pl.GetPoint3dAt(i + 1) - pt;
                    var vPrev = new Vector2d(vPrev3d.X, vPrev3d.Y);
                    var vNext = new Vector2d(vNext3d.X, vNext3d.Y);
                    tangent = (vPrev + vNext).GetNormal(); // average direction
                }

                // Perpendicular vector
                Vector2d perp = tangent.GetPerpendicularVector().GetNormal() * offsetDistance;
                if (!toLeft)
                    perp = -perp;

                offsetPts.Add(new Point2d(pt.X + perp.X, pt.Y + perp.Y));
            }

            // Build new polyline
            Polyline offsetPl = new Polyline();
            for (int i = 0; i < offsetPts.Count; i++)
                offsetPl.AddVertexAt(i, offsetPts[i], 0, 0, 0);

            return offsetPl;
        }
        internal static List<Polyline> CreateOffsetEdgesManual(Polyline centerline, BlockTableRecord btr, Transaction tr, double halfWidth)
        {
            var edges = new List<Polyline>();
            var left = ManualOffset(centerline, halfWidth, true);
            if (left != null)
            {
                btr.AppendEntity(left);
                tr.AddNewlyCreatedDBObject(left, true);
                edges.Add(left);
            }

            var right = ManualOffset(centerline, halfWidth, false);
            if (right != null)
            {
                btr.AppendEntity(right);
                tr.AddNewlyCreatedDBObject(right, true);
                edges.Add(right);
            }

            return edges;
        }



        #endregion

        #region --- Step 3 Trime gradebeam edges at intersections---
        /// <summary>
        /// Trims offset edge polylines at intersections and creates new segments in ModelSpace.
        /// </summary>
        /// <summary>
        /// Trims a list of offset edge polylines at intersections with each other.
        /// Produces new segments in ModelSpace that do not overlap.
        /// </summary>
        private static List<Polyline> TrimEdgesAtIntersections(
            List<Polyline> edges,
            BlockTableRecord btr,
            Transaction tr)
        {
            var trimmedEdges = new List<Polyline>();
            if (edges == null || btr == null || tr == null)
                return trimmedEdges;

            var tol = new Tolerance(1e-9, 1e-9);

            for (int i = 0; i < edges.Count; i++)
            {
                var edge = edges[i];
                if (edge == null || edge.IsErased) continue;

                // Collect all points: start/end + intersections with other edges
                var points = new List<Point3d> { edge.StartPoint };

                // Add intermediate vertices
                for (int v = 1; v < edge.NumberOfVertices; v++)
                    points.Add(edge.GetPoint3dAt(v));

                // Check intersections with other edges
                for (int j = 0; j < edges.Count; j++)
                {
                    if (i == j) continue;
                    var other = edges[j];
                    if (other == null || other.IsErased) continue;

                    for (int k = 0; k < edge.NumberOfVertices - 1; k++)
                    {
                        var segLine = new Line(edge.GetPoint3dAt(k), edge.GetPoint3dAt(k + 1));
                        var intersections = FindPolylineIntersectionPoints(segLine, other);

                        foreach (var data in intersections)
                        {
                            // Only add if the point lies on the segment
                            if (segLine.StartPoint.DistanceTo(data.Point) + data.Point.DistanceTo(segLine.EndPoint)
                                - segLine.StartPoint.DistanceTo(segLine.EndPoint) < 1e-9)
                            {
                                points.Add(data.Point);
                            }
                        }

                        segLine.Dispose();
                    }
                }

                // Remove duplicates and sort along original start
                points = points.Distinct(new Point3dEqualityComparer(tol)).OrderBy(p => p.DistanceTo(edge.StartPoint)).ToList();

                // Create segments between consecutive points
                for (int s = 0; s < points.Count - 1; s++)
                {
                    if (points[s].DistanceTo(points[s + 1]) < 1e-9) continue;

                    var seg = new Polyline();
                    seg.AddVertexAt(0, new Point2d(points[s].X, points[s].Y), 0, 0, 0);
                    seg.AddVertexAt(1, new Point2d(points[s + 1].X, points[s + 1].Y), 0, 0, 0);

                    btr.AppendEntity(seg);
                    tr.AddNewlyCreatedDBObject(seg, true);

                    trimmedEdges.Add(seg);
                }
            }

            return trimmedEdges;
        }

        /// <summary>
        /// Equality comparer for Point3d with tolerance.
        /// </summary>
        private class Point3dEqualityComparer : IEqualityComparer<Point3d>
        {
            private readonly Tolerance _tol;
            public Point3dEqualityComparer(Tolerance tol) => _tol = tol;

            public bool Equals(Point3d a, Point3d b) =>
                a.IsEqualTo(b, _tol);

            public int GetHashCode(Point3d obj) =>
                obj.X.GetHashCode() ^ obj.Y.GetHashCode() ^ obj.Z.GetHashCode();
        }


        public class IntersectPointData
        {
            public Point3d Point { get; set; }
            // You can add other properties if needed later (e.g., segment index)
        }

        /// <summary>
        /// Finds intersection points between a line and a polyline.
        /// </summary>
        public static List<IntersectPointData> FindPolylineIntersectionPoints(Line segLine, Polyline poly, bool extendLine = false)
        {
            var results = new List<IntersectPointData>();
            if (segLine == null || poly == null) return results;

            for (int i = 0; i < poly.NumberOfVertices - 1; i++)
            {
                Line polySeg = new Line(poly.GetPoint3dAt(i), poly.GetPoint3dAt(i + 1));

                // IntersectType = OnBothOperands, ignoring tangents
                Point3dCollection intersections = new Point3dCollection();
                polySeg.IntersectWith(segLine, Intersect.OnBothOperands, intersections, IntPtr.Zero, IntPtr.Zero);

                foreach (Point3d pt in intersections)
                {
                    results.Add(new IntersectPointData { Point = pt });
                }

                polySeg.Dispose(); // release the temp line
            }

            return results;
        }

        /// <summary>
        /// Sorts points by distance from a reference point.
        /// </summary>
        public static List<Point3d> SortByDistanceFromRefPoint(List<Point3d> points, Point3d refPoint)
        {
            if (points == null || points.Count == 0) return new List<Point3d>();

            return points.OrderBy(p => p.DistanceTo(refPoint)).ToList();
        }

        /// <summary>
        /// Creates a 2-vertex polyline in ModelSpace.
        /// </summary>
        public static Polyline CreatePolyline(Point3d start, Point3d end, string layer = "0", string linetype = "ByLayer")
        {
            Polyline pl = new Polyline();
            pl.AddVertexAt(0, new Point2d(start.X, start.Y), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(end.X, end.Y), 0, 0, 0);

            pl.Layer = layer;
            pl.Linetype = linetype;

            return pl;
        }

        #endregion

        #region --- Step 4 Join edge segments if they are on the same polyline---
        /// <summary>
        /// Joins consecutive edge segments into single polylines where possible.
        /// Assumes all segments are already added to ModelSpace and part of a valid transaction.
        /// </summary>
        public static List<Polyline> JoinEdgeSegments(FoundationContext context, List<Polyline> segments)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (segments == null || segments.Count == 0)
                return new List<Polyline>();

            var joinedEdges = new List<Polyline>();

            // Tolerance for comparing points
            var tol = new Tolerance(1e-6, 1e-6);

            bool[] used = new bool[segments.Count];

            for (int i = 0; i < segments.Count; i++)
            {
                if (used[i]) continue;

                Polyline baseSeg = segments[i];

                // Skip if segment is erased or has no database
                if (baseSeg.IsErased || baseSeg.Database == null)
                {
                    used[i] = true;
                    continue;
                }

                used[i] = true;

                bool foundJoin;

                do
                {
                    foundJoin = false;

                    for (int j = 0; j < segments.Count; j++)
                    {
                        if (used[j]) continue;

                        Polyline candidate = segments[j];

                        // Skip invalid candidates
                        if (candidate.IsErased || candidate.Database == null)
                        {
                            used[j] = true;
                            continue;
                        }

                        // Upgrade both polylines for write if needed
                        if (!baseSeg.IsWriteEnabled) baseSeg.UpgradeOpen();
                        if (!candidate.IsWriteEnabled) candidate.UpgradeOpen();

                        // Check if endpoints match (start/end)
                        if (baseSeg.EndPoint.IsEqualTo(candidate.StartPoint, tol))
                        {
                            baseSeg.JoinEntities(new Entity[] { candidate });
                            candidate.Erase();
                            used[j] = true;
                            foundJoin = true;
                            break;
                        }
                        else if (baseSeg.StartPoint.IsEqualTo(candidate.EndPoint, tol))
                        {
                            candidate.JoinEntities(new Entity[] { baseSeg });
                            baseSeg = candidate; // New base
                            used[j] = true;
                            foundJoin = true;
                            break;
                        }
                    }

                } while (foundJoin);

                joinedEdges.Add(baseSeg);
            }

            return segments;
        }


        #endregion

        #region --- Step 5 ---

        #endregion

        #region --- Step 6 ---

        #endregion
    }



}
