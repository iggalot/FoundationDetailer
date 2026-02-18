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
        /// Trims a polyline to a closed boundary polyline.
        /// Returns a list of resulting polylines (may be multiple).
        /// Returns empty list if fully outside.
        /// </summary>

            private static readonly Tolerance Tol = new Tolerance(1e-8, 1e-8);

            public static List<Polyline> TrimPolylineToPolyline(
                Polyline source,
                Polyline boundary)
            {
                var results = new List<Polyline>();

                if (source == null)
                    return results;

                if (boundary == null || !boundary.Closed || boundary.NumberOfVertices < 3)
                {
                    results.Add(source);
                    return results;
                }

                Curve boundaryCurve = boundary;

                var currentPts = new List<Point2d>();

                int segCount = source.Closed
                    ? source.NumberOfVertices
                    : source.NumberOfVertices - 1;

                for (int i = 0; i < segCount; i++)
                {
                    int next = (i + 1) % source.NumberOfVertices;

                    Point3d p0 = source.GetPoint3dAt(i);
                    Point3d p1 = source.GetPoint3dAt(next);

                    using (Line segment = new Line(p0, p1))
                    {
                        var pieces = SplitAndClean(segment, boundaryCurve);

                        foreach (Curve piece in pieces)
                        {
                            if (IsInsideOrOnBoundary(piece, boundary))
                            {
                                var sp = piece.StartPoint;
                                var ep = piece.EndPoint;

                                var p2d0 = new Point2d(sp.X, sp.Y);
                                var p2d1 = new Point2d(ep.X, ep.Y);

                                if (currentPts.Count == 0)
                                    currentPts.Add(p2d0);

                                currentPts.Add(p2d1);
                            }
                            else
                            {
                                Flush(results, currentPts);
                            }

                            piece.Dispose();
                        }
                    }
                }

                Flush(results, currentPts);

                return results;
            }

            // ---------------------------------------------
            // Robust curve splitting
            // ---------------------------------------------
            private static List<Curve> SplitAndClean(Curve curve, Curve boundary)
            {
                var intersectionPts = new Point3dCollection();

                curve.IntersectWith(
                    boundary,
                    Intersect.OnBothOperands,
                    intersectionPts,
                    IntPtr.Zero,
                    IntPtr.Zero);

                // Remove duplicates using tolerance
                var uniquePts = intersectionPts
                    .Cast<Point3d>()
                    .Distinct(new Point3dTolComparer(Tol.EqualPoint))
                    .ToList();

                // Handle full collinear overlap case
                if (uniquePts.Count == 0)
                {
                    // Could be fully inside, fully outside, or fully on boundary
                    return new List<Curve> { curve.Clone() as Curve };
                }

                var parameters = new DoubleCollection();

                foreach (var pt in uniquePts)
                {
                    try
                    {
                        double param = curve.GetParameterAtPoint(pt);
                        parameters.Add(param);
                    }
                    catch { }
                }

                if (parameters.Count == 0)
                    return new List<Curve> { curve.Clone() as Curve };

                var pieces = curve.GetSplitCurves(parameters);
                return pieces.Cast<Curve>().ToList();
            }

            // ---------------------------------------------
            // Inside test (treat boundary as inside)
            // ---------------------------------------------
            private static bool IsInsideOrOnBoundary(Curve piece, Polyline boundary)
            {
                double midParam = (piece.StartParam + piece.EndParam) / 2.0;
                Point3d mid = piece.GetPointAtParameter(midParam);

                var pt2d = new Point2d(mid.X, mid.Y);

                if (IsPointOnBoundary(pt2d, boundary))
                    return true;

                return IsPointInside(boundary, pt2d);
            }

        private static bool IsPointOnBoundary(Point2d pt, Polyline boundary)
        {
            Point3d testPt = new Point3d(pt.X, pt.Y, 0.0);

            for (int i = 0; i < boundary.NumberOfVertices; i++)
            {
                Point3d a = boundary.GetPoint3dAt(i);
                Point3d b = boundary.GetPoint3dAt((i + 1) % boundary.NumberOfVertices);

                Vector3d ab = b - a;
                Vector3d ap = testPt - a;

                double abLengthSq = ab.DotProduct(ab);
                if (abLengthSq < 1e-16)
                    continue; // Degenerate edge

                // Project point onto edge
                double t = ap.DotProduct(ab) / abLengthSq;

                // Check if projection lies within segment
                if (t < -1e-8 || t > 1.0 + 1e-8)
                    continue;

                Point3d projection = a + ab * t;

                // Check distance to projected point
                if (projection.DistanceTo(testPt) <= 1e-8)
                    return true;
            }

            return false;
        }


        private static bool IsPointInside(Polyline poly, Point2d pt)
            {
                int crossings = 0;

                for (int i = 0; i < poly.NumberOfVertices; i++)
                {
                    var a = poly.GetPoint2dAt(i);
                    var b = poly.GetPoint2dAt((i + 1) % poly.NumberOfVertices);

                    if (((a.Y > pt.Y) != (b.Y > pt.Y)) &&
                        (pt.X < (b.X - a.X) * (pt.Y - a.Y) / (b.Y - a.Y + 1e-12) + a.X))
                    {
                        crossings++;
                    }
                }

                return (crossings % 2) == 1;
            }

            private static void Flush(List<Polyline> results, List<Point2d> pts)
            {
                if (pts.Count < 2)
                {
                    pts.Clear();
                    return;
                }

                var pl = new Polyline();

                for (int i = 0; i < pts.Count; i++)
                    pl.AddVertexAt(i, pts[i], 0, 0, 0);

                results.Add(pl);
                pts.Clear();
            }

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
