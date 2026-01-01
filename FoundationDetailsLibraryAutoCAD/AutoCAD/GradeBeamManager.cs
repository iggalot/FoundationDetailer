using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using FoundationDetailer.Managers;
using FoundationDetailsLibraryAutoCAD.AutoCAD;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace FoundationDetailer.AutoCAD
{
    public class GradeBeamManager
    {
        // Track grade beams per document
        private static readonly Dictionary<Document, List<ObjectId>> _gradeBeams = new Dictionary<Document, List<ObjectId>>();

        // Track which documents have already registered the RegApp
        private static readonly HashSet<Document> _regAppRegistered = new HashSet<Document>();

        /// <summary>
        /// Creates horizontal and vertical grade beams for a closed boundary polyline.
        /// </summary>
        public static void CreateBothGridlines(Polyline boundary, double horiz_min, double horiz_max, double vert_min, double vert_max, int vertexCount)
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
                    var (horizontalLines, verticalLines) = FoundationDetailsLibraryAutoCAD.Managers.GridlineManager.ComputeBothGridlines(boundary, horiz_min, horiz_max, vert_min, vert_max, vertexCount);

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
            FoundationEntityData.Write(tr, pl, NODManager.KEY_GRADEBEAM);
            AddGradeBeamHandleToNOD(pl.ObjectId);
        }

        private static void SetGradeBeamXData(ObjectId id, Transaction tr)
        {
            if (id.IsNull) return;

            var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
            ent.XData = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, NODManager.KEY_GRADEBEAM));
        }

        /// <summary>
        /// Adds a grade beam polyline handle to the EE_Foundation NOD under FD_GRADEBEAM.
        /// </summary>
        /// <param name="id">The ObjectId of the grade beam polyline.</param>
        private static void AddGradeBeamHandleToNOD(ObjectId id)
        {
            if (id.IsNull || !id.IsValid) return;

            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;

            using (doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // Ensure EE_Foundation NOD and subdictionaries exist
                    NODManager.InitFoundationNOD(tr);

                    DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                    DBDictionary root = (DBDictionary)tr.GetObject(nod.GetAt(NODManager.ROOT), OpenMode.ForWrite);
                    DBDictionary gradebeamDict = (DBDictionary)tr.GetObject(root.GetAt(NODManager.KEY_GRADEBEAM), OpenMode.ForWrite);

                    // Convert ObjectId handle to uppercase string
                    string handleStr = id.Handle.ToString().ToUpperInvariant();

                    // Add to NOD using existing helper
                    NODManager.AddHandleToDictionary(tr, gradebeamDict, handleStr);

                    tr.Commit();
                }
            }
        }

        public void CreatePreliminary(
        Polyline boundary,
        double hMin, double hMax,
        double vMin, double vMax,
        int vertexCount = 5)
        {
            if (boundary == null)
                throw new ArgumentNullException(nameof(boundary));

            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            try
            {
                using (doc.LockDocument())
                {
                    GradeBeamManager.CreateBothGridlines(
                        boundary,
                        hMin, hMax,
                        vMin, vMax,
                        vertexCount);

                    doc.Editor.WriteMessage("\nGrade beams created successfully.");
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                doc.Editor.WriteMessage($"\nError creating grade beams: {ex.Message}");
            }
        }

        public void ClearAll()
        {
            var db = Application.DocumentManager.MdiActiveDocument.Database;

            NODManager.DeleteEntitiesFromFoundationSubDictionary(db, NODManager.KEY_GRADEBEAM);
            NODManager.ClearFoundationSubDictionary(db, NODManager.KEY_GRADEBEAM);
        }

    }
}
