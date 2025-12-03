using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.DatabaseServices;
using System;
using System.Collections.Generic;

namespace FoundationDetailsLibraryAutoCAD.Managers
{
    public static class GridlineManager
    {
        public static (List<List<Point3d>> Horizontal, List<List<Point3d>> Vertical)
            ComputeBothGridlines(Polyline pl, double maxSpacing, int vertexCount)
        {
            ValidateInputs(pl, maxSpacing, vertexCount);

            return (
                ComputeGridlines(pl, maxSpacing, vertexCount, horizontal: true),
                ComputeGridlines(pl, maxSpacing, vertexCount, horizontal: false)
            );
        }

        // -------------------------
        // Internal Helpers
        // -------------------------

        private static void ValidateInputs(Polyline pl, double maxSpacing, int vertexCount)
        {
            if (pl == null) throw new ArgumentNullException(nameof(pl));
            if (!pl.Closed) throw new ArgumentException("Polyline must be closed.", nameof(pl));
            if (maxSpacing <= 0) throw new ArgumentException("Max spacing must be > 0.", nameof(maxSpacing));
            if (vertexCount < 2) throw new ArgumentException("vertexCount must be >= 2.", nameof(vertexCount));
        }

        private static List<List<Point3d>> ComputeGridlines(Polyline pl, double maxSpacing, int vertexCount, bool horizontal)
        {
            var ext = pl.GeometricExtents;
            double minX = ext.MinPoint.X, maxX = ext.MaxPoint.X;
            double minY = ext.MinPoint.Y, maxY = ext.MaxPoint.Y;

            double length = horizontal ? (maxY - minY) : (maxX - minX);
            if (length <= 0) return new List<List<Point3d>>();

            // Compute number of intervals: largest spacing <= maxSpacing
            int intervals = (int)Math.Ceiling(length / maxSpacing); // minimal number of spaces
            double spacing = length / intervals; // actual spacing ≤ maxSpacing

            var result = new List<List<Point3d>>(intervals + 1); // +1 for end line

            // Loop from 0 to intervals inclusive to include both ends
            for (int i = 0; i <= intervals; i++)
            {
                double c = (horizontal ? minY : minX) + i * spacing;
                Point3d start = horizontal ? new Point3d(minX, c, 0) : new Point3d(c, minY, 0);
                Point3d end = horizontal ? new Point3d(maxX, c, 0) : new Point3d(c, maxY, 0);
                result.Add(SubdivideLine(start, end, vertexCount));
            }

            return result;
        }

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
