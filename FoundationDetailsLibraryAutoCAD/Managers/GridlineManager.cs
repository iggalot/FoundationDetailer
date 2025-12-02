using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace FoundationDetailsLibraryAutoCAD.Managers
{
    public static class GridlineGenerator
    {
        // ============================================================
        //  PUBLIC API
        // ============================================================

        /// <summary>
        /// Generate horizontal gridlines only.
        /// </summary>
        public static List<(Point3d Start, Point3d End)>
        ComputeHorizontalGridlines(Polyline pl, double maxSpacing)
        {
            if (pl == null) throw new ArgumentNullException(nameof(pl));
            if (!pl.Closed) throw new ArgumentException("Polyline must be closed.");
            if (maxSpacing <= 0) throw new ArgumentException("Max spacing must be > 0.");

            // Get bounding extents
            Extents3d ext = pl.GeometricExtents;
            double minX = ext.MinPoint.X;
            double maxX = ext.MaxPoint.X;
            double minY = ext.MinPoint.Y;
            double maxY = ext.MaxPoint.Y;
            double height = maxY - minY;

            int count = (int)Math.Floor(height / maxSpacing);
            if (count < 1)
                return new List<(Point3d, Point3d)>(); // none needed

            double dy = height / (count + 1);

            var result = new List<(Point3d, Point3d)>();
            for (int i = 1; i <= count; i++)
            {
                double y = minY + i * dy;
                result.Add((
                    new Point3d(minX, y, 0),
                    new Point3d(maxX, y, 0)
                ));
            }

            return result;
        }


        /// <summary>
        /// Generate vertical gridlines only.
        /// </summary>
        public static List<(Point3d Start, Point3d End)>
        ComputeVerticalGridlines(Polyline pl, double maxSpacing)
        {
            if (pl == null) throw new ArgumentNullException(nameof(pl));
            if (!pl.Closed) throw new ArgumentException("Polyline must be closed.");
            if (maxSpacing <= 0) throw new ArgumentException("Max spacing must be > 0.");

            // Get bounding extents
            Extents3d ext = pl.GeometricExtents;
            double minX = ext.MinPoint.X;
            double maxX = ext.MaxPoint.X;
            double minY = ext.MinPoint.Y;
            double maxY = ext.MaxPoint.Y;
            double width = maxX - minX;

            int count = (int)Math.Floor(width / maxSpacing);
            if (count < 1)
                return new List<(Point3d, Point3d)>(); // none needed

            double dx = width / (count + 1);

            var result = new List<(Point3d, Point3d)>();
            for (int i = 1; i <= count; i++)
            {
                double x = minX + i * dx;
                result.Add((
                    new Point3d(x, minY, 0),
                    new Point3d(x, maxY, 0)
                ));
            }

            return result;
        }


        /// <summary>
        /// Generate both horizontal and vertical gridlines.
        /// </summary>
        public static (List<(Point3d Start, Point3d End)> Horizontal,
                       List<(Point3d Start, Point3d End)> Vertical)
        ComputeBothGridlines(Polyline pl, double maxSpacing)
        {
            return (
                ComputeHorizontalGridlines(pl, maxSpacing),
                ComputeVerticalGridlines(pl, maxSpacing)
            );
        }
    }
}

