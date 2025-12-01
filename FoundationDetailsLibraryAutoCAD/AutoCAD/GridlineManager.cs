using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;

namespace FoundationDetailer.Utilities
{
    public class GridLineManager
    {
        private readonly Polyline _boundary;
        private readonly double _maxSpacing;
        private readonly int _minVerticesPerLine = 5;

        public GridLineManager(Polyline boundary, double maxSpacing, int minVerticesPerLine = 5)
        {
            _boundary = boundary ?? throw new ArgumentNullException(nameof(boundary));
            if (maxSpacing <= 0) throw new ArgumentException("Max spacing must be greater than zero");

            _maxSpacing = maxSpacing;
            _minVerticesPerLine = minVerticesPerLine;
        }

        private Extents2d GetBoundingBox()
        {
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;

            for (int i = 0; i < _boundary.NumberOfVertices; i++)
            {
                var pt = _boundary.GetPoint2dAt(i);
                if (pt.X < minX) minX = pt.X;
                if (pt.Y < minY) minY = pt.Y;
                if (pt.X > maxX) maxX = pt.X;
                if (pt.Y > maxY) maxY = pt.Y;
            }

            return new Extents2d(new Point2d(minX, minY), new Point2d(maxX, maxY));
        }

        /// <summary>
        /// Generates vertical lines subdivided into points along the segment, using max spacing.
        /// </summary>
        public List<Polyline> GetVerticalPolylines()
        {
            var polylines = new List<Polyline>();
            var ext = GetBoundingBox();
            double width = ext.MaxPoint.X - ext.MinPoint.X;
            int numLines = (int)Math.Floor(width / _maxSpacing);

            if (numLines < 1) numLines = 1;
            double spacing = width / (numLines + 1);

            for (int i = 1; i <= numLines; i++)
            {
                double x = ext.MinPoint.X + i * spacing;
                var pl = new Polyline();
                for (int j = 0; j < _minVerticesPerLine; j++)
                {
                    double y = ext.MinPoint.Y + j * (ext.MaxPoint.Y - ext.MinPoint.Y) / (_minVerticesPerLine - 1);
                    pl.AddVertexAt(j, new Point2d(x, y), 0, 0, 0);
                }
                pl.Closed = false;
                polylines.Add(pl);
            }

            return polylines;
        }

        /// <summary>
        /// Generates horizontal lines subdivided into points along the segment, using max spacing.
        /// </summary>
        public List<Polyline> GetHorizontalPolylines()
        {
            var polylines = new List<Polyline>();
            var ext = GetBoundingBox();
            double height = ext.MaxPoint.Y - ext.MinPoint.Y;
            int numLines = (int)Math.Floor(height / _maxSpacing);

            if (numLines < 1) numLines = 1;
            double spacing = height / (numLines + 1);

            for (int i = 1; i <= numLines; i++)
            {
                double y = ext.MinPoint.Y + i * spacing;
                var pl = new Polyline();
                for (int j = 0; j < _minVerticesPerLine; j++)
                {
                    double x = ext.MinPoint.X + j * (ext.MaxPoint.X - ext.MinPoint.X) / (_minVerticesPerLine - 1);
                    pl.AddVertexAt(j, new Point2d(x, y), 0, 0, 0);
                }
                pl.Closed = false;
                polylines.Add(pl);
            }

            return polylines;
        }
    }
}
