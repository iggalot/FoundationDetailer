using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FoundationDetailsLibraryAutoCAD.Managers
{
    public class MathHelperManager
    {
        public static double ComputePolylineArea(Polyline pl)
        {
            if (pl == null || pl.NumberOfVertices < 3)
                return 0.0;

            double area = 0.0;

            for (int i = 0; i < pl.NumberOfVertices; i++)
            {
                Point2d p1 = pl.GetPoint2dAt(i);
                Point2d p2 = pl.GetPoint2dAt((i + 1) % pl.NumberOfVertices);
                area += (p1.X * p2.Y) - (p2.X * p1.Y);
            }

            return Math.Abs(area / 2.0);
        }

        public static double ComputePolylineLength(Polyline pl)
        {
            double length = 0.0;
            int vertexCount = pl.NumberOfVertices;

            for (int i = 0; i < vertexCount - 1; i++)
            {
                Point2d p1 = pl.GetPoint2dAt(i);
                Point2d p2 = pl.GetPoint2dAt(i + 1);

                double dx = p2.X - p1.X;
                double dy = p2.Y - p1.Y;

                double segmentLength = Math.Sqrt(dx * dx + dy * dy);

                // Apply bulge if needed
                double bulge = pl.GetBulgeAt(i);
                if (Math.Abs(bulge) > 1e-9)
                {
                    // Arc length formula for bulge
                    double chord = segmentLength;
                    double alpha = 4 * Math.Atan(Math.Abs(bulge));
                    double radius = chord / (2 * Math.Sin(alpha / 2));
                    segmentLength = Math.Abs(alpha * radius);
                }

                length += segmentLength;
            }

            return length / 12.0;
        }

        /// <summary>
        /// Clips a line in the given direction through origin to the bounding box extents.
        /// Returns the two intersection points on the box.
        /// </summary>
        public static bool TryClipLineToBoundingBoxExtents(
            Point3d origin,
            Vector3d dir,
            Extents3d ext,
            out Point3d p1,
            out Point3d p2)
        {
            const double eps = 1e-9;
            p1 = Point3d.Origin;
            p2 = Point3d.Origin;

            var ts = new List<double>();

            // Vertical boundaries (X = minX / maxX)
            if (Math.Abs(dir.X) > eps)
            {
                ts.Add((ext.MinPoint.X - origin.X) / dir.X);
                ts.Add((ext.MaxPoint.X - origin.X) / dir.X);
            }

            // Horizontal boundaries (Y = minY / maxY)
            if (Math.Abs(dir.Y) > eps)
            {
                ts.Add((ext.MinPoint.Y - origin.Y) / dir.Y);
                ts.Add((ext.MaxPoint.Y - origin.Y) / dir.Y);
            }

            // Evaluate intersection candidates
            var points = new List<Point3d>();
            foreach (double t in ts)
            {
                var pt = origin + dir * t;
                if (pt.X >= ext.MinPoint.X - eps &&
                    pt.X <= ext.MaxPoint.X + eps &&
                    pt.Y >= ext.MinPoint.Y - eps &&
                    pt.Y <= ext.MaxPoint.Y + eps)
                {
                    points.Add(pt);
                }
            }

            if (points.Count < 2)
                return false;

            // Return the two points
            p1 = points[0];
            p2 = points[1];
            return true;
        }

        /// <summary>
        /// Clips a line in the given direction through origin to the polyline boundary.
        /// Returns the two intersection points on the polyline.
        /// </summary>
        public static bool TryClipLineToPolyline(
            Point3d origin,
            Vector3d dir,
            Polyline pl,
            out Point3d p1,
            out Point3d p2)
        {
            const double eps = 1e-9;
            p1 = Point3d.Origin;
            p2 = Point3d.Origin;

            if (pl == null || pl.NumberOfVertices < 2)
                return false;

            var intersections = new List<Point3d>();

            // Loop through all polyline segments
            for (int i = 0; i < pl.NumberOfVertices - 1; i++)
            {
                Point3d a = pl.GetPoint3dAt(i);
                Point3d b = pl.GetPoint3dAt(i + 1);

                if (TryIntersectLineSegment(origin, dir, a, b, out Point3d pt))
                {
                    intersections.Add(pt);
                }
            }

            // If polyline is closed, include last segment
            if (pl.Closed)
            {
                if (TryIntersectLineSegment(origin, dir, pl.GetPoint3dAt(pl.NumberOfVertices - 1), pl.GetPoint3dAt(0), out Point3d pt))
                    intersections.Add(pt);
            }

            if (intersections.Count < 2)
                return false;

            // Sort points along the line direction
            intersections.Sort((x, y) =>
            {
                double dx = (x - origin).DotProduct(dir);
                double dy = (y - origin).DotProduct(dir);
                return dx.CompareTo(dy);
            });

            // Return the two outermost points along the line
            p1 = intersections.First();
            p2 = intersections.Last();
            return true;
        }

        /// <summary>
        /// Clips a polyline to a rectangular bounding box. Vertices outside the box are trimmed.
        /// Only segments that cross the box are trimmed; vertices entirely outside are removed.
        /// </summary>
        public static Polyline ClipPolylineToBoundingBox(Polyline input, Extents3d ext)
        {
            if (input == null || input.NumberOfVertices < 2)
                return null;

            Polyline result = new Polyline();
            int outIndex = 0;

            bool prevInside = false;
            Point3d prevPoint = input.GetPoint3dAt(0);
            prevInside = IsPointInsideExtents(prevPoint, ext);

            for (int i = 1; i < input.NumberOfVertices; i++)
            {
                Point3d currPoint = input.GetPoint3dAt(i);
                bool currInside = IsPointInsideExtents(currPoint, ext);

                if (prevInside && currInside)
                {
                    // Segment fully inside, keep current point
                    result.AddVertexAt(outIndex++, new Point2d(currPoint.X, currPoint.Y), 0.0, 0.0, 0.0);
                }
                else if (prevInside && !currInside)
                {
                    // Segment leaving the box: trim end
                    if (TryClipLineToBoundingBoxExtents(prevPoint, currPoint - prevPoint, ext, out Point3d p1, out Point3d p2))
                    {
                        result.AddVertexAt(outIndex++, new Point2d(p2.X, p2.Y), 0.0, 0.0, 0.0);
                    }
                }
                else if (!prevInside && currInside)
                {
                    // Segment entering the box: trim start
                    if (TryClipLineToBoundingBoxExtents(prevPoint, currPoint - prevPoint, ext, out Point3d p1, out Point3d p2))
                    {
                        result.AddVertexAt(outIndex++, new Point2d(p1.X, p1.Y), 0.0, 0.0, 0.0);
                        result.AddVertexAt(outIndex++, new Point2d(currPoint.X, currPoint.Y), 0.0, 0.0, 0.0);
                    }
                }
                // else both outside: ignore segment

                prevPoint = currPoint;
                prevInside = currInside;
            }

            if (result.NumberOfVertices < 2)
                return null;

            return result;
        }

        /// <summary>
        /// Trims a source polyline to a closed boundary polyline.
        /// Returns a list of one or more polylines fully inside the boundary.
        /// Segments fully outside are discarded, segments crossing the boundary are split.
        /// </summary>
        public static List<Polyline> TrimPolylineToPolyline(Polyline source, Polyline boundary)
        {
            var results = new List<Polyline>();
            if (source == null || boundary == null || !boundary.Closed || boundary.NumberOfVertices < 3)
                return results;

            // --- Step 1: Collect all segments of the source polyline
            var sourceSegments = new List<Line>();
            int segCount = source.Closed ? source.NumberOfVertices : source.NumberOfVertices - 1;
            for (int i = 0; i < segCount; i++)
            {
                int next = (i + 1) % source.NumberOfVertices;
                sourceSegments.Add(new Line(source.GetPoint3dAt(i), source.GetPoint3dAt(next)));
            }

            // --- Step 2: Split each segment at intersections with the boundary
            var insideSegments = new List<Line>();
            Curve boundaryCurve = boundary;

            foreach (var seg in sourceSegments)
            {
                var intersections = new List<Point3d>();

                // Check intersections with boundary segments
                int n = boundary.NumberOfVertices;
                for (int i = 0; i < n; i++)
                {
                    Point3d a = boundary.GetPoint3dAt(i);
                    Point3d b = boundary.GetPoint3dAt((i + 1) % n);
                    if (TryIntersectLineSegment(seg.StartPoint, seg.EndPoint, a, b, out Point3d ip))
                    {
                        intersections.Add(ip);
                    }
                }

                // Include endpoints if they are inside or on the boundary
                if (IsPointInsideOrOn(seg.StartPoint, boundary)) intersections.Add(seg.StartPoint);
                if (IsPointInsideOrOn(seg.EndPoint, boundary)) intersections.Add(seg.EndPoint);

                // --- Step 3: Sort intersections along segment
                intersections = intersections
                    .Distinct(new Point3dComparer(1e-8))
                    .OrderBy(p => (p - seg.StartPoint).Length)
                    .ToList();

                // --- Step 4: Create sub-segments and test midpoint inside
                for (int i = 0; i < intersections.Count - 1; i++)
                {
                    var p0 = intersections[i];
                    var p1 = intersections[i + 1];
                    if (p0.DistanceTo(p1) < 1e-9) continue; // skip zero-length

                    var mid = new Point3d(
                        (p0.X + p1.X) / 2.0,
                        (p0.Y + p1.Y) / 2.0,
                        (p0.Z + p1.Z) / 2.0
                    );

                    if (IsPointInsideOrOn(mid, boundary))
                    {
                        insideSegments.Add(new Line(p0, p1));
                    }
                }
            }

            // --- Step 5: Merge consecutive inside segments into polylines
            var currentPts = new List<Point2d>();
            Point3d? lastEnd = null;

            foreach (var seg in insideSegments.OrderBy(s => s.StartPoint.X).ThenBy(s => s.StartPoint.Y))
            {
                if (lastEnd == null || seg.StartPoint.DistanceTo(lastEnd.Value) > 1e-6)
                {
                    // New polyline
                    if (currentPts.Count > 1)
                        results.Add(PolylineFromPoints(currentPts));

                    currentPts.Clear();
                    currentPts.Add(new Point2d(seg.StartPoint.X, seg.StartPoint.Y));
                }
                currentPts.Add(new Point2d(seg.EndPoint.X, seg.EndPoint.Y));
                lastEnd = seg.EndPoint;
            }

            if (currentPts.Count > 1)
                results.Add(PolylineFromPoints(currentPts));

            // --- Cleanup
            foreach (var l in sourceSegments) l.Dispose();

            return results;
        }

        private static Polyline PolylineFromPoints(List<Point2d> pts)
        {
            Polyline pl = new Polyline();
            for (int i = 0; i < pts.Count; i++)
                pl.AddVertexAt(i, pts[i], 0, 0, 0);
            return pl;
        }

        // Simple point-in-polyline test using Ray Casting (2D)
        private static bool IsPointInsideOrOn(Point3d pt, Polyline boundary)
        {
            var p2d = new Point2d(pt.X, pt.Y);
            bool inside = false;
            int n = boundary.NumberOfVertices;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Point2d vi = boundary.GetPoint2dAt(i);
                Point2d vj = boundary.GetPoint2dAt(j);

                if (((vi.Y > p2d.Y) != (vj.Y > p2d.Y)) &&
                    (p2d.X < (vj.X - vi.X) * (p2d.Y - vi.Y) / (vj.Y - vi.Y + 1e-12) + vi.X))
                {
                    inside = !inside;
                }

                // On edge check
                double cross = Math.Abs((vj.X - vi.X) * (p2d.Y - vi.Y) - (vj.Y - vi.Y) * (p2d.X - vi.X));
                double len = Math.Sqrt((vj.X - vi.X) * (vj.X - vi.X) + (vj.Y - vi.Y) * (vj.Y - vi.Y));
                if (len > 0 && cross / len < 1e-9)
                    return true; // exactly on boundary
            }
            return inside;
        }

        // Segment intersection utility
        private static bool TryIntersectLineSegment(Point3d p1, Point3d p2, Point3d q1, Point3d q2, out Point3d ip)
        {
            ip = Point3d.Origin;

            Vector2d r = new Vector2d(p2.X - p1.X, p2.Y - p1.Y);
            Vector2d s = new Vector2d(q2.X - q1.X, q2.Y - q1.Y);
            double rxs = r.X * s.Y - r.Y * s.X;
            double qpxr = (q1.X - p1.X) * r.Y - (q1.Y - p1.Y) * r.X;
            if (Math.Abs(rxs) < 1e-12) return false; // parallel

            double t = ((q1.X - p1.X) * s.Y - (q1.Y - p1.Y) * s.X) / rxs;
            double u = qpxr / rxs;
            if (t >= -1e-12 && t <= 1 + 1e-12 && u >= -1e-12 && u <= 1 + 1e-12)
            {
                ip = new Point3d(p1.X + t * r.X, p1.Y + t * r.Y, 0);
                return true;
            }
            return false;
        }

        // Comparer for Point3d deduplication
        private class Point3dComparer : IEqualityComparer<Point3d>
        {
            private readonly double _eps;
            public Point3dComparer(double eps) { _eps = eps; }
            public bool Equals(Point3d a, Point3d b) => a.DistanceTo(b) < _eps;
            public int GetHashCode(Point3d obj) => 0; // not used
        }

        private static readonly Tolerance Tol = new Tolerance(1e-8, 1e-8);







        // ---------------------------------------------
        // Tolerance comparer
        // ---------------------------------------------
        private class Point3dTolComparer : IEqualityComparer<Point3d>
        {
            private readonly double _tol;

            public Point3dTolComparer(double tol)
            {
                _tol = tol;
            }

            public bool Equals(Point3d a, Point3d b)
            {
                return a.DistanceTo(b) <= _tol;
            }

            public int GetHashCode(Point3d obj)
            {
                return 0;
            }
        }

        // --- Helpers ---
        private static bool IsPointInsideExtents(Point3d pt, Extents3d ext)
        {
            return pt.X >= ext.MinPoint.X && pt.X <= ext.MaxPoint.X &&
                   pt.Y >= ext.MinPoint.Y && pt.Y <= ext.MaxPoint.Y;
        }







        /// <summary>
        /// Checks intersection of infinite line (origin + t*dir) with a segment [a,b].
        /// Returns true if they intersect and outputs the intersection point.
        /// </summary>
        private static bool TryIntersectLineSegment(Point3d origin, Vector3d dir, Point3d a, Point3d b, out Point3d intersection)
        {
            intersection = Point3d.Origin;

            // Represent segment as vector
            Vector3d seg = b - a;

            // Solve for t (line) and u (segment)
            double det = dir.X * seg.Y - dir.Y * seg.X;

            if (Math.Abs(det) < 1e-9)
                return false; // Parallel lines

            double t = ((a.X - origin.X) * seg.Y - (a.Y - origin.Y) * seg.X) / det;
            double u = ((a.X - origin.X) * dir.Y - (a.Y - origin.Y) * dir.X) / det;

            if (u < -1e-9 || u > 1.0 + 1e-9)
                return false; // Intersection is outside segment

            // Intersection point
            intersection = origin + dir * t;
            return true;
        }

        /// <summary>
        /// Creates a new polyline offset from the original by a specified distance.
        /// Positive = to the right of the path, negative = left (using AutoCAD polyline normals)
        /// </summary>
        internal static Polyline CreateOffsetPolyline(Polyline centerline, double offsetDistance)
        {
            if (centerline == null) return null;

            // Use the built-in Polyline.GetOffsetCurves
            var offsetCurves = centerline.GetOffsetCurves(offsetDistance);
            if (offsetCurves.Count == 0) return null;

            // Expecting a single polyline result
            var offsetPl = offsetCurves[0] as Polyline;
            return offsetPl;
        }

    }
}
