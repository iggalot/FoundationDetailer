using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System;

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
    }
}
