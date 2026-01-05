using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System;

internal sealed class PolylineTreeItemViewModel
{
    public string Handle { get; }
    public double TotalLength { get; }

    public PolylineTreeItemViewModel(Polyline pl)
    {
        Handle = pl.Handle.ToString();
        TotalLength = CalculatePolylineLength(pl);
    }

    internal static double CalculatePolylineLength(Polyline pl)
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

        return length;
    }
}
