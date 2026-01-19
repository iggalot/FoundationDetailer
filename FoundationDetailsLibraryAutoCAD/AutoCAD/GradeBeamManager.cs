using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using FoundationDetailsLibraryAutoCAD.AutoCAD;
using FoundationDetailsLibraryAutoCAD.AutoCAD.NOD;
using FoundationDetailsLibraryAutoCAD.Data;
using FoundationDetailsLibraryAutoCAD.Managers;
using FoundationDetailsLibraryAutoCAD.Services;
using FoundationDetailsLibraryAutoCAD.UI.Controls.EqualSpacingGBControl;
using System;
using System.Collections.Generic;
using System.Linq;
using static FoundationDetailsLibraryAutoCAD.AutoCAD.NOD.HandleHandler;

namespace FoundationDetailer.AutoCAD
{
    public class GradeBeamManager
    {
        private const double DEFAULT_BEAM_WIDTH_IN = 10.0;
        private const double INCHES_TO_DRAWING_UNITS = 1.0 / 12.0;


        // Track grade beams per document
        private readonly Dictionary<Document, List<ObjectId>> _gradeBeams = new Dictionary<Document, List<ObjectId>>();



        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Placeholder for future use")]
        public void Initialize(FoundationContext context)
        {

        }

        // -------------------------
        // Internal Helpers
        // -------------------------
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
                    Polyline pl = PolylineConversionService.CreatePolylineFromVertices(verts);
                    RegisterGradeBeam(context, pl, tr, appendToModelSpace: true);
                    createdBeams.Add(pl);
                }

                // --- Vertical grade beams ---
                foreach (var linePts in gridlines.Vertical)
                {
                    List<Point2d> verts = linePts.Select(p => new Point2d(p.X, p.Y)).ToList();
                    Polyline pl = PolylineConversionService.CreatePolylineFromVertices(verts);
                    RegisterGradeBeam(context, pl, tr, appendToModelSpace: true);
                    createdBeams.Add(pl);
                }

