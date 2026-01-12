using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using FoundationDetailsLibraryAutoCAD.AutoCAD;
using FoundationDetailsLibraryAutoCAD.Data;
using FoundationDetailsLibraryAutoCAD.Managers;
using FoundationDetailsLibraryAutoCAD.Services;
using FoundationDetailsLibraryAutoCAD.UI.Controls.EqualSpacingGBControl;
using System;
using System.Collections.Generic;
using System.Linq;

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

        /// <summary>
        /// Need for inheritance from EntityJig
        /// </summary>
        /// <param name="prompts"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Need for inheritance from EntityJig
        /// </summary>
        /// <returns></returns>
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

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Placeholder for future use")]
        public void Initialize(FoundationContext context)
        {

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
            if (!rat.Has(NODManager.KEY_GRADEBEAM_SUBDICT))
            {
                var ratr = new RegAppTableRecord { Name = NODManager.KEY_GRADEBEAM_SUBDICT };
                rat.Add(ratr);
                tr.AddNewlyCreatedDBObject(ratr, true);
            }

            _regAppRegistered.Add(doc);
        }

        /// <summary>
        /// Adds a grade beam polyline handle to the EE_Foundation NOD under FD_GRADEBEAM.
        /// </summary>
        /// <param name="id">The ObjectId of the grade beam polyline.</param>
        private void AddGradeBeamCenterlineHandleToNOD(
            FoundationContext context,
            ObjectId id,
            Transaction tr)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (id.IsNull || !id.IsValid) return;

            var doc = context.Document;
            var db = doc.Database;

            // Ensure EE_Foundation NOD exists
            NODManager.InitFoundationNOD(context, tr);

            DBDictionary nod =
                (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

            DBDictionary root =
                (DBDictionary)tr.GetObject(nod.GetAt(NODManager.ROOT), OpenMode.ForWrite);

            DBDictionary gradebeamDict =
                (DBDictionary)tr.GetObject(root.GetAt(NODManager.KEY_GRADEBEAM_SUBDICT), OpenMode.ForWrite);

            // Handle string
            string handleStr = id.Handle.ToString().ToUpperInvariant();

            // Create full grade beam structure (safe if already exists)
            CreateGradeBeamNODStructure(context, tr, db, handleStr, id);
        }

        public static void CreateGradeBeamNODStructure(
            FoundationContext context,
            Transaction tr,
            Database db,
            string gradeBeamHandle,
            ObjectId centerlineId)
        {
            if (tr == null || db == null || string.IsNullOrEmpty(gradeBeamHandle))
                throw new ArgumentNullException();

            string centerlineHandle = centerlineId.Handle.ToString();

            DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
            DBDictionary root = NODManager.GetOrCreateSubDictionary(tr, nod, NODManager.ROOT);
            DBDictionary gradebeamDict = NODManager.GetOrCreateSubDictionary(tr, root, NODManager.KEY_GRADEBEAM_SUBDICT);
            DBDictionary handleDict = NODManager.GetOrCreateSubDictionary(tr, gradebeamDict, gradeBeamHandle);

            // Attach the existing centerline entity to the handle directory
            // ------------------------------------------------
            // CENTERLINE HANDLE (XRecord, persistent)
            // ------------------------------------------------
            if (!handleDict.Contains(NODManager.KEY_CENTERLINE))
            {
                Xrecord xrec = new Xrecord();
                xrec.Data = new ResultBuffer(
                    new TypedValue((int)DxfCode.Text, centerlineHandle));

                handleDict.SetAt(NODManager.KEY_CENTERLINE, xrec);
                tr.AddNewlyCreatedDBObject(xrec, true);
            }

            // Ensure FD_EDGES sub dictionar exists
            NODManager.GetOrCreateSubDictionary(tr, handleDict, NODManager.KEY_EDGES_SUBDICT);

            // Add metadata Xrecord for future use -- this is a single xrecord and not a subdictionary at this time
            NODManager.GetOrCreateMetadataXrecord(tr, handleDict, NODManager.KEY_METADATA_SUBDICT);

        }

        public List<Polyline> CreatePreliminaryGradeBeamLayout(
            FoundationContext context,
            Polyline boundary,
            double horizMin,
            double horizMax,
            double vertMin,
            double vertMax,
            int vertexCount = 5)
        {
            if (boundary == null) throw new ArgumentNullException(nameof(boundary));
            if (context?.Document == null) throw new ArgumentNullException(nameof(context));

            var db = context.Document.Database;
            List<Polyline> createdBeams = new List<Polyline>();

            // --- Compute horizontal and vertical gridlines using GridlineManager ---
            var gridlines = GridlineManager.ComputeBothGridlines(
                boundary,
                horizMin,
                horizMax,
                vertMin,
                vertMax,
                vertexCount
            );

            using (context.Document.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // --- Horizontal grade beams ---
                foreach (var linePts in gridlines.Horizontal)
                {
                    List<Point2d> verts = linePts.Select(p => new Point2d(p.X, p.Y)).ToList();
                    Polyline pl = CreatePolylineFromVertices(verts);
                    RegisterGradeBeam(context, pl, tr, appendToModelSpace: true);
                    createdBeams.Add(pl);
                }

                // --- Vertical grade beams ---
                foreach (var linePts in gridlines.Vertical)
                {
                    List<Point2d> verts = linePts.Select(p => new Point2d(p.X, p.Y)).ToList();
                    Polyline pl = CreatePolylineFromVertices(verts);
                    RegisterGradeBeam(context, pl, tr, appendToModelSpace: true);
                    createdBeams.Add(pl);
                }

                tr.Commit();
            }

            return createdBeams;
        }


        /// <summary>
        /// Creates a new Polyline from a list of 2D points and copies basic properties from an optional source entity.
        /// </summary>
        internal Polyline CreatePolylineFromVertices(List<Point2d> verts, Entity source = null)
        {
            if (verts == null || verts.Count < 2)
                throw new ArgumentException("Vertex list must have at least 2 points");

            Polyline pl = new Polyline();

            for (int i = 0; i < verts.Count; i++)
                pl.AddVertexAt(i, verts[i], 0, 0, 0);

            if (source != null)
            {
                pl.LayerId = source.LayerId;
                pl.Color = source.Color;
                pl.LinetypeId = source.LinetypeId;
                pl.LineWeight = source.LineWeight;
            }

            return pl;
        }


        public void ClearAllGradeBeams(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            if (doc == null) return;

            var db = context.Document.Database;
            if(db == null) return;

            using (doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    NODManager.DeleteEntitiesFromFoundationSubDictionary(context, tr, db, NODManager.KEY_GRADEBEAM_SUBDICT);
                    NODManager.ClearFoundationSubDictionary(context, db, NODManager.KEY_GRADEBEAM_SUBDICT);
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
                    NODManager.KEY_GRADEBEAM_SUBDICT,  // The sub-dictionary key for grade beams
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
                // Get the KEY_GRADEBEAM_SUBDICT sub-dictionary
                var subDict = NODManager.GetSubDictionary(tr, db, NODManager.KEY_GRADEBEAM_SUBDICT);
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

        internal Polyline RegisterGradeBeam(
            FoundationContext context,
            Polyline pl,
            Transaction tr,
            bool appendToModelSpace = false)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (pl == null) throw new ArgumentNullException(nameof(pl));

            Database db = context.Document.Database;

            // --- Append to ModelSpace if requested ---
            if (appendToModelSpace)
            {
                ModelSpaceWriterService.AppendToModelSpace(tr, db, pl);
            }

            // --- Write grade beam metadata (domain-specific) ---
            FoundationEntityData.Write(tr, pl, NODManager.KEY_GRADEBEAM_SUBDICT);

            // --- Add centerline handle (domain-specific) ---
            AddGradeBeamCenterlineHandleToNOD(context, pl.ObjectId, tr);

            return pl;
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
                // Create the interpolated Polyline
                Polyline pl = new Polyline();
                for (int i = 0; i < vertexCount; i++)
                {
                    double t = (double)i / (vertexCount - 1);
                    double x = start.X + (end.X - start.X) * t;
                    double y = start.Y + (end.Y - start.Y) * t;
                    pl.AddVertexAt(i, new Point2d(x, y), 0, 0, 0);
                }

                // Register in NOD and append to ModelSpace
                RegisterGradeBeam(context, pl, tr, appendToModelSpace: true);

                tr.Commit();
                return pl;
            }
        }

        // ---------------------------
        // GradeBeam service function
        // ---------------------------
        internal void AddExistingAsGradeBeam(
            FoundationContext context,
            ObjectId polylineId,
            Transaction tr)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (polylineId.IsNull) throw new ArgumentException("Invalid Polyline ObjectId.", nameof(polylineId));

            Polyline pl = tr.GetObject(polylineId, OpenMode.ForRead) as Polyline;
            if (pl == null)
                throw new ArgumentException("Object is not a Polyline.", nameof(polylineId));

            // Just register in NOD, no append needed
            RegisterGradeBeam(context, pl, tr, appendToModelSpace: false);
        }

        internal void ConvertToGradeBeam(
            FoundationContext context,
            ObjectId oldEntityId,
            int vertexCount,
            Transaction tr)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (oldEntityId.IsNull) throw new ArgumentException("Invalid ObjectId.", nameof(oldEntityId));
            if (tr == null) throw new ArgumentNullException(nameof(tr));

            var db = context.Document.Database;
            var oldEnt = tr.GetObject(oldEntityId, OpenMode.ForRead) as Entity;
            if (oldEnt == null)
                throw new ArgumentException("Object is not a valid entity.", nameof(oldEntityId));

            // --- Convert old entity to new Polyline ---
            var verts = PolylineConversionService.GetVertices(oldEnt);

            // Ensure minimum vertex count
            verts = PolylineConversionService.EnsureMinimumVertices(verts, vertexCount);

            Polyline newPl = PolylineConversionService.CreatePolylineFromVertices(verts, oldEnt);

            // --- Append to ModelSpace (infrastructure) if needed ---
            ModelSpaceWriterService.AppendToModelSpace(tr, db, newPl);

            // --- Write GradeBeam metadata and register in NOD ---
            RegisterGradeBeam(context, newPl, tr, appendToModelSpace: false);

            // --- Remove old GradeBeam NOD entry if it exists ---
            NODManager.EraseGradeBeamEntry(tr, db, oldEnt.Handle.ToString());

            // --- Delete old entity from ModelSpace ---
            oldEnt.UpgradeOpen();
            oldEnt.Erase();
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

            if (!root.Contains(NODManager.KEY_GRADEBEAM_SUBDICT))
                return false;

            var gradeBeamDict = (DBDictionary)
                tr.GetObject(root.GetAt(NODManager.KEY_GRADEBEAM_SUBDICT), OpenMode.ForRead);

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

        public (Point3d? start, Point3d? end) PromptForSpacingPoints(FoundationContext context)
        {
            if (context == null)
                return (null, null);

            Document doc = context.Document;

            if (doc == null)
                return (null, null);

            Editor ed = doc.Editor;

            PromptPointResult p1 = ed.GetPoint("\nPick first reference point:");
            if (p1.Status != PromptStatus.OK)
                return (null, null);

            PromptPointOptions ppo = new PromptPointOptions("\nPick second reference point:")
            {
                BasePoint = p1.Value,
                UseBasePoint = true
            };

            PromptPointResult p2 = ed.GetPoint(ppo);
            if (p2.Status != PromptStatus.OK)
                return (null, null);

            return (p1.Value, p2.Value);
        }

        public int? PromptForEqualSpacingCount(FoundationContext context, int min = 1, int max = 1000)
        {
            if (context == null)
                return 1;

            Document doc = context.Document;

            if (doc == null)
                return 1;

            Editor ed = doc.Editor;

            var opts = new PromptIntegerOptions(
                $"\nEnter number of equal spaces [{min}–{max}]:")
            {
                AllowNegative = false,
                AllowZero = false,
                LowerLimit = min,
                UpperLimit = max
            };

            PromptIntegerResult res = ed.GetInteger(opts);

            if (res.Status != PromptStatus.OK)
                return null;

            return res.Value;
        }
        public SpacingDirections? PromptForSpacingDirection(FoundationContext context)
        {
            if (context == null)
                return null;

            Document doc = context.Document;

            if (doc == null)
                return null;

            Editor ed = doc.Editor;

            var opts = new PromptKeywordOptions(
                "\nSpacing direction [Horizontal/Vertical/Perpendicular] <Perpendicular>:")
            {
                AllowNone = true
            };

            // Add keywords
            opts.Keywords.Add("Horizontal", "H", "Horizontal");
            opts.Keywords.Add("Vertical", "V", "Vertical");
            opts.Keywords.Add("Perpendicular", "P", "Perpendicular");

            PromptResult res = ed.GetKeywords(opts);

            // ESC / Cancel
            if (res.Status == PromptStatus.Cancel)
                return null;

            // ENTER pressed → default
            if (res.Status == PromptStatus.None || string.IsNullOrEmpty(res.StringResult))
                return SpacingDirections.Perpendicular;

            // Map keyword to enum
            switch (res.StringResult)
            {
                case "Horizontal":
                    return SpacingDirections.Horizontal;

                case "Vertical":
                    return SpacingDirections.Vertical;

                case "Perpendicular":
                    return SpacingDirections.Perpendicular;

                default:
                    // Should never happen, but safe guard
                    ed.WriteMessage("\nInvalid direction selected.");
                    return null;
            }
        }
    }

}


