using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using FoundationDetailsLibraryAutoCAD.AutoCAD;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace FoundationDetailer.AutoCAD
{
    public static class AutoCADAdapter
    {
        private static Polyline CreatePolyline(System.Collections.Generic.List<Point3d> pts, double elevation)
        {
            Polyline pl = new Polyline();
            for (int i = 0; i < pts.Count; i++)
                pl.AddVertexAt(i, new Autodesk.AutoCAD.Geometry.Point2d(pts[i].X, pts[i].Y), 0, 0, 0);
            pl.Closed = true;
            pl.Elevation = elevation;
            return pl;
        }

        private static Polyline CreateRectangle(Point3d center, double widthIn, double depthIn)
        {
            double hx = widthIn / 2.0;
            double hy = depthIn / 2.0;
            var pts = new System.Collections.Generic.List<Point3d>
            {
                new Point3d(center.X - hx, center.Y - hy, center.Z),
                new Point3d(center.X + hx, center.Y - hy, center.Z),
                new Point3d(center.X + hx, center.Y + hy, center.Z),
                new Point3d(center.X - hx, center.Y + hy, center.Z)
            };
            return CreatePolyline(pts, center.Z);
        }
    }

    /// <summary>
    /// Helper function to make the drawing area active immediately.  Useful for immediate response during button clicks in the Palette.
    /// </summary>
    public static class AcadFocusHelper
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        /// <summary>
        /// Brings AutoCAD main window to the foreground.
        /// </summary>
        public static void FocusAutoCADWindow()
        {
            IntPtr hwnd = FindWindow("AcCtrlFrame", null);
            if (hwnd != IntPtr.Zero)
                SetForegroundWindow(hwnd);
        }
    }
}
