using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using FoundationDetailer.Model;
using System.Collections.Generic;

using DB = Autodesk.AutoCAD.DatabaseServices;

namespace FoundationDetailer.AutoCAD
{
    public static class PreviewManager
    {
        // Store all transient DB entities
        private static List<Entity> _transients = new List<Entity>();

        /// <summary>
        /// Show a transient preview of the foundation model.
        /// </summary>
        public static void ShowPreview(FoundationModel model)
        {
            ClearPreview();

            // Boundaries
            foreach (var b in model.Boundaries)
            {
                DB.Polyline pl = CreatePolyline(b.Points, b.Elevation);
                AddTransient(pl);
            }

            // Piers
            foreach (var pier in model.Piers)
            {
                Entity ent = pier.IsCircular
                    ? (Entity)new Circle(pier.Location, Vector3d.ZAxis, pier.Diameter / 2)
                    : (Entity)CreateRectangle(pier.Location, pier.Width, pier.Depth);
                AddTransient(ent);
            }

            // Grade Beams
            foreach (var gb in model.GradeBeams)
            {
                Line line = new Line(gb.Start, gb.End);
                AddTransient(line);
            }

            // Rebars
            foreach (var r in model.Rebars)
            {
                Line line = new Line(r.Start, r.End);
                AddTransient(line);
            }

            // Strands
            foreach (var s in model.Strands)
            {
                Line line = new Line(s.Start, s.End);
                AddTransient(line);
            }

            // Slopes
            foreach (var slope in model.Slopes)
            {
                DB.Polyline pl = CreatePolyline(slope.Boundary, 0);
                AddTransient(pl);
            }

            // Drops
            foreach (var drop in model.Drops)
            {
                DB.Polyline pl = CreatePolyline(drop.Boundary, -drop.Depth);
                AddTransient(pl);
            }

            // Curbs
            foreach (var curb in model.Curbs)
            {
                DB.Polyline pl = CreatePolyline(curb.Boundary, 0);
                AddTransient(pl);
            }
        }

        /// <summary>
        /// Clear all transient preview graphics.
        /// </summary>
        public static void ClearPreview()
        {
            foreach (var ent in _transients)
            {
                TransientManager.CurrentTransientManager.EraseTransient(
                    ent,
                    new IntegerCollection()
                );
                ent.Dispose();
            }
            _transients.Clear();
        }

        #region --- Helper Methods ---

        private static DB.Polyline CreatePolyline(List<Point3d> points, double elevation)
        {
            DB.Polyline pl = new DB.Polyline();
            for (int i = 0; i < points.Count; i++)
            {
                var pt = points[i];
                pl.AddVertexAt(i, new Autodesk.AutoCAD.Geometry.Point2d(pt.X, pt.Y), 0, 0, 0);
            }
            pl.Closed = true;
            pl.Elevation = elevation;
            return pl;
        }

        private static DB.Polyline CreateRectangle(Point3d center, double width, double depth)
        {
            double hx = width / 2.0;
            double hy = depth / 2.0;
            List<Point3d> pts = new List<Point3d>
            {
                new Point3d(center.X - hx, center.Y - hy, center.Z),
                new Point3d(center.X + hx, center.Y - hy, center.Z),
                new Point3d(center.X + hx, center.Y + hy, center.Z),
                new Point3d(center.X - hx, center.Y + hy, center.Z)
            };
            return CreatePolyline(pts, center.Z);
        }

        private static void AddTransient(Entity ent)
        {
            // Optionally assign color/layer for preview
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
