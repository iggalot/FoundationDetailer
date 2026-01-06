using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using FoundationDetailsLibraryAutoCAD.AutoCAD;
using FoundationDetailsLibraryAutoCAD.Data;
using FoundationDetailsLibraryAutoCAD.Managers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;

namespace FoundationDetailer.AutoCAD
{
    public class GradeBeamPolylineJig : EntityJig
    {
        private Point3d _start;
        private Point3d _end;

        public Polyline Polyline => (Polyline)Entity;

        public GradeBeamPolylineJig(Point3d startPoint)
            : base(CreateInitialPolyline(startPoint))
        {
            _start = startPoint;
            _end = startPoint;
        }

        private static Polyline CreateInitialPolyline(Point3d start)
        {
            var pl = new Polyline();
            pl.AddVertexAt(0, new Point2d(start.X, start.Y), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(start.X, start.Y), 0, 0, 0);
            return pl;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var opts = new JigPromptPointOptions("\nSelect second point:")
            {
                BasePoint = _start,
                UseBasePoint = true
            };

            var res = prompts.AcquirePoint(opts);
            if (res.Status != PromptStatus.OK)
                return SamplerStatus.Cancel;

            if (res.Value.IsEqualTo(_end))
                return SamplerStatus.NoChange;

            _end = res.Value;
            return SamplerStatus.OK;
        }

        protected override bool Update()
        {
            // Update preview polyline geometry
            Polyline.SetPointAt(1, new Point2d(_end.X, _end.Y));
            return true;
        }
    }

    public class GradeBeamManager
    {
        // Track grade beams per document
        private readonly Dictionary<Document, List<ObjectId>> _gradeBeams = new Dictionary<Document, List<ObjectId>>();

        // Track which documents have already registered the RegApp
        private readonly HashSet<Document> _regAppRegistered = new HashSet<Document>();

        public void Initialize(FoundationContext _context)
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

            using (doc.LockDocument())
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

        public (int Quantity, double TotalLength) GetGradeBeamSummary(FoundationContext context)
        {
            int quantity = 0;
            double totalLength = 0;

            var db = context.Document.Database;

            using (context.Document.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // Get the KEY_GRADEBEAM sub-dictionary
                var subDict = NODManager.GetSubDictionary(tr, db, NODManager.KEY_GRADEBEAM);
                if (subDict == null)
                    return (0, 0);

                // Get all valid ObjectIds using your helper
                var validIds = NODManager.GetAllValidObjectIdsFromSubDictionary(context, tr, db, subDict);

                quantity = validIds.Count;

                foreach (var oid in validIds)
                {
                    var ent = tr.GetObject(oid, OpenMode.ForRead) as Autodesk.AutoCAD.DatabaseServices.Entity;

                    if (ent is Autodesk.AutoCAD.DatabaseServices.Line line)
                    {
                        totalLength += line.Length;
                    }
                    else if (ent is Autodesk.AutoCAD.DatabaseServices.Polyline pl)
                    {
                        totalLength += MathHelperManager.ComputePolylineLength(pl);
                    }
                    // Extend for other beam types if needed
                }

                tr.Commit();
            }

            return (quantity, totalLength);
        }

        ///Adds a new gradebeam object between the two selected user points <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <param name="vertexCount"></param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        internal Polyline AddInterpolatedGradeBeam(FoundationContext context, Point3d start, Point3d end, int vertexCount)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (vertexCount < 2) throw new ArgumentException("Vertex count must be >= 2", nameof(vertexCount));

            var db = context.Document.Database;

            using (context.Document.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // Create the polyline
                Polyline pl = new Polyline();
                for (int i = 0; i < vertexCount; i++)
                {
                    double t = (double)i / (vertexCount - 1); // linear interpolation
                    double x = start.X + (end.X - start.X) * t;
                    double y = start.Y + (end.Y - start.Y) * t;
                    double z = start.Z + (end.Z - start.Z) * t;

                    pl.AddVertexAt(i, new Point2d(x, y), 0, 0, 0);
                }

                btr.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);

                FoundationEntityData.Write(tr, pl, NODManager.KEY_GRADEBEAM);
                AddGradeBeamHandleToNOD(context, pl.Id, tr);  // add the grade beam to the NOD.

                tr.Commit();

                return pl;
            }

            return null;
        }

        public static bool TryGetGradeBeamHandles(
            FoundationContext context,
            Transaction tr,
            out List<string> handleStrings)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));

            handleStrings = new List<string>();

            var doc = context.Document;
            var db = doc.Database;

            var nod = (DBDictionary)
                tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

            if (!nod.Contains(NODManager.ROOT))
                return false;

            var root = (DBDictionary)
                tr.GetObject(nod.GetAt(NODManager.ROOT), OpenMode.ForRead);

            if (!root.Contains(NODManager.KEY_GRADEBEAM))
                return false;

            var gradeBeamDict = (DBDictionary)
                tr.GetObject(root.GetAt(NODManager.KEY_GRADEBEAM), OpenMode.ForRead);

            foreach (DBDictionaryEntry entry in gradeBeamDict)
                handleStrings.Add(entry.Key);

            return handleStrings.Count > 0;
        }

        public static bool TryGetGradeBeams(
            FoundationContext context,
            out List<Polyline> gradeBeams)
        {
            gradeBeams = new List<Polyline>();

            if (context == null)
                return false;

            var doc = context.Document;
            var db = doc.Database;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (!TryGetGradeBeamHandles(context, tr, out var handles))
                    return false;

                foreach (string handleStr in handles)
                {
                    if (!NODManager.TryGetObjectIdFromHandleString(
                            context, db, handleStr, out ObjectId oid))
                        continue;

                    if (oid.IsNull || oid.IsErased || !oid.IsValid)
                        continue;

                    var pl = tr.GetObject(oid, OpenMode.ForRead, false) as Polyline;
                    if (pl != null)
                        gradeBeams.Add(pl);
                }

                return gradeBeams.Count > 0;
            }
        }

        public void HighlightGradeBeams(FoundationContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var ed = doc.Editor;

            // STEP 1 — Collect grade beams
            if (!GradeBeamManager.TryGetGradeBeams(context, out List<Polyline> beams) ||
                beams == null || beams.Count == 0)
            {
                ed.WriteMessage("\n[GradeBeam] No grade beams found.");
                return;
            }

            // STEP 2 — Extract ObjectIds
            var ids = beams
                .Where(b => b != null)
                .Select(b => b.ObjectId);

            // STEP 3 — Use SelectionService to filter valid IDs and get invalid ones for logging
            var validIds = SelectionService.FilterValidIds(context, ids, out List<ObjectId> invalidIds);

            // STEP 4 — Log diagnostics
            ed.WriteMessage($"\n[GradeBeam] Found={beams.Count}, Valid={validIds.Count}, Invalid={invalidIds.Count}");
            foreach (var id in invalidIds)
            {
                string handle = id.IsNull ? "<null>" : id.Handle.ToString();
                ed.WriteMessage($"\n  {handle} (invalid/erased)");
            }

            // Bring AutoCAD to front and highlight selected objects
            SelectionService.FocusAndHighlight(context, ids, "HighlightGradeBeam");

        }




    }

}


