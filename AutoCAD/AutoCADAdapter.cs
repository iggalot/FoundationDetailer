using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using FoundationDetailer.Model;
using System;

namespace FoundationDetailer.AutoCAD
{
    public static class AutoCADAdapter
    {
        public static void CommitModelToDrawing(FoundationModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // Boundaries
                foreach (var b in model.Boundaries)
                {
                    var pl = CreatePolyline(b.Points, b.Elevation);
                    pl.Layer = "FOUNDATION-BOUNDARY";
                    ms.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);
                    WriteExtensionXRecord(pl, "Boundary", b.Name ?? model.Id.ToString(), tr);
                }

                // Piers
                foreach (var p in model.Piers)
                {
                    Entity ent = p.IsCircular
                        ? (Entity)new Circle(p.Location, Vector3d.ZAxis, p.DiameterIn / 2.0)
                        : (Entity)CreateRectangle(p.Location, p.WidthIn, p.DepthIn);
                    ent.Layer = p.Layer ?? "FOUNDATION-PIER";
                    ms.AppendEntity(ent);
                    tr.AddNewlyCreatedDBObject(ent, true);
                    WriteExtensionXRecord(ent, "Pier", p.Id.ToString(), tr);
                }

                // Grade beams
                foreach (var gb in model.GradeBeams)
                {
                    Line ln = new Line(gb.Start, gb.End) { Layer = gb.Layer ?? "FOUNDATION-GRADEBEAM" };
                    ms.AppendEntity(ln);
                    tr.AddNewlyCreatedDBObject(ln, true);
                    WriteExtensionXRecord(ln, "GradeBeam", gb.Id.ToString(), tr);
                }

                // Rebars & strands
                foreach (var r in model.Rebars)
                {
                    Line ln = new Line(r.Start, r.End) { Layer = r.Layer ?? "FOUNDATION-REBAR" };
                    ms.AppendEntity(ln);
                    tr.AddNewlyCreatedDBObject(ln, true);
                    WriteExtensionXRecord(ln, "Rebar", r.Id.ToString(), tr);
                }
                foreach (var s in model.Strands)
                {
                    Line ln = new Line(s.Start, s.End) { Layer = s.Layer ?? "FOUNDATION-STRAND" };
                    ms.AppendEntity(ln);
                    tr.AddNewlyCreatedDBObject(ln, true);
                    WriteExtensionXRecord(ln, "Strand", s.Id.ToString(), tr);
                }

                tr.Commit();
            }
        }

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

        private static void WriteExtensionXRecord(Entity ent, string type, string id, Transaction tr)
        {
            if (!ent.ExtensionDictionary.IsValid) ent.CreateExtensionDictionary();
            DBDictionary ext = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, OpenMode.ForWrite);
            if (ext == null) return;

            Xrecord xr = new Xrecord();
            xr.Data = new ResultBuffer(
                new TypedValue((int)DxfCode.Text, type),
                new TypedValue((int)DxfCode.Text, id)
            );

            if (ext.Contains("FoundationObj"))
                ext.Remove("FoundationObj");

            ext.SetAt("FoundationObj", xr);
            tr.AddNewlyCreatedDBObject(xr, true);
        }
    }
}
