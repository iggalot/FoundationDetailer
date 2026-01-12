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
    }
}
