using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace FoundationDetailsLibraryAutoCAD.Managers
{
    /// <summary>
    /// Generates horizontal and vertical gridlines for a closed polyline's axis-aligned bounding box.
    /// Each gridline is returned as a list of evenly-spaced Point3d vertices (inclusive of start & end).
    /// </summary>
    public static class GridlineManager
    {
        /// <summary>
        /// Generate horizontal gridlines only. Each gridline returned as List&lt;Point3d&gt; with vertexCount points.
        /// </summary>
        public static List<List<Point3d>> ComputeHorizontalGridlines(Polyline pl, double maxSpacing, int vertexCount)
        {
            ValidateInputs(pl, maxSpacing, vertexCount);

            Extents3d ext = pl.GeometricExtents;
            double minX = ext.MinPoint.X;
            double maxX = ext.MaxPoint.X;
            double minY = ext.MinPoint.Y;
            double maxY = ext.MaxPoint.Y;
            double height = maxY - minY;

            int count = (int)Math.Floor(height / maxSpacing);
            if (count < 1)
                return new List<List<Point3d>>();

            double dy = height / (count + 1);

            var result = new List<List<Point3d>>(count);
            for (int i = 1; i <= count; i++)
            {
                double y = minY + i * dy;
                var pts = SubdivideLine(new Point3d(minX, y, 0), new Point3d(maxX, y, 0), vertexCount);
                result.Add(pts);
            }

            return result;
        }

        /// <summary>
        /// Generate vertical gridlines only. Each gridline returned as List&lt;Point3d&gt; with vertexCount points.
        /// </summary>
        public static List<List<Point3d>> ComputeVerticalGridlines(Polyline pl, double maxSpacing, int vertexCount)
        {
            ValidateInputs(pl, maxSpacing, vertexCount);

            Extents3d ext = pl.GeometricExtents;
            double minX = ext.MinPoint.X;
            double maxX = ext.MaxPoint.X;
            double minY = ext.MinPoint.Y;
            double maxY = ext.MaxPoint.Y;
            double width = maxX - minX;

            int count = (int)Math.Floor(width / maxSpacing);
            if (count < 1)
                return new List<List<Point3d>>();

            double dx = width / (count + 1);

            var result = new List<List<Point3d>>(count);
            for (int j = 1; j <= count; j++)
            {
                double x = minX + j * dx;
                var pts = SubdivideLine(new Point3d(x, minY, 0), new Point3d(x, maxY, 0), vertexCount);
                result.Add(pts);
            }

            return result;
        }

        /// <summary>
        /// Generate both horizontal and vertical gridlines. Returns a tuple of (Horizontal, Vertical)
        /// where each item is a List&lt;List&lt;Point3d&gt;&gt;.
        /// </summary>
        public static (List<List<Point3d>> Horizontal, List<List<Point3d>> Vertical)
            ComputeBothGridlines(Polyline pl, double maxSpacing, int vertexCount)
        {
            // Validate once (both functions also validate but keep this explicit)
            ValidateInputs(pl, maxSpacing, vertexCount);

            var horiz = ComputeHorizontalGridlines(pl, maxSpacing, vertexCount);
            var vert = ComputeVerticalGridlines(pl, maxSpacing, vertexCount);

            return (horiz, vert);
        }

        // -------------------------
        // Internal helpers
        // -------------------------

        private static void ValidateInputs(Polyline pl, double maxSpacing, int vertexCount)
        {
            if (pl == null) throw new ArgumentNullException(nameof(pl));
            if (!pl.Closed) throw new ArgumentException("Polyline must be closed.", nameof(pl));
            if (maxSpacing <= 0) throw new ArgumentException("Max spacing must be > 0.", nameof(maxSpacing));
            if (vertexCount < 2) throw new ArgumentException("vertexCount must be >= 2.", nameof(vertexCount));
        }

        /// <summary>
        /// Return 'vertexCount' evenly spaced points along the segment [start, end], inclusive.
        /// </summary>
        private static List<Point3d> SubdivideLine(Point3d start, Point3d end, int vertexCount)
        {
            var pts = new List<Point3d>(vertexCount);

            double dx = (end.X - start.X) / (vertexCount - 1);
            double dy = (end.Y - start.Y) / (vertexCount - 1);
            double dz = (end.Z - start.Z) / (vertexCount - 1);

            for (int i = 0; i < vertexCount; i++)
            {
                pts.Add(new Point3d(
                    start.X + dx * i,
                    start.Y + dy * i,
                    start.Z + dz * i
                ));
            }

            return pts;
        }
    }
}
