using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FoundationDetailer.Model;
using System.Collections.Generic;

namespace FoundationDetailer.AutoCAD
{
    public static class AutoCADAdapter
    {
        /// <summary>
        /// Commit the foundation model permanently to the AutoCAD drawing.
        /// </summary>
        public static void CommitModelToDrawing(FoundationModel model)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                // --- Boundaries ---
                foreach (var b in model.Boundaries)
                {
                    Polyline pline = CreatePolyline(b.Points, b.Elevation);
                    pline.Layer = "BOUNDARY";
                    btr.AppendEntity(pline);
                    tr.AddNewlyCreatedDBObject(pline, true);
                }

                // --- Piers ---
                foreach (var pier in model.Piers)
                {
                    Entity ent;
                    if (pier.IsCircular)
                    {
                        ent = new Circle(pier.Location, Vector3d.ZAxis, pier.Diameter / 2);
                    }
                    else
                    {
                        ent = CreateRectangle(pier.Location, pier.Width, pier.Depth);
                    }
                    ent.Layer = "PIER";
                    btr.AppendEntity(ent);
                    tr.AddNewlyCreatedDBObject(ent, true);
                }

                // --- Grade Beams ---
                foreach (var gb in model.GradeBeams)
                {
                    Line line = new Line(gb.Start, gb.End) { Layer = "GRADEBEAM" };
                    btr.AppendEntity(line);
                    tr.AddNewlyCreatedDBObject(line, true);
                }

                // --- Rebar Bars ---
                foreach (var r in model.Rebars)
                {
                    Line line = new Line(r.Start, r.End)
                    {
                        Layer = r.Layer ?? "REBAR"
                    };
                    btr.AppendEntity(line);
                    tr.AddNewlyCreatedDBObject(line, true);
                }

                // --- Strands ---
                foreach (var s in model.Strands)
                {
                    Line line = new Line(s.Start, s.End)
                    {
                        Layer = s.Layer ?? "STRAND"
                    };
                    btr.AppendEntity(line);
                    tr.AddNewlyCreatedDBObject(line, true);
                }

                // --- Slopes ---
                foreach (var slope in model.Slopes)
                {
                    Polyline pl = CreatePolyline(slope.Boundary, 0);
                    pl.Layer = "SLOPE";
                    btr.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);
                }

                // --- Drops ---
                foreach (var drop in model.Drops)
                {
                    Polyline pl = CreatePolyline(drop.Boundary, -drop.Depth);
                    pl.Layer = "DROP";
                    btr.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);
                }

                // --- Curbs ---
                foreach (var curb in model.Curbs)
                {
                    Polyline pl = CreatePolyline(curb.Boundary, 0);
                    pl.Layer = "CURB";
                    btr.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);
                }

                tr.Commit();
            }
        }

        #region --- Helper Methods ---

        private static Polyline CreatePolyline(List<Point3d> points, double elevation)
        {
            Polyline pl = new Polyline();
            for (int i = 0; i < points.Count; i++)
            {
                pl.AddVertexAt(i, new Autodesk.AutoCAD.Geometry.Point2d(points[i].X, points[i].Y), 0, 0, 0);
            }
            pl.Closed = true;
            pl.Elevation = elevation;
            return pl;
        }

        private static Polyline CreateRectangle(Point3d center, double width, double depth)
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

        #endregion
    }
}
