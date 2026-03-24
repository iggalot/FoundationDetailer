using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD
{
    public static class GradeBeamBuilder
    {
        public const double DEFAULT_BEAM_WIDTH_IN = 10.0;
        public const double DEFAULT_BEAM_DEPTH_IN = 28.0;

        // Beam edge segment data structure
        internal class BeamEdgeSegment
        {
            public ObjectId BeamId;
            public bool IsLeft;
            public LineSegment3d Segment;
        }

        // Offset polyline safely
        internal static Polyline OffsetPolyline(Polyline pl, double offset)
        {
            var curves = pl.GetOffsetCurves(offset);
            return (Polyline)curves[0];
        }

        // Build beam footprint polygon from centerline + width
        internal static Polyline BuildFootprint(Polyline centerline, double halfWidth)
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
        internal static List<BeamEdgeSegment> ExplodeEdges(ObjectId beamId, Polyline edge, bool isLeft)
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
        internal static List<BeamEdgeSegment> TrimAllEdges(
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
        internal static List<LineSegment3d> TrimSegmentByPolygon(LineSegment3d seg, Polyline poly)
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

        internal static double GetParameterAlongSegment(LineSegment3d seg, Point3d pt)
        {
            double dx = seg.EndPoint.X - seg.StartPoint.X;
            double dy = seg.EndPoint.Y - seg.StartPoint.Y;

            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared < 1e-12) return 0.0; // degenerate

            double t = ((pt.X - seg.StartPoint.X) * dx + (pt.Y - seg.StartPoint.Y) * dy) / lengthSquared;
            return t; // t = 0 at StartPoint, t = 1 at EndPoint
        }


        internal static bool TryIntersect2D(LineSegment3d a, LineSegment3d b, out Point3d ip)
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
        internal static IEnumerable<LineSegment3d> ExplodePolylineEdges(Polyline pl)
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

        internal static bool PointInsidePolyline(Point3d pt, Polyline poly)
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
    }
}
