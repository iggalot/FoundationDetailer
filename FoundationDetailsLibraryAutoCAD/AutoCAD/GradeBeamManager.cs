using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FoundationDetailer.AutoCAD
{
    public static class GradeBeamManager
    {
        private const string XrecordKey = "FD_GRADEBEAM";

        // Track grade beams per document
        private static readonly Dictionary<Document, List<ObjectId>> _gradeBeams = new Dictionary<Document, List<ObjectId>>();

        // Track which documents have already registered the RegApp
        private static readonly HashSet<Document> _regAppRegistered = new HashSet<Document>();

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

            try
            {
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // Register RegApp once per document
                    RegisterGradeBeamRegApp(doc, tr);

                    // Compute gridline points
                    var (horizontalLines, verticalLines) = FoundationDetailsLibraryAutoCAD.Managers.GridlineManager
                        .ComputeBothGridlines(boundary, maxSpacing, vertexCount);

                    // Prepare storage
                    if (!_gradeBeams.ContainsKey(doc))
                        _gradeBeams[doc] = new List<ObjectId>();
                    else
                        ClearGradeBeams(doc, tr);

                    // Create DB polylines with XData
                    foreach (var pts in horizontalLines)
                    {
                        CreateDbLines(doc, pts, tr);
                        horiz_count++;
                    }

                    foreach (var pts in verticalLines)
                    {
                        CreateDbLines(doc, pts, tr);
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

        /// <summary>
        /// Registers the FD_GRADEBEAM RegApp if not already registered for this document.
        /// </summary>
        public static void RegisterGradeBeamRegApp(Document doc, Transaction tr)
        {
            if (_regAppRegistered.Contains(doc)) return;

            var db = doc.Database;
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForWrite);
            if (!rat.Has(XrecordKey))
            {
                var ratr = new RegAppTableRecord { Name = XrecordKey };
                rat.Add(ratr);
                tr.AddNewlyCreatedDBObject(ratr, true);
            }

            _regAppRegistered.Add(doc);
        }

        public static void CreateDbLines(Document doc, List<Point3d> points, Transaction tr)
        {
            if (points == null || points.Count < 2) return;

            for (int i = 0; i < points.Count - 1; i++)
            {
                AddPolylineToDb(doc, new List<Point3d> { points[i], points[i + 1] }, tr);
            }
        }

        private static void AddPolylineToDb(Document doc, List<Point3d> vertices, Transaction tr)
        {
            if (vertices == null || vertices.Count < 2) return;

            var db = doc.Database;
            var pl = new Polyline();

            for (int i = 0; i < vertices.Count; i++)
                pl.AddVertexAt(i, new Point2d(vertices[i].X, vertices[i].Y), 0, 0, 0);

            pl.Closed = false;

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);

            // Attach XData to mark as grade beam
            SetGradeBeamXData(pl.ObjectId, tr);

            // Store reference
            if (!_gradeBeams.ContainsKey(doc))
                _gradeBeams[doc] = new List<ObjectId>();
            _gradeBeams[doc].Add(pl.ObjectId);

            // Store all grade beams in NOD
            StoreGradeBeamsInNod(doc, tr);
        }

        private static void SetGradeBeamXData(ObjectId id, Transaction tr)
        {
            if (id.IsNull) return;

            var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
            ent.XData = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, XrecordKey));
        }

        private static void StoreGradeBeamsInNod(Document doc, Transaction tr)
        {
            if (!_gradeBeams.ContainsKey(doc)) return;

            var nod = (DBDictionary)tr.GetObject(doc.Database.NamedObjectsDictionaryId, OpenMode.ForWrite);

            Xrecord xr;
            if (nod.Contains(XrecordKey))
                xr = (Xrecord)tr.GetObject(nod.GetAt(XrecordKey), OpenMode.ForWrite);
            else
            {
                xr = new Xrecord();
                nod.SetAt(XrecordKey, xr);
                tr.AddNewlyCreatedDBObject(xr, true);
            }

            TypedValue[] handles = _gradeBeams[doc]
                .Where(id => !id.IsNull)
                .Select(id => new TypedValue((int)DxfCode.Handle, id.Handle.Value))
                .ToArray();

            xr.Data = new ResultBuffer(handles);
        }

    }
}
