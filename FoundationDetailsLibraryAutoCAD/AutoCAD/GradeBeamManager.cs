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
                        RegisterInteriorGradeBeam(context, original, tr, true);
                        createdBeams.Add(original);
                    }
                    else
                    {
                        foreach (var piece in trimmedPieces)
                        {
                            RegisterInteriorGradeBeam(context, piece, tr, true);
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
                        RegisterInteriorGradeBeam(context, original, tr, true);
                        createdBeams.Add(original);
                    }
                    else
                    {
                        foreach (var piece in trimmedPieces)
                        {
                            RegisterInteriorGradeBeam(context, piece, tr, true);
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
        internal Polyline RegisterInteriorGradeBeam(
            FoundationContext context,
            Polyline pl,
            Transaction tr,
            bool appendToModelSpace = false)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (pl == null) throw new ArgumentNullException(nameof(pl));
            if (tr == null) throw new ArgumentNullException(nameof(tr));

            var db = context.Document.Database;

            // --- Ensure polyline is in ModelSpace first
            if (appendToModelSpace && pl.ObjectId.IsNull)
                ModelSpaceWriterService.AppendToModelSpace(tr, db, pl);

            // --- Use polyline handle as the NOD key
            string handle = pl.Handle.ToString();

            // --- This will safely create the grade beam node under FD_GRADEBEAMS
            var gbNode = NODCore.GetOrCreateInteriorGradeBeamNode(tr, db, handle);

            // --- Optional: delete existing edges if needed
            DeleteGradeBeamInteriorBeam_EdgesOnlyInternal(context, tr, handle);

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
        internal Polyline AddInterpolatedInteriorGradeBeam(FoundationContext context, Point3d start, Point3d end, int vertexCount = DEFAULT_VERTEX_QTY)
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
                RegisterInteriorGradeBeam(context, pl, tr, appendToModelSpace: true);

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
        internal void ConvertToInteriorGradeBeam(FoundationContext context, ObjectId oldEntityId, int vertexCount)
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
                    RegisterInteriorGradeBeam(context, newPl, tr, appendToModelSpace: false);

                    // --- Remove old GradeBeam NOD entry if it exists
                    DeleteGradeBeamNode(context, tr, oldEnt.Handle.ToString());

                    // --- Delete old entity
                    oldEnt.UpgradeOpen();
                    oldEnt.Erase();

                    tr.Commit();
                }
            }
        }

        #region DELETE FUNCTIONS

        public int DeleteEdgesForMultipleInteriorBeams(FoundationContext context, IEnumerable<string> handles)
        {
            if (context?.Document == null || handles == null)
                return 0;

            int total = 0;
            using (var lockDoc = context.Document.LockDocument())
            using (var tr = context.Document.Database.TransactionManager.StartTransaction())
            {
                foreach (var handle in handles)
                {
                    total += DeleteGradeBeamInteriorBeam_EdgesOnlyInternal(context, tr, handle);
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
        public static int DeleteEdgesForAllInteriorGradeBeams(FoundationContext context)
        {
            if (context?.Document == null)
                return 0;

            int total = 0;
            using (var lockDoc = context.Document.LockDocument())
            using (var tr = context.Document.Database.TransactionManager.StartTransaction())
            {
                foreach (var (handle, _) in GradeBeamInteriorNOD.EnumerateInteriorGradeBeams(context, tr))
                {
                    total += DeleteGradeBeamInteriorBeam_EdgesOnlyInternal(context, tr, handle);
                }

                tr.Commit();
            }

            return total;
        }

        /// <summary>
        /// Deletes all edge entities of a single grade beam but keeps centerline and NOD dictionary.
        /// </summary>
        internal static int DeleteGradeBeamInteriorBeam_EdgesOnlyInternal(FoundationContext context, Transaction tr, string gradeBeamHandle)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (string.IsNullOrWhiteSpace(gradeBeamHandle))
                return 0;

            int deleted = 0;
            var db = context.Document.Database;

            // --- Get the beam's edges dictionary (read-only)
            if (!NODCore.TryGetGradeBeamInteriorBeamNode(tr, db, gradeBeamHandle, out DBDictionary beamNode) ||
                !NODCore.TryGetBeamEdges(tr, beamNode, out DBDictionary edgesDict))
            {
                return 0;
            }

            // --- Upgrade edges dictionary to write before modifying
            edgesDict.UpgradeOpen();

            // --- Copy keys to safely iterate while deleting
            var keys = new List<string>();
            foreach (DictionaryEntry entry in edgesDict)
                keys.Add((string)entry.Key);

            foreach (var edgeKey in keys)
            {
                // Get Xrecord (upgrade to write to delete)
                var xrecId = edgesDict.GetAt(edgeKey);
                var xrec = tr.GetObject(xrecId, OpenMode.ForWrite) as Xrecord;

                if (xrec?.Data != null)
                {
                    foreach (TypedValue tv in xrec.Data)
                    {
                        if (tv.TypeCode != (int)DxfCode.Text) continue;

                        string handleStr = tv.Value as string;
                        if (string.IsNullOrWhiteSpace(handleStr)) continue;

                        // Get the AutoCAD entity by handle
                        if (!NODCore.TryGetObjectIdFromHandleString(tr, db, handleStr, out ObjectId oid))
                            continue;

                        if (oid.IsValid && !oid.IsErased)
                        {
                            // Upgrade the entity to write before erasing
                            var ent = tr.GetObject(oid, OpenMode.ForWrite) as Entity;
                            ent?.Erase();
                            deleted++;
                        }
                    }
                }

                // Remove the Xrecord from the edges dictionary
                if (edgesDict.Contains(edgeKey))
                    edgesDict.Remove(edgeKey);

                // Erase the Xrecord itself
                if (xrec != null && !xrec.IsErased)
                    xrec.Erase();
            }

            return deleted;
        }

        /// <summary>
        /// Function to fully delete a single grade beam from the NOD and AutoCAD drawing
        /// </summary>
        /// <param name="context"></param>
        /// <param name="handle"></param>
        /// <returns></returns>
        public int DeleteSingleInteriorGradeBeam(FoundationContext context, string handle)
        {
            if (context?.Document == null || string.IsNullOrWhiteSpace(handle))
                return 0;

            using (var lockDoc = context.Document.LockDocument())
            using (var tr = context.Document.Database.TransactionManager.StartTransaction())
            {
                int edgesDeleted, beamsDeleted;
                int deleted = DeleteInteriorGradeBeamFullInternal(context, tr, handle, out edgesDeleted, out beamsDeleted);

                tr.Commit();
                return deleted;
            }
        }

        /// <summary>
        /// Delete all grade beams in the NOD and their associated edges from AutoCAD drawing and the NOD tree.
        /// </summary>
        public int DeleteAllInteriorGradeBeams(FoundationContext context)
        {
            if (context?.Document == null)
                return 0;

            int totalBeamsDeleted = 0;

            using (var lockDoc = context.Document.LockDocument())
            using (var tr = context.Document.Database.TransactionManager.StartTransaction())
            {
                // Enumerate all grade beams from FD_GRADEBEAMS (read-only)
                foreach (var (handle, _) in GradeBeamInteriorNOD.EnumerateInteriorGradeBeams(context, tr))
                {
                    int edgesDeleted, beamsDeleted;
                    DeleteInteriorGradeBeamFullInternal(context, tr, handle, out edgesDeleted, out beamsDeleted);
                    totalBeamsDeleted += beamsDeleted;
                }

                tr.Commit();
            }

            return totalBeamsDeleted;
        }

        /// <summary>
        /// Fully deletes a grade beam: edges + centerline + NOD node.
        /// Upgrades objects to write only when required.
        /// </summary>
        private int DeleteInteriorGradeBeamFullInternal(
            FoundationContext context,
            Transaction tr,
            string handle,
            out int edgesDeleted,
            out int beamsDeleted)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (string.IsNullOrWhiteSpace(handle)) { edgesDeleted = 0; beamsDeleted = 0; return 0; }

            edgesDeleted = 0;
            beamsDeleted = 0;
            var db = context.Document.Database;

            // --- Delete edges first
            edgesDeleted = DeleteGradeBeamInteriorBeam_EdgesOnlyInternal(context, tr, handle);

            // --- Delete centerline (upgrade to write as needed)
            if (NODCore.TryGetObjectIdFromHandleString(tr, db, handle, out ObjectId clId) &&
                clId.IsValid && !clId.IsErased)
            {
                var entity = tr.GetObject(clId, OpenMode.ForWrite) as Entity;
                entity?.Erase();
                beamsDeleted++;
            }

            // --- Delete the NOD node (dictionary) recursively
            if (NODCore.TryGetGradeBeamInteriorBeamNode(tr, db, handle, out DBDictionary beamNode))
            {
                // Upgrade to write before erasing
                beamNode.UpgradeOpen();

                NODCore.EraseDictionaryRecursive(tr, db, beamNode, ref edgesDeleted, ref beamsDeleted, eraseEntities: true);

                // Remove from root dictionary
                if (NODCore.TryGetGradeBeamInteriorRoot(tr, db, out DBDictionary rootDict))
                {
                    rootDict.UpgradeOpen();
                    if (rootDict.Contains(handle))
                        rootDict.Remove(handle);
                }

                if (!beamNode.IsErased)
                    beamNode.Erase();
            }

            return edgesDeleted + beamsDeleted;
        }


        #endregion



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

        public bool HasAnyInteriorGradeBeams(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var doc = context.Document;
            if (doc == null) return false;

            var db = doc.Database;
            bool exists = false;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // --- Get the FD_GRADEBEAMS root dictionary
                if (NODCore.TryGetGradeBeamInteriorRoot(tr, db, out DBDictionary rootDict) && rootDict != null)
                {
                    // --- Check if there is at least one grade beam node
                    exists = rootDict.Count > 0;
                }

                // --- No commit needed; read-only
            }

            return exists;
        }


        public (int Quantity, double TotalLength) GetInteriorGradeBeamSummary(FoundationContext context)
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
                // Enumerate all grade beams (handle + dictionary)
                foreach (var (handle, gbDict) in GradeBeamInteriorNOD.EnumerateInteriorGradeBeams(context, tr))
                {
                    // --- Get the centerline ObjectId from the handle
                    if (!NODCore.TryGetObjectIdFromHandleString(tr, db, handle, out ObjectId clId))
                        continue;

                    if (!clId.IsValid || clId.IsErased)
                        continue;

                    var ent = tr.GetObject(clId, OpenMode.ForRead) as Entity;
                    if (ent == null)
                        continue;

                    quantity++;

                    // Compute length based on entity type
                    switch (ent)
                    {
                        case Line line:
                            totalLength += line.Length;
                            break;
                        case Polyline pl:
                            totalLength += MathHelperManager.ComputePolylineLengthInFeet(pl);
                            break;
                    }
                }

                // No need to commit; just reading
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
                // Enumerate all grade beams
                foreach (var (handle, gbDict) in GradeBeamInteriorNOD.EnumerateInteriorGradeBeams(context, tr))
                {
                    // Get the centerline ObjectId from the handle
                    if (!NODCore.TryGetObjectIdFromHandleString(tr, db, handle, out ObjectId clId))
                        continue;

                    if (clId.IsValid && !clId.IsErased)
                        ids.Add(clId);
                }

                // No need to commit; we are just reading
            }

            if (ids.Count == 0)
            {
                ed.WriteMessage("\nNo grade beams found.");
                return;
            }

            ed.WriteMessage($"\nHighlighting {ids.Count} grade beam centerlines...");

            // ----------------------------------------------------
            // STEP 1 - Use shared highlighting service
            // ----------------------------------------------------
            HighlightService.HighlightEntities(context, ids);

            // ----------------------------------------------------
            // STEP 2 – Focus and highlight in AutoCAD editor
            // ----------------------------------------------------
            SelectionService.FocusAndHighlight(context, ids, "HighlightGradeBeams");
        }





        #region Geometry Calculations (derived)
        /// <summary>
        /// Generates edge polylines for all grade beams, adds them to ModelSpace, 
        /// Deletes the edges of all grade beams in the drawing.
        /// and stores handles in the NOD.
        /// Returns the number of grade beams processed.
        /// </summary>
        public void GenerateEdgesForAllGradeBeams(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var db = doc.Database;

            CreateGradeBeamTrimmedEdges(context);

            doc.Editor.Regen();
        }



        /// <summary>
        /// Deletes a single grade beam node and all its subdictionaries/XRecords.
        /// Only affects the NOD structure; does NOT touch AutoCAD entities.
        /// </summary>
        /// <param name="context">Current foundation context</param>
        /// <param name="centerlineHandle">Handle string of the centerline for the grade beam to delete</param>
        /// <returns>True if deletion succeeded, false otherwise</returns>
        /// <summary>
        /// Deletes a single grade beam node and all its subdictionaries/XRecords.
        /// Also erases any AutoCAD entities referenced in the node (edges or centerline).
        /// </summary>
        /// <param name="context">Current foundation context</param>
        /// <param name="tr">Open transaction</param>
        /// <param name="centerlineHandle">Handle string of the centerline for the grade beam to delete</param>
        /// <returns>True if deletion succeeded, false otherwise</returns>
        internal bool DeleteGradeBeamNode(FoundationContext context, Transaction tr, string centerlineHandle)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (string.IsNullOrWhiteSpace(centerlineHandle)) throw new ArgumentNullException(nameof(centerlineHandle));

            var doc = context.Document;
            var db = doc.Database;

            try
            {
                // --- Get the grade beam node by handle
                if (!NODCore.TryGetGradeBeamInteriorBeamNode(tr, db, centerlineHandle, out DBDictionary beamNode))
                {
                    doc.Editor.WriteMessage($"\n[GradeBeamNOD] No grade beam node found for handle {centerlineHandle}.");
                    return false;
                }

                int edgesDeleted = 0;
                int beamsDeleted = 0;

                // --- Use EraseDictionaryRecursive to remove all subdictionaries, XRecords, and linked entities
                NODCore.EraseDictionaryRecursive(tr, db, beamNode, ref edgesDeleted, ref beamsDeleted);

                // --- Remove the node itself from the parent FD_GRADEBEAMS dictionary
                if (NODCore.TryGetGradeBeamInteriorRoot(tr, db, out var rootDict))
                {
                    rootDict.UpgradeOpen();
                    rootDict.Remove(centerlineHandle);
                }

                doc.Editor.WriteMessage($"\n[GradeBeamNOD] Deleted grade beam '{centerlineHandle}': {edgesDeleted} edges, {beamsDeleted} entities.");
                return true;
            }
            catch (Exception ex)
            {
                doc.Editor.WriteMessage($"\n[GradeBeamNOD] Failed to delete grade beam '{centerlineHandle}': {ex.Message}");
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
                    if (GradeBeamInteriorNOD.TryResolveOwningGradeBeam(context, tr, id, out string handle, out bool _, out bool _))
                        handles.Add(handle);
                }
            }
            return handles;
        }

        #endregion


        public void DrawGradeBeamLengthTable(FoundationContext context, Point3d? insertPoint = null, double scale = 1.0)
        {
            if (context == null) return;

            var doc = context.Document;
            var db = doc.Database;
            var ed = doc.Editor;

            Point3d pt = insertPoint ?? GradeBeamManager.DEFAULT_GRADEBEAMLENGTHTABLE_INSERT_PT;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // --- Delete any existing block references and block definition
                if (bt.Has("GRADEBEAMLENGTHTABLE"))
                {
                    var blockId = bt["GRADEBEAMLENGTHTABLE"];
                    var refsToErase = new List<ObjectId>();

                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased) continue;
                        if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference ent)) continue;

                        try
                        {
                            var def = tr.GetObject(ent.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                            if (def != null && def.Name.Equals("GRADEBEAMLENGTHTABLE", StringComparison.OrdinalIgnoreCase))
                                refsToErase.Add(id);
                        }
                        catch { /* ignore references that fail to resolve */ }
                    }

                    foreach (var refId in refsToErase)
                    {
                        if (!refId.IsErased)
                        {
                            var entWrite = tr.GetObject(refId, OpenMode.ForWrite) as Entity;
                            entWrite?.Erase();
                        }
                    }

                    // Erase the block definition itself
                    var blockDef = tr.GetObject(blockId, OpenMode.ForWrite) as BlockTableRecord;
                    if (blockDef != null && !blockDef.IsErased)
                        blockDef.Erase();
                }

                // --- Get grade beam centerlines
                var gradeBeamCenterlines = GetAllGradeBeamCenterlinePolylines(context);
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
                columnText[1].AddRange(Enumerable.Repeat("", gradeBeamCenterlines.Count));
                columnText[2].AddRange(gradeBeamCenterlines.Select(pl =>
                    Math.Ceiling(MathHelperManager.ComputePolylineLengthInFeet(pl)).ToString()));

                // --- Compute column widths
                double padding = 0.2 * scale;
                double[] colWidths = new double[colCount];
                for (int c = 0; c < colCount; c++)
                {
                    double maxWidth = scale * Math.Max(
                        MeasureTextWidth(colHeaders[c]),
                        columnText[c].Select(text => MeasureTextWidth(text)).DefaultIfEmpty(0).Max()
                    );
                    colWidths[c] = maxWidth + padding;
                }

                double rowHeight = 0.4 * scale;
                double tableWidth = colWidths.Sum();
                double tableHeight = rowCount * rowHeight;

                // --- Create new block definition
                BlockTableRecord newBlock = new BlockTableRecord { Name = "GRADEBEAMLENGTHTABLE" };
                ObjectId blockIdNew = bt.Add(newBlock);
                tr.AddNewlyCreatedDBObject(newBlock, true);

                // --- Draw outer rectangle
                Polyline outer = new Polyline(5);
                outer.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
                outer.AddVertexAt(1, new Point2d(tableWidth, 0), 0, 0, 0);
                outer.AddVertexAt(2, new Point2d(tableWidth, -tableHeight), 0, 0, 0);
                outer.AddVertexAt(3, new Point2d(0, -tableHeight), 0, 0, 0);
                outer.Closed = true;
                outer.LineWeight = LineWeight.LineWeight050;
                newBlock.AppendEntity(outer);
                tr.AddNewlyCreatedDBObject(outer, true);

                // --- Draw vertical lines
                double headerTopY = -rowHeight;
                double x = 0;
                for (int c = 0; c <= colCount; c++)
                {
                    Polyline vLine = new Polyline(2);
                    vLine.AddVertexAt(0, new Point2d(x, headerTopY), 0, 0, 0);
                    vLine.AddVertexAt(1, new Point2d(x, -tableHeight), 0, 0, 0);
                    vLine.LineWeight = (c == 0 || c == colCount) ? LineWeight.LineWeight050 : LineWeight.LineWeight015;
                    newBlock.AppendEntity(vLine);
                    tr.AddNewlyCreatedDBObject(vLine, true);
                    if (c < colCount) x += colWidths[c];
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
                InsertMText(newBlock, tr, new Point3d(tableWidth / 2, -rowHeight / 2, 0),
                    "GRADE BEAM LENGTHS", tableWidth, rowHeight, CellAlignment.MiddleCenter, scale);

                // --- Insert headers
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

                // --- TOTAL row
                double totalLengthFeet = Math.Ceiling(gradeBeamCenterlines.Sum(pl => MathHelperManager.ComputePolylineLengthInFeet(pl)));
                double totalRowY = -rowHeight * (gradeBeamCenterlines.Count + 2.5);
                InsertMText(newBlock, tr,
                    new Point3d((colWidths[0] + colWidths[1]) / 2, totalRowY, 0),
                    "TOTAL", colWidths[0] + colWidths[1], rowHeight, CellAlignment.MiddleCenter, scale);
                InsertMText(newBlock, tr,
                    new Point3d(colWidths[0] + colWidths[1] + colWidths[2] / 2, totalRowY, 0),
                    totalLengthFeet.ToString() + " ft.", colWidths[2], rowHeight, CellAlignment.MiddleRight, scale);

                // --- Insert block reference in ModelSpace
                BlockReference blockRef = new BlockReference(pt, blockIdNew);
                ms.AppendEntity(blockRef);
                tr.AddNewlyCreatedDBObject(blockRef, true);

                tr.Commit();
                doc.Editor.Regen();
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



        public List<Polyline> GetAllGradeBeamCenterlinePolylines(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var result = new List<Polyline>();
            var doc = context.Document;
            var db = doc.Database;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // --- Enumerate all grade beams via NODCore
                foreach (var (handle, _) in GradeBeamInteriorNOD.EnumerateInteriorGradeBeams(context, tr))
                {
                    if (string.IsNullOrWhiteSpace(handle))
                        continue;

                    // --- Get the centerline ObjectId from the handle
                    if (NODCore.TryGetObjectIdFromHandleString(tr, db, handle, out ObjectId clId) &&
                        clId.IsValid && !clId.IsErased)
                    {
                        var pl = tr.GetObject(clId, OpenMode.ForRead) as Polyline;
                        if (pl != null)
                            result.Add(pl);
                    }
                }

                // No need to commit; reading only
            }

            return result;
        }

        public int GetGradeBeamCount(FoundationContext context)
        {
            if (context == null || context.Document == null)
                return 0;

            var doc = context.Document;
            int count = 0;

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                count = NODCore.CountInteriorGradeBeams(tr, doc.Database);
                tr.Commit();
            }

            return count;
        }

        /// <summary>
        /// Returns the total length of all grade beam polylines stored in FD_GRADEBEAM_INTERIOR.
        /// </summary>
        /// <param name="context">The current foundation context</param>
        /// <returns>Total length of all polylines (0 if none)</returns>
        public double GetTotalGradeBeamLength(FoundationContext context)
        {
            if (context == null || context.Document == null)
                return 0.0;

            var doc = context.Document;
            double totalLength = 0.0;

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var db = doc.Database;
                var gradeBeamRoot = NODCore.GetOrCreateGradeBeamInteriorRootDictionary(tr, db, forWrite: false);
                if (gradeBeamRoot == null || gradeBeamRoot.Count == 0)
                {
                    tr.Commit();
                    return 0.0;
                }

                foreach (DictionaryEntry entry in gradeBeamRoot)
                {
                    if (!(entry.Key is string handleStr) || string.IsNullOrWhiteSpace(handleStr))
                        continue;

                    if (!NODCore.TryGetObjectIdFromHandleString(tr, db, handleStr, out ObjectId plId))
                        continue;

                    var pl = tr.GetObject(plId, OpenMode.ForRead) as Polyline;
                    if (pl != null && !pl.IsErased)
                        totalLength += pl.Length;
                }

                tr.Commit();
            }

            return totalLength;
        }

        /// <summary>
        /// Creates and trims the edges for the gradebeam and draws them in model space.
        /// Appends edge trimmed objects to the NOD
        /// </summary>
        /// <param name="context"></param>
        internal static void CreateGradeBeamTrimmedEdges(FoundationContext context)
        {
            if (context == null) return;

            var doc = context.Document;
            var db = doc.Database;

            // --- Lock and create new transaction
            using (doc.LockDocument())
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    DeleteEdgesForAllInteriorGradeBeams(context);

                    // --- Enumerate all grade beams
                    var beams = GradeBeamInteriorNOD.EnumerateInteriorGradeBeams(context, tr).ToList();
                    if (!beams.Any()) return;

                    // --- Delete all existing edges
                    foreach (var (_, gbDict) in beams)
                        GradeBeamInteriorNOD.DeleteBeamEdgesOnly(context, tr, gbDict);

                    tr.Commit();
                }

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // --- Enumerate all grade beams
                    var beams = GradeBeamInteriorNOD.EnumerateInteriorGradeBeams(context, tr).ToList();
                    if (!beams.Any()) return;

                    // --- Cache beam widths
                    var beamWidths = beams.ToDictionary(
                        b => b.Handle,
                        b => GradeBeamInteriorNOD.GetBeamSection(tr, b.Dict).Width
                    );

                    // --- Build footprints
                    var footprints = new Dictionary<ObjectId, Polyline>();
                    foreach (var (handle, gbDict) in beams)
                    {
                        if (!NODCore.TryGetObjectIdFromHandleString(tr, db, handle, out var clId))
                            continue;

                        var cl = tr.GetObject(clId, OpenMode.ForRead) as Polyline;
                        if (cl == null) continue;

                        footprints[clId] = GradeBeamBuilder.BuildFootprint(cl, beamWidths[handle]);
                    }

                    // --- Generate left/right edge polylines
                    var allEdges = new List<GradeBeamBuilder.BeamEdgeSegment>();
                    foreach (var (handle, gbDict) in beams)
                    {
                        if (!NODCore.TryGetObjectIdFromHandleString(tr, db, handle, out var clId))
                            continue;

                        var cl = tr.GetObject(clId, OpenMode.ForRead) as Polyline;
                        if (cl == null) continue;

                        double width = beamWidths[handle];

                        var leftOffset = GradeBeamBuilder.OffsetPolyline(cl, +width);
                        var rightOffset = GradeBeamBuilder.OffsetPolyline(cl, -width);

                        allEdges.AddRange(GradeBeamBuilder.ExplodeEdges(clId, leftOffset, true));
                        allEdges.AddRange(GradeBeamBuilder.ExplodeEdges(clId, rightOffset, false));
                    }

                    // --- Trim edges using footprints
                    var trimmedEdges = GradeBeamBuilder.TrimAllEdges(allEdges, footprints);

                    // --- Store edges and draw in modelspace
                    foreach (var group in trimmedEdges.GroupBy(e => e.BeamId))
                    {
                        var leftSegs = group.Where(e => e.IsLeft).ToList();
                        var rightSegs = group.Where(e => !e.IsLeft).ToList();

                        ObjectId[] leftIds = leftSegs.Select(seg =>
                        {
                            var ln = new Line(seg.Segment.StartPoint, seg.Segment.EndPoint) { ColorIndex = 1 };
                            ms.AppendEntity(ln);
                            tr.AddNewlyCreatedDBObject(ln, true);
                            return ln.ObjectId;
                        }).ToArray();

                        ObjectId[] rightIds = rightSegs.Select(seg =>
                        {
                            var ln = new Line(seg.Segment.StartPoint, seg.Segment.EndPoint) { ColorIndex = 5 };
                            ms.AppendEntity(ln);
                            tr.AddNewlyCreatedDBObject(ln, true);
                            return ln.ObjectId;
                        }).ToArray();

                        string handle = group.Key.Handle.ToString();

                        GradeBeamInteriorNOD.StoreEdgeObjects(context, tr, db, handle, leftIds, rightIds);
                    }

                    doc.Editor.Regen();
                    tr.Commit();
                }
            }
        }

    }
}


