using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using FoundationDetailer.Model;
using System.Collections.Generic;

namespace FoundationDetailer.AutoCAD
{
    public static class PreviewManager
    {
        private static readonly List<Entity> _transients = new List<Entity>();

        public static void ShowPreview(FoundationModel model)
        {
            ClearPreview();
            if (model == null) return;

            // Boundaries
            foreach (var b in model.Boundaries)
            {
                var pl = CreateDbPolyline(b.Points, b.Elevation);
                pl.ColorIndex = 7; // white
                _AddTransient(pl);
            }

            // Piers
            foreach (var p in model.Piers)
            {
                Entity ent = p.IsCircular
                    ? (Entity)new Circle(p.Location, Vector3d.ZAxis, p.DiameterIn / 2.0)
                    : (Entity)CreateDbRectangle(p.Location, p.WidthIn, p.DepthIn);
                ent.ColorIndex = 3;
                _AddTransient(ent);
            }

            // Grade beams
            foreach (var gb in model.GradeBeams)
            {
                var ln = new Line(gb.Start, gb.End) { ColorIndex = 5 };
                _AddTransient(ln);
            }

            // Rebars
            foreach (var r in model.Rebars)
            {
                var ln = new Line(r.Start, r.End) { ColorIndex = 1 };
                _AddTransient(ln);
            }

            // Strands
            foreach (var s in model.Strands)
            {
                var ln = new Line(s.Start, s.End) { ColorIndex = 6 };
                _AddTransient(ln);
            }

            // Slopes / drops / curbs as polylines
            foreach (var slope in model.Slopes)
            {
                var pl = CreateDbPolyline(slope.Boundary, 0);
                pl.ColorIndex = 2;
                _AddTransient(pl);
            }

            foreach (var drop in model.Drops)
            {
                var pl = CreateDbPolyline(drop.Boundary, -drop.DepthIn);
                pl.ColorIndex = 4;
                _AddTransient(pl);
            }

            foreach (var curb in model.Curbs)
            {
                var pl = CreateDbPolyline(curb.Boundary, 0);
                pl.ColorIndex = 8;
                _AddTransient(pl);
            }
        }

        public static void ClearPreview()
        {
            var tm = TransientManager.CurrentTransientManager;
            var ints = new IntegerCollection();
            foreach (var ent in _transients)
            {
                tm.EraseTransient(ent, ints);
                ent.Dispose();
            }
            _transients.Clear();
        }

        #region helpers
        private static Autodesk.AutoCAD.DatabaseServices.Polyline CreateDbPolyline(List<Point3d> pts, double elevation)
        {
            var pl = new Autodesk.AutoCAD.DatabaseServices.Polyline();
            for (int i = 0; i < pts.Count; i++)
                pl.AddVertexAt(i, new Autodesk.AutoCAD.Geometry.Point2d(pts[i].X, pts[i].Y), 0, 0, 0);
            pl.Closed = true;
            pl.Elevation = elevation;
            return pl;
        }

        private static Autodesk.AutoCAD.DatabaseServices.Polyline CreateDbRectangle(Point3d center, double widthIn, double depthIn)
        {
            double hx = widthIn / 2.0;
            double hy = depthIn / 2.0;
            var pts = new List<Point3d>
            {
                new Point3d(center.X - hx, center.Y - hy, center.Z),
                new Point3d(center.X + hx, center.Y - hy, center.Z),
                new Point3d(center.X + hx, center.Y + hy, center.Z),
                new Point3d(center.X - hx, center.Y + hy, center.Z)
            };
            return CreateDbPolyline(pts, center.Z);
        }

        private static void _AddTransient(Entity ent)
        {
            TransientManager.CurrentTransientManager.AddTransient(
                ent,
                TransientDrawingMode.DirectShortTerm,
                128,
                new IntegerCollection()
            );
            _transients.Add(ent);
        }
        #endregion
    }
}
