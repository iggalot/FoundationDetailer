using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using FoundationDetailsLibraryAutoCAD.AutoCAD;
using FoundationDetailsLibraryAutoCAD.AutoCAD.NOD;
using FoundationDetailsLibraryAutoCAD.Data;
using FoundationDetailsLibraryAutoCAD.Managers;
using FoundationDetailsLibraryAutoCAD.Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace FoundationDetailer.AutoCAD
{
    public class GradeBeamManager
    {
        public const double DEFAULT_BEAM_WIDTH_IN = 10.0;
        public const double INCHES_TO_DRAWING_UNITS = 1.0 / 12.0;
        public const int DEFAULT_VERTEX_QTY = 3; 


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
    int vertexCount = GradeBeamManager.DEFAULT_VERTEX_QTY)
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
        internal Polyline AddInterpolatedGradeBeam(FoundationContext context, Point3d start, Point3d end, int vertexCount=DEFAULT_VERTEX_QTY)
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
            GradeBeamNOD.DeleteGradeBeamNode(context, oldEnt.Handle.ToString());

            // --- Delete old entity from ModelSpace ---
            oldEnt.UpgradeOpen();
            oldEnt.Erase();
        }




        internal int DeleteAllGradeBeams(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var db = doc.Database;
            int deleted = 0;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // Open NOD root dictionary
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                if (!nod.Contains(NODCore.ROOT))
                    return 0;

                var root = (DBDictionary)tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForRead);
                if (!root.Contains(NODCore.KEY_GRADEBEAM_SUBDICT))
                    return 0;

                var gbRoot = (DBDictionary)tr.GetObject(root.GetAt(NODCore.KEY_GRADEBEAM_SUBDICT), OpenMode.ForWrite);

                // Collect keys safely
                var gradeBeamKeys = new List<string>();
                foreach (DBDictionaryEntry entry in gbRoot)
                    gradeBeamKeys.Add(entry.Key);

                foreach (var key in gradeBeamKeys)
                {
                    deleted += DeleteGradeBeamInternal(context, tr, gbRoot, key);
                }

                tr.Commit();
            }

            return deleted;
        }

        /// <summary>
        /// Deletes all grade beam edge entities in the drawing,
        /// leaving centerlines and NOD metadata intact.
        /// Returns the total number of edge entities erased.
        /// C# 7.3 compliant.
        /// </summary>
        internal static int DeleteAllGradeBeamEdges(FoundationContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var db = doc.Database;
            var ed = doc.Editor;

            int totalDeleted = 0;
            List<string> handles;

            // --- Collect all grade beam handles in one transaction
            using (var tr = db.TransactionManager.StartTransaction())
            {
                handles = GradeBeamNOD.EnumerateGradeBeams(context, tr)
                                       .Select(h => h.Handle)
                                       .ToList();
                tr.Commit();
            }

            if (handles.Count == 0)
            {
                ed.WriteMessage("\n[DEBUG] No grade beams found. No edges deleted.");
                return 0;
            }

            // --- Delete edges for each grade beam individually
            foreach (var handle in handles)
            {
                totalDeleted += DeleteGradeBeamEdgesOnly(context, handle);
            }

            ed.WriteMessage($"\n[DEBUG] Total grade beam edges deleted: {totalDeleted}");
            return totalDeleted;
        }



        /// <summary>
        /// INTERNAL: Deletes a single grade beam inside an existing transaction.
        /// Deletes edges, centerline, and removes the NOD dictionary.
        /// </summary>
        internal int DeleteGradeBeamInternal(
            FoundationContext context,
            Transaction tr,
            DBDictionary gbRoot,
            string handle)
        {
            if (!gbRoot.Contains(handle)) return 0;

            int deleted = 0;
            var gbDict = (DBDictionary)tr.GetObject(gbRoot.GetAt(handle), OpenMode.ForWrite);

            deleted += DeleteGradeBeamEdgesOnlyInternal(context, tr, handle);

            if (GradeBeamNOD.TryGetCenterline(context, tr, gbDict, out ObjectId clId))
            {
                if (tr.GetObject(clId, OpenMode.ForWrite) is Entity ent && !ent.IsErased)
                {
                    ent.Erase();
                    deleted++;
                }
            }

            gbRoot.Remove(handle);
            return deleted;
        }


        /// <summary>
        /// Deletes an entire grade beam by key (edges, centerline, and NOD dictionary).
        /// Returns the number of AutoCAD entities erased.
        /// </summary>
        internal int DeleteGradeBeam(FoundationContext context, string gradeBeamKey)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(gradeBeamKey))
                throw new ArgumentNullException(nameof(gradeBeamKey));

            int deleted = 0;
            var doc = context.Document;
            var db = doc.Database;
            var ed = doc.Editor;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(
                    db.NamedObjectsDictionaryId, OpenMode.ForRead);

                if (!nod.Contains(NODCore.ROOT))
                    return 0;

                var root = (DBDictionary)tr.GetObject(
                    nod.GetAt(NODCore.ROOT), OpenMode.ForRead);

                if (!root.Contains(NODCore.KEY_GRADEBEAM_SUBDICT))
                    return 0;

                var gbRoot = (DBDictionary)tr.GetObject(
                    root.GetAt(NODCore.KEY_GRADEBEAM_SUBDICT),
                    OpenMode.ForWrite);

                deleted = DeleteGradeBeamInternal(
                    context, tr, gbRoot, gradeBeamKey);

                tr.Commit();
            }

            ed.WriteMessage($"\n[GradeBeamManager] Deleted {deleted} entities.");
            return deleted;
        }

        internal int DeleteGradeBeamsBatch(
            FoundationContext context,
            IEnumerable<string> handles)
        {
            var list = handles?.Distinct().ToList();
            if (list == null || list.Count == 0) return 0;

            int deleted = 0;
            var doc = context.Document;
            var db = doc.Database;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var gbRoot = GradeBeamNOD.GetGradeBeamRoot(tr, db);
                foreach (var handle in list)
                    deleted += DeleteGradeBeamInternal(context, tr, gbRoot, handle);

                tr.Commit();
            }

            GenerateEdgesForAllGradeBeams(context);
            return deleted;
        }

        /// <summary>
        /// Deletes all grade beam edge entities in the drawing AND clears their Xrecords,
        /// but keeps the edges sub-dictionary intact for each beam.
        /// Returns the total number of AutoCAD entities erased.
        /// </summary>
        internal static int DeleteAllGradeBeamEdgesAndClearNOD(FoundationContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var db = doc.Database;
            var totalDeleted = 0;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // --- Enumerate all grade beams
                foreach (var (handle, gbDict) in GradeBeamNOD.EnumerateGradeBeams(context, tr))
                {
                    // --- Skip beams without edges sub-dictionary
                    if (!NODCore.TryGetNestedSubDictionary(tr, gbDict, out DBDictionary edgesDict, NODCore.KEY_EDGES_SUBDICT))
                        continue;

                    // --- Collect keys to safely iterate
                    var keys = edgesDict.Cast<DBDictionaryEntry>().Select(e => e.Key).ToList();

                    foreach (var edgeKey in keys)
                    {
                        var xrecId = edgesDict.GetAt(edgeKey);
                        var xrec = tr.GetObject(xrecId, OpenMode.ForRead) as Xrecord;

                        if (xrec?.Data != null)
                        {
                            foreach (var tv in xrec.Data)
                            {
                                if (tv.TypeCode != (int)DxfCode.Text) continue;

                                string handleStr = tv.Value as string;
                                if (string.IsNullOrWhiteSpace(handleStr)) continue;

                                if (!NODCore.TryGetObjectIdFromHandleString(context, db, handleStr, out ObjectId oid))
                                    continue;

                                if (oid.IsValid && !oid.IsErased)
                                {
                                    (tr.GetObject(oid, OpenMode.ForWrite) as Entity)?.Erase();
                                    totalDeleted++;
                                }
                            }
                        }

                        // --- Remove the Xrecord itself from the edges dictionary
                        edgesDict.Remove(edgeKey);
                        xrec?.UpgradeOpen();
                        xrec?.Erase();
                    }
                }

                tr.Commit();
            }

            doc.Editor.WriteMessage($"\n[DEBUG] Deleted {totalDeleted} grade beam edges and cleared Xrecords (edges sub-dictionary preserved).");
            return totalDeleted;
        }



        /// <summary>
        /// Deletes all edge entities of a single grade beam but keeps centerline and NOD dictionary.
        /// Returns the number of entities erased. Provides debug messages for each edge.
        /// </summary>
        internal static int DeleteGradeBeamEdgesOnly(FoundationContext context, string gradeBeamKey)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(gradeBeamKey)) return 0;

            var doc = context.Document;
            var db = doc.Database;
            var ed = doc.Editor;
            int deleted = 0;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // Get the grade beam root dictionary
                var gbRoot = GradeBeamNOD.GetGradeBeamRoot(tr, db);
                if (!gbRoot.Contains(gradeBeamKey))
                {
                    ed.WriteMessage($"\n[DEBUG] Grade beam '{gradeBeamKey}' not found.");
                    return 0;
                }

                var gbDict = (DBDictionary)tr.GetObject(gbRoot.GetAt(gradeBeamKey), OpenMode.ForWrite);

                if (!GradeBeamNOD.HasEdgesDictionary(tr, db, gradeBeamKey))
                {
                    ed.WriteMessage($"\n[DEBUG] Grade beam '{gradeBeamKey}' has no edges.");
                    return 0;
                }

                var edgesDict = GradeBeamNOD.GetEdgesDictionary(tr, db, gradeBeamKey, forWrite: true);

                // Copy keys safely
                var edgeKeys = new List<string>();
                foreach (DictionaryEntry entry in edgesDict)
                {
                    edgeKeys.Add((string)entry.Key);
                }

                foreach (var edgeKey in edgeKeys)
                {
                    var xrecId = edgesDict.GetAt(edgeKey);
                    var xrec = tr.GetObject(xrecId, OpenMode.ForWrite) as Xrecord;

                    if (xrec?.Data != null)
                    {
                        foreach (TypedValue tv in xrec.Data)
                        {
                            if (tv.TypeCode != (int)DxfCode.Text) continue;

                            string handleStr = tv.Value as string;
                            if (string.IsNullOrWhiteSpace(handleStr)) continue;

                            if (!NODCore.TryGetObjectIdFromHandleString(context, db, handleStr, out ObjectId oid))
                                continue;

                            if (oid.IsValid && !oid.IsErased)
                            {
                                (tr.GetObject(oid, OpenMode.ForWrite) as Entity)?.Erase();
                                deleted++;
                                ed.WriteMessage($"\n[DEBUG] Deleted edge '{edgeKey}' ({oid.Handle}).");
                            }
                        }
                    }

                    // Remove the Xrecord from dictionary
                    edgesDict.Remove(edgeKey);
                    xrec?.Erase();
                    ed.WriteMessage($"\n[DEBUG] Removed edge Xrecord '{edgeKey}'.");
                }

                tr.Commit();
            }

            return deleted;
        }



        internal int DeleteGradeBeamEdgesOnlyInternal(
            FoundationContext context,
            Transaction tr,
            string gradeBeamKey)
        {
            int deleted = 0;
            var db = context.Document.Database;

            if (!GradeBeamNOD.HasEdgesDictionary(tr, db, gradeBeamKey))
                return 0;

            var edgesDict = GradeBeamNOD.GetEdgesDictionary(tr, db, gradeBeamKey, forWrite: true);

            // Copy keys to a list to avoid modifying collection while iterating
            var keys = new List<string>();
            foreach (DBDictionaryEntry entry in edgesDict)
                keys.Add(entry.Key);

            foreach (var edgeKey in keys)
            {
                var xrecId = edgesDict.GetAt(edgeKey);
                var xrec = tr.GetObject(xrecId, OpenMode.ForRead) as Xrecord;

                if (xrec?.Data != null)
                {
                    foreach (TypedValue tv in xrec.Data)
                    {
                        if (tv.TypeCode != (int)DxfCode.Text) continue;

                        string handleStr = tv.Value as string;
                        if (string.IsNullOrWhiteSpace(handleStr)) continue;

                        if (!NODCore.TryGetObjectIdFromHandleString(context, db, handleStr, out ObjectId oid))
                            continue;

                        if (oid.IsValid && !oid.IsErased)
                        {
                            (tr.GetObject(oid, OpenMode.ForWrite) as Entity)?.Erase();
                            deleted++;
                        }
                    }
                }

                // Erase the Xrecord itself from FD_EDGES
                xrec?.UpgradeOpen(); // ensure writable
                xrec?.Erase();
            }

            return deleted;
        }



        // ------------------------------------------------
        // Deletes a grade beam (centerline, edges, metadata, subdicts) by handle
        // Returns total number of AutoCAD entities deleted
        // ------------------------------------------------
        internal int DeleteGradeBeamRecursiveByHandle(FoundationContext context, string centerlineHandle)
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
            var db = doc.Database;

            // STEP 1 Collect grade beams
            var allPolylines = new List<Polyline>();

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // Enumerate all grade beams
                foreach (var (_, gbDict) in GradeBeamNOD.EnumerateGradeBeams(context, tr))
                {
                    // Grab all polylines (centerline + edges) for this beam
                    if (GradeBeamNOD.TryGetGradeBeamObjects(
                            context, tr, gbDict, out var polys,
                            includeCenterline: true, includeEdges: true))
                    {
                        allPolylines.AddRange(polys);
                    }
                }

                // allPolylines now contains every centerline + edge in the drawing
            }

            // STEP 2 Extract ObjectIds
            var ids = allPolylines
                .Where(b => b != null)
                .Select(b => b.ObjectId);

            // STEP 3 Use SelectionService to filter valid IDs and get invalid ones for logging
            var validIds = SelectionService.FilterValidIds(context, ids, out List<ObjectId> invalidIds);

            // STEP 4 Log diagnostics
            ed.WriteMessage($"\n[GradeBeam] Found={allPolylines.Count}, Valid={validIds.Count}, Invalid={invalidIds.Count}");
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
        public void GenerateEdgesForAllGradeBeams(
            FoundationContext context,
            double halfWidth = DEFAULT_BEAM_WIDTH_IN)
        {
            GradeBeamBuilder.CreateGradeBeams(context, halfWidth);
            return;
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


