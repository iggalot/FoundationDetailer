using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
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

        public const double INCHES_TO_DRAWING_UNITS = 1.0 / 12.0;
        public const int DEFAULT_VERTEX_QTY = 3;
        public static readonly Point3d DEFAULT_GRADEBEAMLENGTHTABLE_INSERT_PT = new Point3d(0, 0, 0);


        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Placeholder for future use")]
        public void Initialize(FoundationContext context)
        {

        }

        /// <summary>
        /// Creates the preliminary grade beam grid using default spacing parameters.
        /// This function divides the bounding box in vertical and horizontal directions with equal
        /// spacings between the given max and min limts.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="boundary"></param>
        /// <param name="horizMin"></param>
        /// <param name="horizMax"></param>
        /// <param name="vertMin"></param>
        /// <param name="vertMax"></param>
        /// <param name="vertexCount"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
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

        /// <summary>
        /// Primary function that turns an AutoCAD polyline object into a grade beam centerline for this application
        /// Creates the NOD entry for the specified grade beam centerline.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="pl"></param>
        /// <param name="tr"></param>
        /// <param name="appendToModelSpace"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        internal Polyline RegisterGradeBeam(FoundationContext context, Polyline pl, Transaction tr, bool appendToModelSpace = false)
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

        /// <summary>
        /// Creates a grade beam entity between two points with equally spaced vertices.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="start">start pt</param>
        /// <param name="end">end pt</param>
        /// <param name="vertexCount">Number of vertices for the gradebeam polyline</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        internal Polyline AddInterpolatedGradeBeam(FoundationContext context, Point3d start, Point3d end, int vertexCount = DEFAULT_VERTEX_QTY)
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

        /// <summary>
        /// Converts a polyline or line object in AutoCAD to a grade beam, creating the NOD tree entry for the centerline.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="oldEntityId"></param>
        /// <param name="vertexCount"></param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
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

        /// <summary>
        /// Deletes the edges for a single grade beam.  Keeps the NOD structure and centerline object.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="handle"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Deletes all edge elements for all grade beam in the NOD tree.  Clears the edges subdictionary in the NOD tree for the, but does not delete the centerline or NOD tree directory structure..
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
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
        internal int DeleteGradeBeamEdgesOnlyInternal(FoundationContext context, Transaction tr, string gradeBeamHandle)
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

        /// <summary>
        /// Function to fully delete a single grade beam from the NOD and AutoCAD drawing
        /// </summary>
        /// <param name="context"></param>
        /// <param name="handle"></param>
        /// <returns></returns>
        public int DeleteSingleGradeBeam(FoundationContext context, string handle)
        {
            if (context?.Document == null || string.IsNullOrWhiteSpace(handle))
                return 0;

            using (var lockDoc = context.Document.LockDocument())
            using (var tr = context.Document.Database.TransactionManager.StartTransaction())
            {
                int edgesDeleted, beamsDeleted;
                int deleted = DeleteGradeBeamFullInternal(context, tr, handle, out edgesDeleted, out beamsDeleted);

                tr.Commit();
                return deleted;
            }
        }

        /// <summary>
        /// Delete all grade beams in the NOD and their associated edges from AutoCAD drawing and the NOD tree.
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public int DeleteAllGradeBeams(FoundationContext context)
        {
            if (context?.Document == null)
                return 0;

            int totalBeamsDeleted = 0;

            using (var lockDoc = context.Document.LockDocument())
            using (var tr = context.Document.Database.TransactionManager.StartTransaction())
            {
                foreach (var (handle, _) in GradeBeamNOD.EnumerateGradeBeams(context, tr))
                {
                    int edgesDeleted, beamsDeleted;
                    DeleteGradeBeamFullInternal(context, tr, handle, out edgesDeleted, out beamsDeleted);
                    totalBeamsDeleted += beamsDeleted;
                }

                tr.Commit();
            }

            return totalBeamsDeleted;
        }

        /// <summary>
        /// Internal function to delete a gradebeam with a specified centerline handle.  Removes all edges and associated data.  Clears the NOD entry.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="tr"></param>
        /// <param name="handle"></param>
        /// <param name="edgesDeleted"></param>
        /// <param name="beamsDeleted"></param>
        /// <returns></returns>
        private int DeleteGradeBeamFullInternal(
            FoundationContext context,
            Transaction tr,
            string handle,
            out int edgesDeleted,
            out int beamsDeleted)
        {
            edgesDeleted = 0;
            beamsDeleted = 0;

            if (context?.Document == null || string.IsNullOrWhiteSpace(handle))
                return 0;

            // --- Get the beam dictionary node
            var beamNode = GradeBeamNOD.GetGradeBeamDictionaryByHandle(context, tr, handle);
            if (beamNode == null)
                return 0;

            // --- Delete all edge entities first
            edgesDeleted = DeleteGradeBeamEdgesOnlyInternal(context, tr, handle);

            // --- Delete the centerline AutoCAD object if it exists
            if (NODCore.TryGetObjectIdFromHandleString(context, context.Document.Database, handle, out ObjectId clId))
            {
                if (clId.IsValid && !clId.IsErased)
                {
                    var entity = tr.GetObject(clId, OpenMode.ForWrite) as Entity;
                    entity?.Erase();
                    beamsDeleted++; // count the centerline as a beam-related entity
                }
            }

            // --- Delete the full beam node from NOD, including SECTION metadata and FD_METADATA
            beamsDeleted += GradeBeamNOD.DeleteBeamFull(context, tr, handle);

            return beamsDeleted;
        }

        /// <summary>
        /// Holds the results of single grade beam deletion request
        /// </summary>
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

        /// <summary>
        /// Holds the results of a single multiple grade beam deletion request.
        /// </summary>
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
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            int quantity = 0;
            double totalLength = 0.0;

            var doc = context.Document;
            var db = doc.Database;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // Enumerate all grade beams from GradeBeamNOD
                foreach (var (_, gbDict) in GradeBeamNOD.EnumerateGradeBeams(context, tr))
                {
                    // Retrieve only centerline entities for length calculation
                    if (GradeBeamNOD.TryGetGradeBeamObjects(
                            context,
                            tr,
                            gbDict,
                            out var polys,
                            includeCenterline: true,
                            includeEdges: false))
                    {
                        foreach (var obj in polys)
                        {
                            if (obj == null)
                                continue;

                            // Cast to Entity explicitly
                            var ent = obj as Entity;
                            if (ent == null)
                                continue;

                            quantity++;

                            // Handle each supported type explicitly
                            var type = ent.GetType();

                            if (type == typeof(Line))
                            {
                                var line = (Line)ent;
                                totalLength += line.Length;
                            }
                            else if (type == typeof(Polyline))
                            {
                                var pl = (Polyline)ent;
                                totalLength += MathHelperManager.ComputePolylineLengthInFeet(pl);
                            }
                            // Extend here for other entity types if needed
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

            var ids = new List<ObjectId>();

            var doc = context.Document;
            var db = doc.Database;
            var ed = doc.Editor;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var (_, gbDict) in GradeBeamNOD.EnumerateGradeBeams(context, tr))
                {
                    if (GradeBeamNOD.TryGetGradeBeamObjects(
                            context,
                            tr,
                            gbDict,
                            out var polys,
                            includeCenterline: true,
                            includeEdges: false))
                    {
                        ids.AddRange(polys.Select(p => p.ObjectId));
                    }
                }

                tr.Commit();
            }

            if (ids.Count == 0)
            {
                ed.WriteMessage("\nNo grade beams found.");
                return;
            }

            ed.WriteMessage($"\nHighlighting {ids.Count} grade beam centerlines...");

            // ----------------------------------------------------
            // STEP 2 - Use shared highlighting service -- wait for user input to exit
            // ----------------------------------------------------
            HighlightService.HighlightEntities(context, ids);

            // ----------------------------------------------------
            // STEP 3 – Select the real centerlines
            // ----------------------------------------------------
            SelectionService.FocusAndHighlight(context, ids, "HighlightGradeBeams");
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
        public void GenerateEdgesForAllGradeBeams(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var db = doc.Database;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // --- Enumerate all beams in the NOD
                var beams = GradeBeamNOD.EnumerateGradeBeams(context, tr).ToList();
                if (beams.Count == 0)
                {
                    tr.Commit();
                    return;
                }

                // --- Delete existing edges for each beam (keeps centerlines)
                foreach (var (_, gbDict) in beams)
                {
                    GradeBeamNOD.DeleteBeamEdgesOnly(context, tr, gbDict);
                }

                // --- Recreate edges for each beam
                GradeBeamBuilder.CreateGradeBeams(context, tr);

                tr.Commit();
                doc.Editor.Regen();
            }
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
        public void DrawGradeBeamLengthTable(FoundationContext context, Point3d? insertPoint = null, double scale=1.0)
        {
            if (context == null) return;

            var doc = context.Document;
            var db = doc.Database;
            var ed = doc.Editor;

            // Use default insert point if none provided
            Point3d pt = insertPoint ?? GradeBeamManager.DEFAULT_GRADEBEAMLENGTHTABLE_INSERT_PT;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // Open BlockTable for write
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // --- Delete any existing block named GRADEBEAMLENGTHTABLE
                if (bt.Has("GRADEBEAMLENGTHTABLE"))
                {
                    var blockId = bt["GRADEBEAMLENGTHTABLE"];
                    var blockRefIds = new List<ObjectId>();

                    // find all references in model space
                    foreach (ObjectId id in ms)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                        if (ent != null && ent.Name == "GRADEBEAMLENGTHTABLE")
                            blockRefIds.Add(id);
                    }

                    // Erase references
                    foreach (var id in blockRefIds)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                        ent?.Erase();
                    }

                    // Erase existing block definition
                    var blockDef = tr.GetObject(blockId, OpenMode.ForWrite) as BlockTableRecord;
                    if (blockDef != null && !blockDef.IsErased)
                        blockDef.Erase();
                }

                // --- Get grade beam centerlines
                List<Polyline> gradeBeamCenterlines = GetAllGradeBeamCenterlines(context);
                if (gradeBeamCenterlines.Count == 0)
                {
                    ed.WriteMessage("\nNo grade beams currently defined. Skipping table generation.");
                    return;
                }

                int rowCount = gradeBeamCenterlines.Count + 2; // title + data + TOTAL row
                int colCount = 3; // Index, Label, Length

                // --- Prepare column header and data text
                string[] colHeaders = { "ID", "Label", "Length (ft)" };
                List<string>[] columnText = new List<string>[colCount];
                for (int c = 0; c < colCount; c++)
                    columnText[c] = new List<string>();

                columnText[0].AddRange(Enumerable.Range(1, gradeBeamCenterlines.Count).Select(i => i.ToString()));
                columnText[1].AddRange(Enumerable.Repeat("", gradeBeamCenterlines.Count)); // placeholder labels
                columnText[2].AddRange(gradeBeamCenterlines.Select(pl =>
                    Math.Ceiling(MathHelperManager.ComputePolylineLengthInFeet(pl)).ToString()));

                // --- Compute column widths
                double padding = 0.2 * scale; // inches
                double[] colWidths = new double[colCount];
                for (int c = 0; c < colCount; c++)
                {
                    double maxWidth = scale * Math.Max(
                        MeasureTextWidth(colHeaders[c]),
                        columnText[c].Select(text => MeasureTextWidth(text)).DefaultIfEmpty(0).Max()
                    );
                    colWidths[c] = maxWidth + padding;
                }

                // --- Row height and table size
                double rowHeight = 0.4 * scale;
                double tableWidth = colWidths.Sum();
                double tableHeight = rowCount * rowHeight;

                // --- Create a new block
                BlockTableRecord newBlock = new BlockTableRecord { Name = "GRADEBEAMLENGTHTABLE" };
                ObjectId blockIdNew = bt.Add(newBlock);
                tr.AddNewlyCreatedDBObject(newBlock, true);

                // --- Draw outer border rectangle
                Polyline outer = new Polyline(5);
                outer.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
                outer.AddVertexAt(1, new Point2d(tableWidth, 0), 0, 0, 0);
                outer.AddVertexAt(2, new Point2d(tableWidth, -tableHeight), 0, 0, 0);
                outer.AddVertexAt(3, new Point2d(0, -tableHeight), 0, 0, 0);
                outer.Closed = true;
                outer.LineWeight = LineWeight.LineWeight050;
                newBlock.AppendEntity(outer);
                tr.AddNewlyCreatedDBObject(outer, true);

                // --- Draw vertical lines starting below the title row
                double headerTopY = -rowHeight; // top of column headers (after title)
                double x = 0;
                for (int c = 0; c <= colCount; c++)
                {
                    Polyline vLine = new Polyline(2);
                    vLine.AddVertexAt(0, new Point2d(x, headerTopY), 0, 0, 0);
                    vLine.AddVertexAt(1, new Point2d(x, -tableHeight), 0, 0, 0);
                    vLine.LineWeight = (c == 0 || c == colCount) ? LineWeight.LineWeight050 : LineWeight.LineWeight015;
                    newBlock.AppendEntity(vLine);
                    tr.AddNewlyCreatedDBObject(vLine, true);

                    if (c < colCount)
                        x += colWidths[c];
                }

                // --- Draw horizontal lines
                double y = 0;
                for (int r = 0; r <= rowCount; r++)
                {
                    Polyline hLine = new Polyline(2);
                    hLine.AddVertexAt(0, new Point2d(0, y), 0, 0, 0);
                    hLine.AddVertexAt(1, new Point2d(tableWidth, y), 0, 0, 0);
                    hLine.LineWeight = (r == 0 || r == 1 || r == rowCount || r == rowCount - 1) ? LineWeight.LineWeight050 : LineWeight.LineWeight015;
                    newBlock.AppendEntity(hLine);
                    tr.AddNewlyCreatedDBObject(hLine, true);
                    y -= rowHeight;
                }

                // --- Insert title
                InsertMText(newBlock, tr,
                    new Point3d(tableWidth / 2, -rowHeight / 2, 0),
                    "GRADE BEAM LENGTHS", tableWidth, rowHeight, CellAlignment.MiddleCenter, scale);

                // --- Insert column headers
                x = 0;
                y = -rowHeight * 1.5;
                for (int c = 0; c < colCount; c++)
                {
                    InsertMText(newBlock, tr,
                        new Point3d(x + colWidths[c] / 2, y, 0),
                        colHeaders[c], colWidths[c], rowHeight, CellAlignment.MiddleCenter, scale);
                    x += colWidths[c];
                }

                // --- Insert data rows
                for (int r = 0; r < gradeBeamCenterlines.Count; r++)
                {
                    x = 0;
                    for (int c = 0; c < colCount; c++)
                    {
                        double yCenter = -rowHeight * (r + 2) - rowHeight / 2;
                        CellAlignment align = c == 0 ? CellAlignment.MiddleCenter :
                                              c == 1 ? CellAlignment.MiddleLeft :
                                                       CellAlignment.MiddleRight;
                        InsertMText(newBlock, tr,
                            new Point3d(x + colWidths[c] / 2, yCenter, 0),
                            columnText[c][r], colWidths[c], rowHeight, align, scale);
                        x += colWidths[c];
                    }
                }

                // --- Insert TOTAL row (drop one row below last data row)
                double totalLengthFeet = Math.Ceiling(gradeBeamCenterlines.Sum(pl => MathHelperManager.ComputePolylineLengthInFeet(pl)));
                double totalRowY = -rowHeight * (gradeBeamCenterlines.Count + 2.5);

                // Merge first two columns for TOTAL label
                InsertMText(newBlock, tr,
                    new Point3d((colWidths[0] + colWidths[1]) / 2, totalRowY, 0),
                    "TOTAL", colWidths[0] + colWidths[1], rowHeight, CellAlignment.MiddleCenter, scale);

                // Last column for total length
                InsertMText(newBlock, tr,
                    new Point3d(colWidths[0] + colWidths[1] + colWidths[2] / 2, totalRowY, 0),
                    totalLengthFeet.ToString() + " ft.", colWidths[2], rowHeight, CellAlignment.MiddleRight, scale);

                // --- Insert block reference into model space
                BlockReference blockRef = new BlockReference(pt, blockIdNew);
                ms.AppendEntity(blockRef);
                tr.AddNewlyCreatedDBObject(blockRef, true);

                tr.Commit();
            }
        }

        // --- Helper to insert MText centered in a rectangle
        private void InsertMText(BlockTableRecord ms, Transaction tr, Point3d position, string text, double width, double height, CellAlignment alignment, double scale = 1.0)
        {
            MText mtext = new MText
            {
                Location = position,
                Width = width,
                TextHeight = 0.25 * scale,
                Contents = text,
                Attachment = AttachmentPoint.MiddleCenter
            };

            switch (alignment)
            {
                case CellAlignment.MiddleLeft: mtext.Attachment = AttachmentPoint.MiddleLeft; break;
                case CellAlignment.MiddleRight: mtext.Attachment = AttachmentPoint.MiddleRight; break;
                case CellAlignment.MiddleCenter: mtext.Attachment = AttachmentPoint.MiddleCenter; break;
            }

            ms.AppendEntity(mtext);
            tr.AddNewlyCreatedDBObject(mtext, true);
        }

        // --- Dummy function for text width measurement in inches (replace with actual AutoCAD text metrics)
        private double MeasureTextWidth(string text)
        {
            return 0.3 * text.Length; // approx 0.3 inches per character
        }

        // --- Simple enum for alignment
        private enum CellAlignment
        {
            MiddleLeft,
            MiddleRight,
            MiddleCenter
        }

        
        public List<Polyline> GetAllGradeBeamCenterlines(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var result = new List<Polyline>();
            var doc = context.Document;
            var db = doc.Database;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // --- Enumerate all grade beams using the existing GradeBeamNOD helper
                foreach (var (_, gbDict) in GradeBeamNOD.EnumerateGradeBeams(context, tr))
                {
                    // TryGetGradeBeamObjects returns all entities; we only want centerlines
                    if (GradeBeamNOD.TryGetGradeBeamObjects(
                            context,
                            tr,
                            gbDict,
                            out var polys,
                            includeCenterline: true,
                            includeEdges: false))
                    {
                        foreach (var ent in polys)
                        {
                            if (ent is Polyline pl)
                                result.Add(pl);
                        }
                    }
                }

                tr.Commit();
            }

            return result;
        }
    }
}


