using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using FoundationDetailer.Managers;
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
            if (boundary == null)
                throw new ArgumentNullException(nameof(boundary));

            if (context?.Document == null)
                throw new ArgumentNullException(nameof(context));

            var db = context.Document.Database;
            List<Polyline> createdBeams = new List<Polyline>();

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
                // -------------------------------
                // HORIZONTAL BEAMS
                // -------------------------------
                foreach (var linePts in gridlines.Horizontal)
                {
                    List<Point2d> verts = linePts
                        .Select(p => new Point2d(p.X, p.Y))
                        .ToList();

                    Polyline original = PolylineConversionService
                        .CreatePolylineFromVertices(verts);

                    List<Polyline> trimmedPieces = null;
                    trimmedPieces = MathHelperManager
                        .TrimPolylineToPolyline(original, boundary);

                    if (trimmedPieces == null || trimmedPieces.Count == 0)
                    {
                        // Fully outside → register original
                        RegisterGradeBeam(context, original, tr, true);
                        createdBeams.Add(original);
                    }
                    else
                    {
                        foreach (var piece in trimmedPieces)
                        {
                            RegisterGradeBeam(context, piece, tr, true);
                            createdBeams.Add(piece);
                        }

                        // dispose original if not used
                        original.Dispose();
                    }
                }

                // -------------------------------
                // VERTICAL BEAMS
                // -------------------------------
                foreach (var linePts in gridlines.Vertical)
                {
                    List<Point2d> verts = linePts
                        .Select(p => new Point2d(p.X, p.Y))
                        .ToList();

                    Polyline original = PolylineConversionService
                        .CreatePolylineFromVertices(verts);

                    List<Polyline> trimmedPieces = null;
                    trimmedPieces = MathHelperManager
                        .TrimPolylineToPolyline(original, boundary);

                    if (trimmedPieces == null || trimmedPieces.Count == 0)
                    {
                        RegisterGradeBeam(context, original, tr, true);
                        createdBeams.Add(original);
                    }
                    else
                    {
                        foreach (var piece in trimmedPieces)
                        {
                            RegisterGradeBeam(context, piece, tr, true);
                            createdBeams.Add(piece);
                        }

                        original.Dispose();
                    }
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

        internal void ConvertToGradeBeam(FoundationContext context, ObjectId oldEntityId, int vertexCount)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (oldEntityId.IsNull) throw new ArgumentException("Invalid ObjectId.", nameof(oldEntityId));

            var doc = context.Document;
            var db = doc.Database;

            using (doc.LockDocument())
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var oldEnt = tr.GetObject(oldEntityId, OpenMode.ForRead) as Entity;
                    if (oldEnt == null)
                        throw new ArgumentException("Object is not a valid entity.", nameof(oldEntityId));

                    // --- Convert old entity to new Polyline
                    var verts = PolylineConversionService.GetVertices(oldEnt);

                    // Ensure minimum vertex count
                    verts = PolylineConversionService.EnsureMinimumVertices(verts, vertexCount);

                    Polyline newPl = PolylineConversionService.CreatePolylineFromVertices(verts, oldEnt);

                    // --- Append to ModelSpace
                    ModelSpaceWriterService.AppendToModelSpace(tr, db, newPl);

                    // --- Register GradeBeam metadata in NOD
                    RegisterGradeBeam(context, newPl, tr, appendToModelSpace: false);

                    // --- Remove old GradeBeam NOD entry if it exists
                    DeleteGradeBeamNode(context, tr, oldEnt.Handle.ToString());

                    // --- Delete old entity
                    oldEnt.UpgradeOpen();
                    oldEnt.Erase();

                    tr.Commit();
                }
            }
        }



        public int DeleteEdgesForSingleBeam(FoundationContext context, string handle)
        {
            if (context?.Document == null || string.IsNullOrWhiteSpace(handle))
                return 0;

            using (var lockDoc = context.Document.LockDocument())
            using (var tr = context.Document.Database.TransactionManager.StartTransaction())
            {
                int deleted = DeleteGradeBeamEdgesOnlyInternal(context, tr, handle);
                tr.Commit();
                return deleted;
            }
        }


        public int DeleteEdgesForMultipleBeams(FoundationContext context, IEnumerable<string> handles)
        {
            if (context?.Document == null || handles == null)
                return 0;

            int total = 0;
            using (var lockDoc = context.Document.LockDocument())
            using (var tr = context.Document.Database.TransactionManager.StartTransaction())
            {
                foreach (var handle in handles)
                {
                    total += DeleteGradeBeamEdgesOnlyInternal(context, tr, handle);
                }

                tr.Commit();
            }

            return total;
        }




        public int DeleteEdgesForAllBeams(FoundationContext context)
        {
            if (context?.Document == null)
                return 0;

            int total = 0;
            using (var lockDoc = context.Document.LockDocument())
            {
                using (var tr = context.Document.Database.TransactionManager.StartTransaction())
                {
                    foreach (var (handle, _) in GradeBeamNOD.EnumerateGradeBeams(context, tr))
                    {
                        total += DeleteGradeBeamEdgesOnlyInternal(context, tr, handle);
                    }

                    tr.Commit();
                }
            }

            return total;
        }

        /// <summary>
        /// Deletes all edge entities of a single grade beam but keeps centerline and NOD dictionary.
        /// Returns the number of entities erased.
        /// </summary>
        internal int DeleteGradeBeamEdgesOnlyInternal(
            FoundationContext context,
            Transaction tr,
            string gradeBeamHandle)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (string.IsNullOrWhiteSpace(gradeBeamHandle))
                return 0;

            int deleted = 0;
            var db = context.Document.Database;

            // --- Get the beam's edges dictionary
            if (!GradeBeamNOD.HasEdgesDictionary(tr, db, gradeBeamHandle))
                return 0;

            var edgesDict = GradeBeamNOD.GetBeamEdgesDictionary(tr, db, gradeBeamHandle, forWrite: true);

            // --- Copy keys to safely iterate
            var keys = new List<string>();
            foreach (DictionaryEntry entry in edgesDict)
                keys.Add((string)entry.Key);

            foreach (var edgeKey in keys)
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
                        }
                    }
                }

                // --- Remove the Xrecord itself from the dictionary
                edgesDict.Remove(edgeKey);
                xrec?.Erase();
            }

            return deleted;
        }

        public int DeleteSingleBeam(FoundationContext context, string handle)
        {
            if (context?.Document == null || string.IsNullOrWhiteSpace(handle))
                return 0;

            using (var lockDoc = context.Document.LockDocument())
            {
                using (var tr = context.Document.Database.TransactionManager.StartTransaction())
                {
                    int deleted = DeleteBeamFullInternal(context, tr, handle);
                    tr.Commit();
                    return deleted;
                }
            }
        }

        public DeleteMultipleGradeBeamResult DeleteMultipleGradeBeamsByHandles(
            FoundationContext context,
            IEnumerable<string> handles,
            Transaction tr)
        {
            if (context?.Document == null || handles == null || tr == null)
                return new DeleteMultipleGradeBeamResult { Success = false, Message = "Invalid input." };

            int totalBeamsDeleted = 0;
            int totalEdgesDeleted = 0;

            foreach (var handle in handles)
            {
                int edgesDeleted = 0;
                int beamsDeleted = 0;

                // --- Call the internal that returns out params
                DeleteGradeBeamFullInternal(context, tr, handle, out edgesDeleted, out beamsDeleted);

                totalEdgesDeleted += edgesDeleted;
                totalBeamsDeleted += beamsDeleted;
            }

            return new DeleteMultipleGradeBeamResult
            {
                Success = true,
                GradeBeamsDeleted = totalBeamsDeleted,
                EdgesDeleted = totalEdgesDeleted
            };
        }



        public int DeleteAllGradeBeams(FoundationContext context)
        {
            using (var lockDoc = context.Document.LockDocument())
            using (var tr = context.Document.Database.TransactionManager.StartTransaction())
            {
                var beams = GradeBeamNOD
                    .EnumerateGradeBeams(context, tr)
                    .Select(b => b.Handle)
                    .ToList();

                int total = beams.Sum(h =>
                    GradeBeamNOD.DeleteBeamFull(context, tr, h));

                tr.Commit();
                return total;
            }
        }

        private int DeleteGradeBeamFullInternal(FoundationContext context, Transaction tr, string handle, out int edgesDeleted, out int beamsDeleted)
        {
            edgesDeleted = 0;
            beamsDeleted = 0;

            if (context?.Document == null || string.IsNullOrWhiteSpace(handle))
                return 0;

            // --- Look up the beam dictionary
            var gbDict = GradeBeamNOD.GetGradeBeamDictionaryByHandle(context, tr, handle);
            if (gbDict == null) return 0;

            // --- Delete edges + centerline + metadata
            edgesDeleted = DeleteGradeBeamEdgesOnlyInternal(context, tr, handle);

            return GradeBeamNOD.DeleteBeamFull(context, tr, handle);
        }

        // Overload without out params for single-beam deletion
        private int DeleteBeamFullInternal(FoundationContext context, Transaction tr, string handle)
        {
            int edges, beams;
            DeleteGradeBeamFullInternal(context, tr, handle, out edges, out beams);
            return edges + beams;
        }








        public class DeleteSingleGradeBeamEdgesResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public string GradeBeamHandle { get; set; }
            public int EdgesDeleted { get; set; }

            private DeleteSingleGradeBeamEdgesResult()
            {
            }

            public static DeleteSingleGradeBeamEdgesResult CreateFailure(string message)
            {
                return new DeleteSingleGradeBeamEdgesResult
                {
                    Success = false,
                    Message = message,
                    GradeBeamHandle = null,
                    EdgesDeleted = 0
                };
            }

            public static DeleteSingleGradeBeamEdgesResult CreateSuccess(string handle, int deleted)
            {
                return new DeleteSingleGradeBeamEdgesResult
                {
                    Success = true,
                    Message = null,
                    GradeBeamHandle = handle,
                    EdgesDeleted = deleted
                };
            }
        }
        public class DeleteMultipleGradeBeamResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public int GradeBeamsDeleted { get; set; }
            public int EdgesDeleted { get; set; }

            public static DeleteMultipleGradeBeamResult CreateSuccess(int beams, int edges)
            {
                return new DeleteMultipleGradeBeamResult
                {
                    Success = true,
                    GradeBeamsDeleted = beams,
                    EdgesDeleted = edges
                };
            }

            public static DeleteMultipleGradeBeamResult CreateFailure(string message)
            {
                return new DeleteMultipleGradeBeamResult
                {
                    Success = false,
                    Message = message
                };
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
        public void GenerateEdgesForAllGradeBeams(FoundationContext context, double halfWidth = DEFAULT_BEAM_WIDTH_IN, Transaction tr = null)
        {
            //if (tr != null)
            //{
            //    // Use the provided transaction
            //    GradeBeamBuilder.CreateGradeBeams(context, halfWidth, tr);
            //}
            //else
            //{
            // No transaction provided: create our own LockDocument + transaction
            DeleteEdgesForAllBeams(context);
            GradeBeamBuilder.CreateGradeBeams(context, halfWidth);
            //}
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

        /// <summary>
        /// Deletes a single grade beam node and all its subdictionaries/XRecords.
        /// Only affects the NOD structure; does NOT touch AutoCAD entities.
        /// </summary>
        /// <param name="context">Current foundation context</param>
        /// <param name="centerlineHandle">Handle string of the centerline for the grade beam to delete</param>
        /// <returns>True if deletion succeeded, false otherwise</returns>
        internal bool DeleteGradeBeamNode(FoundationContext context, Transaction tr, string centerlineHandle)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(centerlineHandle)) throw new ArgumentNullException(nameof(centerlineHandle));

            Document doc = context.Document;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                // Get the top-level NOD
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

                if (!nod.Contains(NODCore.ROOT))
                {
                    ed.WriteMessage($"\n[GradeBeamNOD] No {NODCore.ROOT} dictionary exists.");
                    return false;
                }

                // Get the FD_GRADEBEAM container
                var root = (DBDictionary)tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForWrite);
                if (!root.Contains(NODCore.KEY_GRADEBEAM_SUBDICT))
                {
                    ed.WriteMessage($"\n[GradeBeamNOD] No {NODCore.KEY_GRADEBEAM_SUBDICT} container exists.");
                    return false;
                }

                var gradeBeamContainer = (DBDictionary)tr.GetObject(root.GetAt(NODCore.KEY_GRADEBEAM_SUBDICT), OpenMode.ForWrite);

                // Delete using NODCore helper
                bool deleted = NODCore.DeleteNODSubDictionary(context, tr, gradeBeamContainer, centerlineHandle);

                if (deleted)
                    tr.Commit();

                return deleted;
            }
            catch (Exception ex)
            {
                ed.WriteMessage($"\n[GradeBeamNOD] Failed to delete grade beam: {ex.Message}");
                return false;
            }
        }

        public IEnumerable<string> ResolveGradeBeamHandles(FoundationContext context, IEnumerable<ObjectId> objectIds)
        {
            var handles = new HashSet<string>();
            if (context?.Document == null || objectIds == null)
                return handles;

            using (var tr = context.Document.Database.TransactionManager.StartTransaction())
            {
                foreach (var id in objectIds)
                {
                    if (GradeBeamNOD.TryResolveOwningGradeBeam(context, tr, id, out string handle, out bool _, out bool _))
                        handles.Add(handle);
                }
            }
            return handles;
        }





        #endregion
    }
}


