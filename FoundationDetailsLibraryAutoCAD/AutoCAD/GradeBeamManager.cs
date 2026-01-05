using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using FoundationDetailsLibraryAutoCAD.AutoCAD;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Collections.Generic;
using System.Windows.Media.Animation;

namespace FoundationDetailer.AutoCAD
{
    public class GradeBeamManager
    {
        // Track grade beams per document
        private readonly Dictionary<Document, List<ObjectId>> _gradeBeams = new Dictionary<Document, List<ObjectId>>();

        // Track which documents have already registered the RegApp
        private readonly HashSet<Document> _regAppRegistered = new HashSet<Document>();

        public void Initialize(FoundationContext context)
        {

        }

        /// <summary>
        /// Creates horizontal and vertical grade beams for a closed boundary polyline.
        /// </summary>
        public void CreateBothGridlines(FoundationContext context, Polyline boundary, double horiz_min, double horiz_max, double vert_min, double vert_max, int vertexCount)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (boundary == null) return;

            var doc = context.Document;
            var model = context.Model;
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
                        CreateDbLine(context, pts, tr);
                        horiz_count++;
                    }

                    foreach (var pts in verticalLines)
                    {
                        CreateDbLine(context, pts, tr);
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
        public void HighlightGradeBeams(FoundationContext context)
        {
            var doc = context.Document;
            if (!_gradeBeams.ContainsKey(doc) || _gradeBeams[doc].Count == 0) return;
            doc.Editor.SetImpliedSelection(_gradeBeams[doc].ToArray());
        }

        // -------------------------
        // Internal Helpers
        // -------------------------

        /// <summary>
        /// Registers the FD_GRADEBEAM RegApp if not already registered for this document.
        /// </summary>
        public void RegisterGradeBeamRegApp(Document doc, Transaction tr)
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

        public void CreateDbLine(FoundationContext context, List<Point3d> points, Transaction tr)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (points == null || points.Count < 2) return;

            var doc = context.Document;
            var model = context.Model;
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
            AddGradeBeamHandleToNOD(context, pl.ObjectId, tr);
        }

        private void SetGradeBeamXData(ObjectId id, Transaction tr)
        {
            if (id.IsNull) return;

            var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
            ent.XData = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, NODManager.KEY_GRADEBEAM));
        }

        /// <summary>
        /// Adds a grade beam polyline handle to the EE_Foundation NOD under FD_GRADEBEAM.
        /// </summary>
        /// <param name="id">The ObjectId of the grade beam polyline.</param>
        private void AddGradeBeamHandleToNOD(FoundationContext context, ObjectId id, Transaction tr)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (id.IsNull || !id.IsValid) return;

            var doc = context.Document;
            var db = doc.Database;

            // Ensure EE_Foundation NOD and subdictionaries exist
            NODManager.InitFoundationNOD(context, tr);

            DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            DBDictionary root = (DBDictionary)tr.GetObject(nod.GetAt(NODManager.ROOT), OpenMode.ForWrite);
            DBDictionary gradebeamDict = (DBDictionary)tr.GetObject(root.GetAt(NODManager.KEY_GRADEBEAM), OpenMode.ForWrite);

            // Convert ObjectId handle to uppercase string
            string handleStr = id.Handle.ToString().ToUpperInvariant();

            // Add to NOD using existing helper
            NODManager.AddHandleToDictionary(tr, gradebeamDict, handleStr);
        }

        public void CreatePreliminary(FoundationContext context, Polyline boundary, double hMin, double hMax, double vMin, double vMax, int vertexCount = 5)
        {
            if (boundary == null)
                throw new ArgumentNullException(nameof(boundary));

            try
            {
                CreateBothGridlines(context, boundary, hMin, hMax, vMin, vMax, vertexCount);
                context.Document.Editor.WriteMessage("\nGrade beams created successfully.");
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                context.Document.Editor.WriteMessage($"\nError creating grade beams: {ex.Message}");
            }
        }

        public void ClearAll(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;

            var db = context.Document.Database;

            using (doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    NODManager.DeleteEntitiesFromFoundationSubDictionary(context, tr, db, NODManager.KEY_GRADEBEAM);
                    NODManager.ClearFoundationSubDictionary(context, db, NODManager.KEY_GRADEBEAM);
                    tr.Commit();
                }
            }
        }

        public bool HasAnyGradeBeams(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            bool exists = false;

            var doc = context.Document;
            var db = doc.Database;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                exists = NODManager.TryGetFirstEntity(
                    context,
                    tr,
                    db,
                    NODManager.KEY_GRADEBEAM,  // The sub-dictionary key for grade beams
                    out ObjectId oid
                );

                // No need to commit; we're just reading
            }

            return exists;
        }

    }
}
