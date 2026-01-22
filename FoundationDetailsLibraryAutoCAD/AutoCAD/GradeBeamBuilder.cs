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

            doc.Editor.WriteMessage($"\n[DEBUG] Entering CreateGradeBeams at {DateTime.Now}");


            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                int createdEdges = 0;

                // --- Enumerate grade beams
                var gradeBeams = GradeBeamNOD.EnumerateGradeBeams(context, tr).ToList();
                if (gradeBeams.Count < 2)
                {
                    doc.Editor.WriteMessage("\nAt least two grade beams must exist in the NOD.");
                    return;
                }

                // --- Delete existing LEFT_/RIGHT_ edges from ModelSpace and NOD (Debug Version)
                foreach (var (handle, gbDict) in gradeBeams)
                {
                    if (GradeBeamNOD.TryGetEdges(context, tr, gbDict, out ObjectId[] leftEdgeIds, out ObjectId[] rightEdgeIds))
                    {
                        var allEdgeIds = leftEdgeIds.Concat(rightEdgeIds).ToList();
                        if (allEdgeIds.Count == 0)
                        {
                            doc.Editor.WriteMessage($"\n[DEBUG] No existing edges found for beam {handle}.");
                        }
                        else
                        {
                            doc.Editor.WriteMessage($"\n[DEBUG] Found {allEdgeIds.Count} edges to erase for beam {handle}:");

                            foreach (var oid in allEdgeIds)
                            {
                                if (oid.IsNull)
                                {
                                    doc.Editor.WriteMessage("\n  [DEBUG] ObjectId is Null, skipping.");
                                    continue;
                                }

                                if (!oid.IsValid)
                                {
                                    doc.Editor.WriteMessage($"\n  [DEBUG] ObjectId {oid} is invalid, skipping.");
                                    continue;
                                }

                                if (oid.IsErased)
                                {
                                    doc.Editor.WriteMessage($"\n  [DEBUG] ObjectId {oid} already erased, skipping.");
                                    continue;
                                }

                                var obj = tr.GetObject(oid, OpenMode.ForWrite, false);
                                if (obj == null)
                                {
                                    doc.Editor.WriteMessage($"\n  [DEBUG] Object {oid} could not be retrieved.");
                                    continue;
                                }

                                doc.Editor.WriteMessage($"\n  [DEBUG] Erasing ObjectId {oid} of type {obj.GetType().Name}.");
                                obj.Erase();
                            }
                        }

                        // --- Clear dictionary entries
                        var edgesDict = GradeBeamNOD.GetEdgesDictionary(tr, db, handle, true);
                        foreach (var key in NODCore.EnumerateDictionary(edgesDict).Select(e => e.Key).ToList())
                        {
                            try
                            {
                                edgesDict.Remove(key);
                                doc.Editor.WriteMessage($"\n  [DEBUG] Removed NOD key: {key}");
                            }
                            catch (Exception ex)
                            {
                                doc.Editor.WriteMessage($"\n  [DEBUG] Failed to remove key {key}: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        doc.Editor.WriteMessage($"\n[DEBUG] No edges found in NOD for beam {handle}.");
                    }
                }


                // --- Process each grade beam
                int count = 0;
                foreach (var (handle, gbDict) in gradeBeams)
                {
                    if (!GradeBeamNOD.TryGetCenterline(context, tr, gbDict, out ObjectId centerlineId))
                        continue;

                    var centerline = tr.GetObject(centerlineId, OpenMode.ForRead) as Polyline;
                    if (centerline == null) continue;

                    // --- 1) Create temporary untrimmed edges
                    List<Polyline> tempEdges = CreateOffsetEdgesManual(centerline, btr, tr, halfWidth);

                    //List<Polyline> tempEdges = CreateOffsetEdges(centerline, btr, tr, halfWidth);

                    // --- 2) Trim edges at intersections
                    List<Polyline> trimmedEdges = TrimEdgesAtIntersections(tempEdges, btr, tr);

                    // --- 3) Join segments
                    List<Polyline> finalEdges = JoinEdgeSegments(context, trimmedEdges);
                    if (finalEdges.Count == 0) continue;

                    // --- 4) Erase temporary untrimmed edges
                    foreach (var e in tempEdges)
                    {
                        if (e != null && !e.IsErased)
                            e.UpgradeOpen();
                        e.Erase();
                    }

                    // --- 5) Store final edges in NOD
                    GradeBeamNOD.StoreEdgeObjects(
                        context, tr, centerlineId,
                        new ObjectId[] { finalEdges[0].ObjectId },
                        new ObjectId[] { finalEdges.Count > 1 ? finalEdges[1].ObjectId : ObjectId.Null }
                    );

                    createdEdges += finalEdges.Count;
                    count++;
                }

                tr.Commit();

                doc.Editor.Regen();
                doc.Editor.UpdateScreen();

                doc.Editor.WriteMessage($"\nCreated {createdEdges} grade beam edges from NOD.");
            }
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

            return joinedEdges;
        }


        #endregion

        #region --- Step 5 ---

        #endregion

        #region --- Step 6 ---

        #endregion
    }



}