                tr.Commit();
            }

            return createdBeams;
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
            FoundationEntityData.Write(tr, pl, NODCore.KEY_GRADEBEAM_SUBDICT);

            // --- Add centerline handle (domain-specific) ---
            GradeBeamNOD.AddGradeBeamCenterlineHandleToNOD(context, pl.ObjectId, tr);

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
            GradeBeamNOD.EraseGradeBeamEntry(tr, db, oldEnt.Handle.ToString());

            // --- Delete old entity from ModelSpace ---
            oldEnt.UpgradeOpen();
            oldEnt.Erase();
        }

        /// <summary>
        /// Deletes a single grade beam dictionary and everything under it (subdictionaries, XRecords, entities)
        /// </summary>
        /// <param name="context">Current foundation context</param>
        /// <param name="tr">Active transaction</param>
        /// <param name="parentDict">Parent dictionary that contains the grade beam entry</param>
        /// <param name="gradeBeamKey">Key of the grade beam (centerline handle or dictionary key)</param>
        /// <returns>Total number of erased AutoCAD entities</returns>
        private static int DeleteGradeBeamRecursive(
            FoundationContext context,
            Transaction tr,
            DBDictionary parentDict,
            string gradeBeamKey)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (parentDict == null) throw new ArgumentNullException(nameof(parentDict));
            if (string.IsNullOrWhiteSpace(gradeBeamKey)) throw new ArgumentNullException(nameof(gradeBeamKey));
            if (!parentDict.Contains(gradeBeamKey)) return 0;

            int deletedCount = 0;

            // Open the grade beam dictionary or object
            var obj = tr.GetObject(parentDict.GetAt(gradeBeamKey), OpenMode.ForWrite);

            void DeleteDBObjectRecursive(DBObject dbObj)
            {
                if (dbObj == null) return;

                switch (dbObj)
                {
                    case DBDictionary dict:
                        // Recursively delete all children
                        foreach (var (childKey, childId) in NODCore.EnumerateDictionary(dict).ToList())
                        {
                            try
                            {
                                var childObj = tr.GetObject(childId, OpenMode.ForWrite);
                                DeleteDBObjectRecursive(childObj);
                            }
                            catch { /* ignore individual errors */ }
                        }
                        dict.Erase();
                        break;

                    case Xrecord xrec:
                        // Delete entities referenced by handles in XRecord
                        if (xrec.Data != null)
                        {
                            foreach (TypedValue tv in xrec.Data)
                            {
                                if (tv.TypeCode == (int)DxfCode.Text)
                                {
                                    string handleStr = tv.Value as string;
                                    if (!string.IsNullOrWhiteSpace(handleStr))
                                    {
                                        try
                                        {
                                            Handle h = new Handle(Convert.ToInt64(handleStr, 16));
                                            ObjectId oid = context.Document.Database.GetObjectId(false, h, 0);
                                            if (oid.IsValid && !oid.IsErased)
                                            {
                                                var ent = tr.GetObject(oid, OpenMode.ForWrite) as Entity;
                                                ent?.Erase();
                                                deletedCount++;
                                            }
                                        }
                                        catch { }
                                    }
                                }
                            }
                        }
                        xrec.Erase();
                        break;

                    default:
                        // Any other object
                        dbObj?.Erase();
                        deletedCount++;
                        break;
                }
            }

            DeleteDBObjectRecursive(obj);

            // Remove entry from parent dictionary
            parentDict.Remove(gradeBeamKey);

            return deletedCount;
        }

        /// <summary>
        /// Deletes a grade beam from the NOD and AutoCAD by centerline handle or dictionary key.
        /// </summary>
        public int DeleteGradeBeam(FoundationContext context, string gradeBeamKey)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(gradeBeamKey)) throw new ArgumentNullException(nameof(gradeBeamKey));

            int deletedCount = 0;
            var doc = context.Document;
            var db = doc.Database;
            var ed = doc.Editor;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                    if (!nod.Contains(NODCore.ROOT)) return 0;

                    var root = (DBDictionary)tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForWrite);
                    if (!root.Contains(NODCore.KEY_GRADEBEAM_SUBDICT)) return 0;

                    var gradeBeamContainer = (DBDictionary)tr.GetObject(root.GetAt(NODCore.KEY_GRADEBEAM_SUBDICT), OpenMode.ForWrite);

                    deletedCount = DeleteGradeBeamRecursive(context, tr, gradeBeamContainer, gradeBeamKey);

                    tr.Commit();
                }
                catch (Exception ex)
                {
                    ed.WriteMessage($"\n[GradeBeamManager] Failed to delete grade beam '{gradeBeamKey}': {ex.Message}");
                }
            }

            ed.WriteMessage($"\n[GradeBeamManager] Deleted {deletedCount} AutoCAD entities from grade beam '{gradeBeamKey}'.");
            return deletedCount;
        }

        public int DeleteAllGradeBeams(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            int totalDeleted = 0;

            var doc = context.Document;
            var db = doc.Database;
            var ed = doc.Editor;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                if (!nod.Contains(NODCore.ROOT)) return 0;

                var root = (DBDictionary)tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForWrite);
                if (!root.Contains(NODCore.KEY_GRADEBEAM_SUBDICT)) return 0;

                var gradeBeamContainer = (DBDictionary)tr.GetObject(root.GetAt(NODCore.KEY_GRADEBEAM_SUBDICT), OpenMode.ForWrite);

                foreach (var (key, _) in NODCore.EnumerateDictionary(gradeBeamContainer).ToList())
                {
                    totalDeleted += DeleteGradeBeamRecursive(context, tr, gradeBeamContainer, key);
                }

                tr.Commit();
            }

            ed.WriteMessage($"\n[GradeBeamManager] Deleted {totalDeleted} grade beam entities.");
            return totalDeleted;
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
                exists = NODScanner.TryGetFirstEntity(
                    context,
                    tr,
                    db,
                    NODCore.KEY_GRADEBEAM_SUBDICT,  // The sub-dictionary key for grade beams
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
                // Get the ROOT dictionary
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                var root = NODCore.GetOrCreateNestedSubDictionary(tr, nod, NODCore.ROOT);

                // Get the top-level GradeBeam dictionary (contains all grade beam handles)
                var gradebeamDict = NODCore.GetOrCreateNestedSubDictionary(tr, root, NODCore.KEY_GRADEBEAM_SUBDICT);

                if (gradebeamDict == null)
                    return (0, 0);

                // Loop over all individual grade beams (sub-dictionaries keyed by handle)
                foreach (DBDictionaryEntry entry in gradebeamDict)
                {
                    if (!(tr.GetObject(entry.Value, OpenMode.ForRead) is DBDictionary handleDict))
                        continue;

                    // Collect all ObjectIds in this handleDict
                    foreach (DBDictionaryEntry subEntry in handleDict)
                    {
                        if (tr.GetObject(subEntry.Value, OpenMode.ForRead) is Entity ent)
                        {
                            quantity++;

                            if (ent is Line line)
                            {
                                totalLength += line.Length;
                            }
                            else if (ent is Polyline pl)
                            {
                                totalLength += MathHelperManager.ComputePolylineLength(pl);
                            }
                            // Extend for other entity types if needed
                        }
                        else if (tr.GetObject(subEntry.Value, OpenMode.ForRead) is Xrecord xr)
                        {
                            // Optionally handle Xrecords if they store length info
                        }
                    }
                }

                tr.Commit();
            }

            return (quantity, totalLength);
        }

        public void HighlightGradeBeams(FoundationContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var ed = doc.Editor;

            // STEP 1 — Collect grade beams
            if (!GradeBeamNOD.TryGetGradeBeamPolylines(context, out List<Polyline> beams) ||
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

        // Track which documents have already registered the RegApp
        private readonly HashSet<Document> _regAppRegistered = new HashSet<Document>();
        /// <summary>
        /// Registers the FD_GRADEBEAM RegApp if not already registered for this document.
        /// </summary>
        public void RegisterGradeBeamRegApp(Document doc, Transaction tr)
        {
            if (_regAppRegistered.Contains(doc)) return;

            var db = doc.Database;
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForWrite);
            if (!rat.Has(NODCore.KEY_GRADEBEAM_SUBDICT))
            {
                var ratr = new RegAppTableRecord { Name = NODCore.KEY_GRADEBEAM_SUBDICT };
                rat.Add(ratr);
                tr.AddNewlyCreatedDBObject(ratr, true);
            }

            _regAppRegistered.Add(doc);
        }

        #region Geometry Calculations (derived)
        /// <summary>
        /// Generates edge polylines for all grade beams, adds them to ModelSpace, 
        /// and stores handles in the NOD.
        /// Returns the number of grade beams processed.
        /// </summary>
        public int GenerateEdgesForAllGradeBeams(
            FoundationContext context,
            double halfWidth)
        {
            if (context?.Document == null) return 0;

            var db = context.Document.Database;
            int createdCount = 0;

            using (context.Document.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var (handle, gbDict) in GradeBeamNOD.EnumerateGradeBeams(context, tr))
                {
                    if (!GradeBeamNOD.TryGetCenterline(context, tr, gbDict, out var centerlineId))
                        continue;

                    var centerline = tr.GetObject(centerlineId, OpenMode.ForRead) as Polyline;
                    if (centerline == null) continue;

                    // --- Generate offsets
                    var leftEdge = MathHelperManager.CreateOffsetPolyline(centerline, -halfWidth);
                    var rightEdge = MathHelperManager.CreateOffsetPolyline(centerline, halfWidth);

                    if (leftEdge == null || rightEdge == null) continue;

                    // --- Add to ModelSpace
                    ModelSpaceWriterService.AppendToModelSpace(tr, db, leftEdge);
                    ModelSpaceWriterService.AppendToModelSpace(tr, db, rightEdge);

                    // --- Store in NOD (handles only)
                    GradeBeamNOD.StoreEdgeObjects(
                        context,
                        tr,
                        centerlineId,
                        new[] { leftEdge.ObjectId },
                        new[] { rightEdge.ObjectId });

                    createdCount++;
                }

                tr.Commit();
            }

            return createdCount;
        }

        // ------------------------------------------------
        // Helper: Finds the grade beam dictionary a selected object belongs to
        // ------------------------------------------------
        internal string FindGradeBeamForObject(FoundationContext context, ObjectId selectedId)
        {
            if (context == null || selectedId.IsNull)
                return null;

            var doc = context.Document;
            var db = doc.Database;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                if (!nod.Contains(NODCore.ROOT)) return null;

                var root = (DBDictionary)tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForRead);
                if (!root.Contains(NODCore.KEY_GRADEBEAM_SUBDICT)) return null;

                var gradeBeamContainer = (DBDictionary)tr.GetObject(root.GetAt(NODCore.KEY_GRADEBEAM_SUBDICT), OpenMode.ForRead);

                foreach (var (centerlineHandle, gbId) in NODCore.EnumerateDictionary(gradeBeamContainer))
                {
                    if (!(tr.GetObject(gbId, OpenMode.ForRead) is DBDictionary gbDict)) continue;

                    if (ObjectInGradeBeamDictionary(tr, db, gbDict, selectedId))
                        return centerlineHandle;
                }
            }

            return null;
        }

        // ------------------------------------------------
        // Helper: Recursively checks if ObjectId is in a grade beam dictionary (including subdictionaries and XRecords)
        // ------------------------------------------------
        private static bool ObjectInGradeBeamDictionary(Transaction tr, Database db, DBDictionary dict, ObjectId selectedId)
        {
            foreach (var (key, id) in NODCore.EnumerateDictionary(dict))
            {
                if (id == selectedId)
                    return true;

                var obj = tr.GetObject(id, OpenMode.ForRead);

                if (obj is DBDictionary subDict && ObjectInGradeBeamDictionary(tr, db, subDict, selectedId))
                    return true;

                if (obj is Xrecord xrec && xrec.Data != null)
                {
                    foreach (TypedValue tv in xrec.Data)
                    {
                        if (tv.TypeCode == (int)DxfCode.Text && tv.Value is string handleStr)
                        {
                            try
                            {
                                Handle h = new Handle(Convert.ToInt64(handleStr, 16));
                                ObjectId oid = db.GetObjectId(false, h, 0);
                                if (oid == selectedId)
                                    return true;
                            }
                            catch { }
                        }
                    }
                }
            }

            return false;
        }

        // ------------------------------------------------
        // Deletes a grade beam (centerline, edges, metadata, subdicts) by handle
        // Returns total number of AutoCAD entities deleted
        // ------------------------------------------------
        public int DeleteGradeBeamRecursiveByHandle(FoundationContext context, string centerlineHandle)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(centerlineHandle)) return 0;

            var doc = context.Document;
            var db = doc.Database;
            int deletedCount = 0;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                if (!nod.Contains(NODCore.ROOT)) return 0;

                var root = (DBDictionary)tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForWrite);
                if (!root.Contains(NODCore.KEY_GRADEBEAM_SUBDICT)) return 0;

                var gradeBeamContainer = (DBDictionary)tr.GetObject(root.GetAt(NODCore.KEY_GRADEBEAM_SUBDICT), OpenMode.ForWrite);
                if (!gradeBeamContainer.Contains(centerlineHandle)) return 0;

                var gbNodeObj = tr.GetObject(gradeBeamContainer.GetAt(centerlineHandle), OpenMode.ForWrite);
                if (gbNodeObj is DBDictionary gbNode)
                {
                    // Recursively delete all subdictionary contents
                    deletedCount += DeleteNODDictionaryRecursive(context, tr, gbNode);

                    // Erase the grade beam dictionary itself
                    try { gbNode.Erase(); } catch { }

                    // Remove from parent container
                    try { gradeBeamContainer.Remove(centerlineHandle); } catch { }
                }

                tr.Commit();
            }

            return deletedCount;
        }

        // ------------------------------------------------
        // Recursively deletes all subdictionaries, XRecords, and AutoCAD entities referenced by handles
        // ------------------------------------------------
        private static int DeleteNODDictionaryRecursive(FoundationContext context, Transaction tr, DBDictionary dict)
        {
            int deletedCount = 0;

            foreach (var (key, id) in NODCore.EnumerateDictionary(dict))
            {
                try
                {
                    var obj = tr.GetObject(id, OpenMode.ForWrite);

                    switch (obj)
                    {
                        case DBDictionary subDict:
                            // Recurse into subdictionary
                            deletedCount += DeleteNODDictionaryRecursive(context, tr, subDict);

                            // Delete the subdictionary itself
                            try { subDict.Erase(); } catch { }
                            break;

                        case Xrecord xrec:
                            if (xrec.Data != null)
                            {
                                foreach (TypedValue tv in xrec.Data)
                                {
                                    if (tv.TypeCode == (int)DxfCode.Text && tv.Value is string handleStr)
                                    {
                                        if (NODCore.TryGetObjectIdFromHandleString(context, context.Document.Database, handleStr, out ObjectId oid))
                                        {
                                            if (oid.IsValid && !oid.IsErased)
                                            {
                                                try
                                                {
                                                    var ent = tr.GetObject(oid, OpenMode.ForWrite) as Entity;
                                                    ent?.Erase();
                                                    deletedCount++;
                                                }
                                                catch { }
                                            }
                                        }
                                    }
                                }
                            }

                            // Erase the XRecord itself
                            try { xrec.Erase(); } catch { }
                            break;

                        default:
                            // Delete any other DBObject
                            try { obj?.Erase(); deletedCount++; } catch { }
                            break;
                    }
                }
                catch { }
            }

            return deletedCount;
        }

        #endregion
    }
}


