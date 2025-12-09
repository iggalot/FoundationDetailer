using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using FoundationDetailsLibraryAutoCAD.AutoCAD;
using System;
using System.Collections.Generic;

namespace FoundationDetailer.AutoCAD
{
    public static class GradeBeamManager
    {
        // Track grade beams per document
        private static readonly Dictionary<Document, List<ObjectId>> _gradeBeams = new Dictionary<Document, List<ObjectId>>();

        // Track which documents have already registered the RegApp
        private static readonly HashSet<Document> _regAppRegistered = new HashSet<Document>();

        /// <summary>
        /// Creates horizontal and vertical grade beams for a closed boundary polyline.
        /// </summary>
        public static void CreateBothGridlines(Polyline boundary, double maxSpacing, int vertexCount)
        {
            if (boundary == null) return;

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

                    //// Prepare storage
                    //if (!_gradeBeams.ContainsKey(doc))
                    //    _gradeBeams[doc] = new List<ObjectId>();
                    //else
                    //    ClearGradeBeams(doc, tr);

                    // Create DB polylines with XData
                    foreach (var pts in horizontalLines)
                    {
                        CreateDbLine(doc, pts, tr);
                        horiz_count++;
                    }

                    foreach (var pts in verticalLines)
                    {
                        CreateDbLine(doc, pts, tr);
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
            var db = doc.Database;

            // --- 1. Remove any grade beams listed in the QueryNOD ---
            DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (nod.Contains("FD_GRADEBEAM"))
            {
                Xrecord xr = (Xrecord)tr.GetObject(nod.GetAt("FD_GRADEBEAM"), OpenMode.ForWrite);
                if (xr.Data != null)
                {
                    foreach (var tv in xr.Data)
                    {
                        if (tv.TypeCode == (int)DxfCode.Handle && tv.Value != null)
                        {
                            try
                            {
                                Handle h = new Handle(Convert.ToInt64(tv.Value));
                                ObjectId id = db.GetObjectId(false, h, 0);
                                if (!id.IsNull)
                                {
                                    var ent = tr.GetObject(id, OpenMode.ForWrite, false) as Entity;
                                    ent?.Erase();
                                }
                            }
                            catch { }
                        }
                    }

                    // Clear the Xrecord after deletion
                    xr.Data = new ResultBuffer();
                }
            }

            // --- 2. Scan ModelSpace for polylines with FD_GRADEBEAM XData ---
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in btr)
            {
                try
                {
                    Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                    if (ent == null) continue;

                    ResultBuffer xdata = ent.XData;
                    if (xdata == null) continue;

                    foreach (TypedValue tv in xdata)
                    {
                        if (tv.TypeCode == (int)DxfCode.ExtendedDataRegAppName && tv.Value != null
                            && tv.Value.ToString() == "FD_GRADEBEAM")
                        {
                            ent.Erase();
                            break; // move to next entity
                        }
                    }
                }
                catch { }
            }

            // --- 3. Clear in-memory tracking dictionary ---
            if (_gradeBeams.ContainsKey(doc))
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
            if (!rat.Has(NODManager.KEY_GRADEBEAM))
            {
                var ratr = new RegAppTableRecord { Name = NODManager.KEY_GRADEBEAM };
                rat.Add(ratr);
                tr.AddNewlyCreatedDBObject(ratr, true);
            }

            _regAppRegistered.Add(doc);
        }

        public static void CreateDbLine(Document doc, List<Point3d> points, Transaction tr)
        {
            if (points == null || points.Count < 2) return;

            var db = doc.Database;
            var pl = new Polyline();

            for (int i = 0; i < points.Count; i++)
                pl.AddVertexAt(i, new Point2d(points[i].X, points[i].Y), 0, 0, 0);

            pl.Closed = false; // grade beams are open

            // Add to model space
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);

            // Attach XData to mark as grade beam
            SetGradeBeamXData(pl.ObjectId, tr);

            // Store reference in dictionary
            if (!_gradeBeams.ContainsKey(doc))
                _gradeBeams[doc] = new List<ObjectId>();
            _gradeBeams[doc].Add(pl.ObjectId);

            // Store the grade beams in its NOD
            NODManager.AddGradeBeamHandle(pl.ObjectId);
        }

        private static void SetGradeBeamXData(ObjectId id, Transaction tr)
        {
            if (id.IsNull) return;

            var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
            ent.XData = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, NODManager.KEY_GRADEBEAM));
        }
    }
}
