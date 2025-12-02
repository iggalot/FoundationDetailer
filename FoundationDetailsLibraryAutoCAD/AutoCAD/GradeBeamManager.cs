using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using FoundationDetailsLibraryAutoCAD.Managers;
using System;
using System.Collections.Generic;

namespace FoundationDetailer.AutoCAD
{
    public static class GradeBeamManager
    {
        private const string XrecordKey = "FD_GRADEBEAM";
        private static readonly Dictionary<Document, List<ObjectId>> _gradeBeams = new Dictionary<Document, List<ObjectId>>();

        /// <summary>
        /// Creates horizontal and vertical grade beams for a closed boundary polyline.
        /// </summary>
        public static void CreateBothGridlines(Polyline boundary, double maxSpacing, int vertexCount)
        {
            if (boundary == null || !boundary.Closed) return;

            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            int horiz_count = 0;
            int vert_count = 0;

            RegisterGradeBeamRegApp(doc);

            try
            {
                using (doc.LockDocument())


                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // Compute gridlines
                    var (horizontalLines, verticalLines) = GridlineManager.ComputeBothGridlines(boundary, maxSpacing, vertexCount);

                    // Flatten both sets for DB creation
                    var allLines = new List<List<Point3d>>();
                    allLines.AddRange(horizontalLines);
                    allLines.AddRange(verticalLines);

                    // Ensure storage
                    if (!_gradeBeams.ContainsKey(doc))
                        _gradeBeams[doc] = new List<ObjectId>();
                    else
                        ClearGradeBeams(doc, tr);

                    // Create DB polylines with XData
                    foreach (var pts in horizontalLines)
                    {
                        CreateDbLines(doc, pts, XrecordKey, tr);
                        horiz_count++;
                    }

                    foreach (var pts in verticalLines)
                    {
                        CreateDbLines(doc, pts, XrecordKey, tr);
                        vert_count++;
                    }

                    tr.Commit();
                }

                doc.Editor.WriteMessage($"\nGrade beams created: horizontal={horiz_count}, vertical={vert_count}");
            }
            catch (Exception ex)
            {
                doc.Editor.WriteMessage($"\nError creating grade beams: {ex.Message}");
            }
        }

        /// <summary>
        /// Remove previously created grade beams.
        /// </summary>
        public static void ClearGradeBeams(Document doc, Transaction tr)
        {
            if (!_gradeBeams.ContainsKey(doc)) return;

            foreach (var id in _gradeBeams[doc])
            {
                if (!id.IsNull)
                {
                    try
                    {
                        var ent = tr.GetObject(id, OpenMode.ForWrite, false) as Entity;
                        ent?.Erase();
                    }
                    catch { }
                }
            }

            _gradeBeams[doc].Clear();
        }

        /// <summary>
        /// Highlight all grade beams in the current document.
        /// </summary>
        public static void HighlightGradeBeams(Document doc)
        {
            if (!_gradeBeams.ContainsKey(doc) || _gradeBeams[doc].Count == 0) return;
            doc.Editor.SetImpliedSelection(_gradeBeams[doc].ToArray());
        }

        // -------------------------
        // Internal Helpers
        // -------------------------

        private static void RegisterGradeBeamRegApp(Document doc)
        {
            Database db = doc.Database;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForWrite);
                if (!rat.Has(XrecordKey))
                {
                    RegAppTableRecord ratr = new RegAppTableRecord { Name = XrecordKey };
                    rat.Add(ratr);
                    tr.AddNewlyCreatedDBObject(ratr, true);
                }
                tr.Commit();
            }
        }


        /// <summary>
        /// Creates AutoCAD DB lines from a list of Point3d vertices and stores them in the drawing with Xrecord.
        /// </summary>
        public static void CreateDbLines(Document doc, List<Point3d> points, string xrecordKey, Transaction tr)
        {
            if (points == null || points.Count < 2) return;

            for (int i = 0; i < points.Count - 1; i++)
            {
                AddPolylineToDb(doc, new List<Point3d> { points[i], points[i + 1] }, xrecordKey, tr);
            }
        }

        /// <summary>
        /// Adds a polyline to the drawing and attaches Xrecord for persistence.
        /// </summary>
        private static void AddPolylineToDb(Document doc, List<Point3d> vertices, string xrecordKey, Transaction tr)
        {
            if (vertices == null || vertices.Count < 2) return;

            var db = doc.Database;
            var pl = new Polyline();

            for (int i = 0; i < vertices.Count; i++)
            {
                pl.AddVertexAt(i, new Point2d(vertices[i].X, vertices[i].Y), 0, 0, 0);
            }

            pl.Closed = false;

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);

            // Attach Xrecord for identification
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
            Xrecord xr;
            if (nod.Contains(xrecordKey))
            {
                xr = (Xrecord)tr.GetObject(nod.GetAt(xrecordKey), OpenMode.ForWrite);
            }
            else
            {
                xr = new Xrecord();
                nod.SetAt(xrecordKey, xr);
                tr.AddNewlyCreatedDBObject(xr, true);
            }

            xr.Data = new ResultBuffer(new TypedValue((int)DxfCode.Handle, pl.Handle.Value));
        }
        /// <summary>
        /// Add XData to a polyline to mark it as a grade beam.
        /// </summary>
        private static void SetGradeBeamXData(ObjectId id, Transaction tr)
        {
            if (id.IsNull) return;

            var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
            ent.XData = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, XrecordKey));
        }
    }
}
