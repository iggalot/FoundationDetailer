using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using FoundationDetailer.AutoCAD;
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
            if (context == null || halfWidth <= 0)
                return;

            var doc = context.Document;
            var db = doc.Database;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // --- Enumerate beams
                var beams = GradeBeamNOD.EnumerateGradeBeams(context, tr).ToList();
                if (beams.Count == 0)
                    return;

                // --- Delete existing edges
                GradeBeamManager.DeleteAllGradeBeamEdges(context);

                // --- 1) Build footprints for all beams
                var footprints = new Dictionary<ObjectId, Polyline>();

                foreach (var (_, gbDict) in beams)
                {
                    if (!GradeBeamNOD.TryGetCenterline(context, tr, gbDict, out ObjectId clId))
                        continue;

                    var cl = tr.GetObject(clId, OpenMode.ForRead) as Polyline;
                    if (cl == null) continue;

                    var fp = BuildFootprint(cl, halfWidth);
                    footprints[clId] = fp;
                }

                // --- 2) Generate all edge segments
                var allEdges = new List<BeamEdgeSegment>();

                foreach (var (_, gbDict) in beams)
                {
                    if (!GradeBeamNOD.TryGetCenterline(context, tr, gbDict, out ObjectId clId))
                        continue;

                    var cl = tr.GetObject(clId, OpenMode.ForRead) as Polyline;
                    if (cl == null) continue;

                    var left = OffsetPolyline(cl, +halfWidth);
                    var right = OffsetPolyline(cl, -halfWidth);

                    allEdges.AddRange(ExplodeEdges(clId, left, true));
                    allEdges.AddRange(ExplodeEdges(clId, right, false));
                }

                // --- 3) Trim edges against all other footprints
                var trimmedEdges = TrimAllEdges(allEdges, footprints);

                // --- 4) Draw results and optionally color (debug)
                foreach (var e in trimmedEdges)
                {
                    var ln = new Line(e.Segment.StartPoint, e.Segment.EndPoint);
                    ln.ColorIndex = e.IsLeft ? 1 : 5; // Red = left, Blue = right
                    ms.AppendEntity(ln);
                    tr.AddNewlyCreatedDBObject(ln, true);
                }

                // --- 5) Commit
                tr.Commit();
                doc.Editor.Regen();
            }
        }


        // Beam edge segment data structure
        class BeamEdgeSegment
        {
            public ObjectId BeamId;
            public bool IsLeft;
            public LineSegment3d Segment;
        }

        // Offset polyline safely
        static Polyline OffsetPolyline(Polyline pl, double offset)
        {
            var curves = pl.GetOffsetCurves(offset);
            return (Polyline)curves[0];
        }

        // Build beam footprint polygon from centerline + width
        static Polyline BuildFootprint(Polyline centerline, double halfWidth)
        {
            var left = OffsetPolyline(centerline, +halfWidth);
            var right = OffsetPolyline(centerline, -halfWidth);

            var poly = new Polyline();
            int idx = 0;

            for (int i = 0; i < left.NumberOfVertices; i++)
                poly.AddVertexAt(idx++, left.GetPoint2dAt(i), 0, 0, 0);

            for (int i = right.NumberOfVertices - 1; i >= 0; i--)
                poly.AddVertexAt(idx++, right.GetPoint2dAt(i), 0, 0, 0);

            poly.Closed = true;
            return poly;
        }

        // Explode a polyline into atomic edge segments
        static List<BeamEdgeSegment> ExplodeEdges(ObjectId beamId, Polyline edge, bool isLeft)
        {
            var list = new List<BeamEdgeSegment>();
            for (int i = 0; i < edge.NumberOfVertices - 1; i++)
            {
                var p0 = edge.GetPoint3dAt(i);
                var p1 = edge.GetPoint3dAt(i + 1);
                list.Add(new BeamEdgeSegment
                {
                    BeamId = beamId,
                    IsLeft = isLeft,
                    Segment = new LineSegment3d(p0, p1)
                });
            }
            return list;
        }

        // Trim all edges against all other beam footprints
        static List<BeamEdgeSegment> TrimAllEdges(
            List<BeamEdgeSegment> edges,
            Dictionary<ObjectId, Polyline> footprints)
        {
            var result = new List<BeamEdgeSegment>();

            foreach (var edge in edges)
            {
                var segments = new List<LineSegment3d> { edge.Segment };

                foreach (var kvp in footprints)
                {
                    if (kvp.Key == edge.BeamId)
                        continue;

                    var next = new List<LineSegment3d>();
                    foreach (var s in segments)
                        next.AddRange(TrimSegmentByPolygon(s, kvp.Value));

                    segments = next;
                    if (segments.Count == 0) break;
                }

                foreach (var s in segments)
                {
                    result.Add(new BeamEdgeSegment
                    {
                        BeamId = edge.BeamId,
                        IsLeft = edge.IsLeft,
                        Segment = s
                    });
                }
            }

            return result;
        }

        // Trim a line segment by a polygon footprint
        static List<LineSegment3d> TrimSegmentByPolygon(LineSegment3d seg, Polyline poly)
        {
            var pts = new List<Point3d> { seg.StartPoint, seg.EndPoint };

            foreach (var edge in ExplodePolylineEdges(poly))
            {
                if (TryIntersect2D(seg, edge, out Point3d ip))
                    pts.Add(ip);
            }

            pts = pts.Distinct(new Point3dComparer())
                     .OrderBy(p => GetParameterAlongSegment(seg, p))
                     .ToList();


            var kept = new List<LineSegment3d>();

            for (int i = 0; i < pts.Count - 1; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                var mid = a + (b - a) * 0.5;

                if (!PointInsidePolyline(mid, poly))
                    kept.Add(new LineSegment3d(a, b));
            }

            return kept;
        }

        static double GetParameterAlongSegment(LineSegment3d seg, Point3d pt)
        {
            double dx = seg.EndPoint.X - seg.StartPoint.X;
            double dy = seg.EndPoint.Y - seg.StartPoint.Y;

            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared < 1e-12) return 0.0; // degenerate

            double t = ((pt.X - seg.StartPoint.X) * dx + (pt.Y - seg.StartPoint.Y) * dy) / lengthSquared;
            return t; // t = 0 at StartPoint, t = 1 at EndPoint
        }


        static bool TryIntersect2D(LineSegment3d a, LineSegment3d b, out Point3d ip)
        {
            ip = new Point3d();

            // Convert to 2D
            var p1 = a.StartPoint;
            var p2 = a.EndPoint;
            var q1 = b.StartPoint;
            var q2 = b.EndPoint;

            double s1x = p2.X - p1.X;
            double s1y = p2.Y - p1.Y;
            double s2x = q2.X - q1.X;
            double s2y = q2.Y - q1.Y;

            double det = (-s2x * s1y + s1x * s2y);
            if (Math.Abs(det) < 1e-10) return false; // parallel

            double s = (-s1y * (p1.X - q1.X) + s1x * (p1.Y - q1.Y)) / det;
            double t = (s2x * (p1.Y - q1.Y) - s2y * (p1.X - q1.X)) / det;

            if (s >= 0 && s <= 1 && t >= 0 && t <= 1)
            {
                ip = new Point3d(p1.X + (t * s1x), p1.Y + (t * s1y), 0);
                return true;
            }

            return false;
        }


        // Explode polyline into line segments
        static IEnumerable<LineSegment3d> ExplodePolylineEdges(Polyline pl)
        {
            for (int i = 0; i < pl.NumberOfVertices; i++)
            {
                var p0 = pl.GetPoint3dAt(i);
                var p1 = pl.GetPoint3dAt((i + 1) % pl.NumberOfVertices);
                yield return new LineSegment3d(p0, p1);
            }
        }

        // Point3d comparer for Distinct
        class Point3dComparer : IEqualityComparer<Point3d>
        {
            const double Tol = 1e-6;
            public bool Equals(Point3d a, Point3d b) => a.DistanceTo(b) < Tol;
            public int GetHashCode(Point3d p) => 0;
        }

        static bool PointInsidePolyline(Point3d pt, Polyline poly)
        {
            int crossings = 0;
            int n = poly.NumberOfVertices;

            for (int i = 0; i < n; i++)
            {
                var a = poly.GetPoint2dAt(i);
                var b = poly.GetPoint2dAt((i + 1) % n);

                // Ray casting along +X from pt
                if (((a.Y > pt.Y) != (b.Y > pt.Y)) &&
                    (pt.X < (b.X - a.X) * (pt.Y - a.Y) / (b.Y - a.Y + 1e-12) + a.X))
                {
                    crossings++;
                }
            }

            return (crossings % 2) == 1; // odd = inside, even = outside
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


        #region --- Step 1 ---

        #endregion

        #region --- Step 2 Create Offset Edges ---

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

        #endregion

        #region --- Step 3 Trime gradebeam edges at intersections---


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






        #endregion

        #region --- Step 4 Join edge segments if they are on the same polyline---


        #endregion

        #region --- Step 5 ---

        #endregion

        #region --- Step 6 ---

        #endregion
    }
}
