using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using FoundationDetailsLibraryAutoCAD.AutoCAD.NOD;
using FoundationDetailsLibraryAutoCAD.Data;
using FoundationDetailsLibraryAutoCAD.Services;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace FoundationDetailer.Managers
{
    /// <summary>
    /// Robust manager for a single polyline "boundary" per Document.
    /// Tracks modifications, erases, undo/redo, append/unappend, and command-based edits.
    /// Adds hybrid handle-change / replacement detection and automatic adoption of replacement polylines.
    /// </summary>
    public class PolylineBoundaryManager
    {
        private const double DEFAULT_WIDTH = 12.0;
        private const double DEFAULT_DEPTH = 40.0;

        // Document -> stored ObjectId (ObjectId.Null if none)
        private static readonly ConcurrentDictionary<Document, ObjectId> _docBoundaryIds = new ConcurrentDictionary<Document, ObjectId>();

        // Document -> last captured geometry snapshot (used to detect replacements)
        private static readonly ConcurrentDictionary<Document, GeometrySnapshot> _lastSnapshots = new ConcurrentDictionary<Document, GeometrySnapshot>();

        // Document -> command-scoped state (appended/erased objects observed during a command)
        private static readonly ConcurrentDictionary<Document, CommandState> _docCommandStates = new ConcurrentDictionary<Document, CommandState>();

        // Per-document deferred idle handlers
        private static readonly ConcurrentDictionary<Document, EventHandler> _deferredIdleHandlers = new ConcurrentDictionary<Document, EventHandler>();

        // Commands worth watching (kept uppercase)
        private static readonly HashSet<string> _monitoredCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "MOVE", "STRETCH", "GRIP_STRETCH", "PEDIT", "EXPLODE", "GRIPS", "GRIP_MOVE",
            "ROTATE", "SCALE", "MIRROR", "ERASE", "DELETE", /*"U", "UNDO", "REDO",*/ "JOIN"
        };

        // Commands that often *replace* geometry (we'll attempt replacement detection after these)
        private static readonly HashSet<string> _replacementCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PEDIT", "EXPLODE", "JOIN"
        };

        public static event EventHandler BoundaryChanged;

        /// <summary>
        /// Raises the BoundaryChanged event to notify subscribers.
        /// </summary>
        public static void RaiseBoundaryChanged()
        {
            BoundaryChanged?.Invoke(null, EventArgs.Empty);
        }

        #region Public API (original functionality preserved)

        public void Initialize(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            // Attach to existing documents
            AttachDocumentEvents(context);
        }

        private void AttachDocumentEvents(FoundationContext context)
        {
            // add document events here
        }


        /// <summary>
        /// Primary function that turns an AutoCAD polyline object into a boundary beam centerline for this application
        /// Creates the NOD entry for the specified boundary beam.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="pl"></param>
        /// <param name="tr"></param>
        /// <param name="appendToModelSpace"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        internal Polyline RegisterGradeBeamPerimeterBeam(
            FoundationContext context,
            Polyline pl,
            Transaction tr,
            bool appendToModelSpace = false)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (pl == null) throw new ArgumentNullException(nameof(pl));
            if (tr == null) throw new ArgumentNullException(nameof(tr));

            var db = context.Document.Database;

            // --- Append to ModelSpace if requested and not already in DB ---
            if (appendToModelSpace && pl.ObjectId.IsNull)
            {
                ModelSpaceWriterService.AppendToModelSpace(tr, db, pl);
            }


            // --- Open FD_GRADEBEAM_PERIMETER root dictionary (always for write) ---
            var gradebeamPerimeterRootDict = NODCore.GetOrCreateGradeBeamPerimeterRootDictionary(tr, db, forWrite: true);
            if (gradebeamPerimeterRootDict == null)
                throw new InvalidOperationException("Unable to open " + NODCore.KEY_GRADEBEAM_PERIMETER_SUBDICT + " dictionary.");

            // --- Delete existing boundary grade beam node, if any ---
            if (gradebeamPerimeterRootDict.Count > 0)
            {
                // FD_BOUNDARY should only have one entry, get its key
                string existingHandle = null;
                foreach (DictionaryEntry de in gradebeamPerimeterRootDict)
                {
                    existingHandle = de.Key as string;
                    break; // only the first
                }

                if (!string.IsNullOrWhiteSpace(existingHandle))
                {
                    DBDictionary existingNode;
                    if (NODCore.TryGetGradeBeamPerimeterBeamNode(tr, db, out existingNode))
                    {
                        int edgesDeleted = 0, beamsDeleted = 0;
                        NODCore.EraseDictionaryRecursive(tr, db, existingNode, ref edgesDeleted, ref beamsDeleted);

                        // Remove entry from FD_BOUNDARY
                        if (!gradebeamPerimeterRootDict.IsWriteEnabled)
                            gradebeamPerimeterRootDict.UpgradeOpen();

                        gradebeamPerimeterRootDict.Remove(existingHandle);
                    }
                }
            }

            // --- Register the new boundary under its centerline handle ---
            string newHandle = pl.Handle.ToString();
            NODCore.GetOrCreateBoundaryGradeBeamNode(tr, db, newHandle);

            return pl;
        }



        /// <summary>
        /// Deletes the edges for a single grade beam or boundary beam.
        /// Keeps the NOD structure and centerline object.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="handle"></param>
        /// <returns>Number of deleted edge entities.</returns>
        public int DeleteEdgesForSingleBeam(FoundationContext context, string handle)
        {
            if (context?.Document == null || string.IsNullOrWhiteSpace(handle))
                return 0;

            using (var lockDoc = context.Document.LockDocument())
            using (var tr = context.Document.Database.TransactionManager.StartTransaction())
            {
                int deleted = DeleteGradeBeamPerimeterBeam_EdgesOnlyInternal(context, tr);
                tr.Commit();
                return deleted;
            }
        }

        /// <summary>
        /// Deletes all edge entities of a single grade/boundary beam but keeps centerline and NOD dictionary.
        /// this functoion deletes the perimeter gradebeam edges
        /// Returns the number of entities erased.
        /// </summary>
        internal int DeleteGradeBeamPerimeterBeam_EdgesOnlyInternal(FoundationContext context, Transaction tr)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));

            int deleted = 0;
            var db = context.Document.Database;

            // --- Get the beam node first (boundary or gradebeam)
            DBDictionary beamNode;
            if (!NODCore.TryGetGradeBeamPerimeterBeamNode(tr, db, out beamNode))
                return 0;

            // --- Get the EDGES subdictionary if it exists
            DBDictionary edgesDict;
            if (!NODCore.TryGetBeamEdges(tr, beamNode, out edgesDict))
                return 0;

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

                        if (!NODCore.TryGetObjectIdFromHandleString(tr, db, handleStr, out ObjectId oid))
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
        /// Deletes all edges and the centerline entity for a single boundary beam handle.
        /// Uses NODCore helpers to find the node and recursively erase it.
        /// </summary>
        private void DeleteBoundaryBeamFullInternal(
            FoundationContext context,
            Transaction tr,
            string handle,
            ref int edgesDeleted,
            ref int beamsDeleted)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (string.IsNullOrWhiteSpace(handle)) throw new ArgumentNullException(nameof(handle));

            var db = context.Document.Database;

            // --- Get the FD_BOUNDARY beam node
            if (!NODCore.TryGetGradeBeamPerimeterBeamNode(tr, db, out DBDictionary beamNode))
                return;

            // --- Recursively erase everything under the node
            NODCore.EraseDictionaryRecursive(tr, db, beamNode, ref edgesDeleted, ref beamsDeleted);

            // --- Delete the centerline AutoCAD entity if it exists
            if (NODCore.TryGetObjectIdFromHandleString(tr, db, handle, out ObjectId clId))
            {
                if (clId.IsValid && !clId.IsErased)
                {
                    (tr.GetObject(clId, OpenMode.ForWrite) as Entity)?.Erase();
                    beamsDeleted++;
                }
            }

            // --- Remove the node itself from FD_BOUNDARY
            var rootDict = NODCore.GetOrCreateBoundaryRootDictionary(tr, db, forWrite: true);
            if (rootDict != null)
            {
                if (!rootDict.IsWriteEnabled)
                    rootDict.UpgradeOpen();

                if (!beamNode.IsWriteEnabled)
                    beamNode.UpgradeOpen();

                rootDict.Remove(handle);
                beamNode.Erase();
            }
        }

        /// <summary>
        /// Deletes the first (and only) boundary beam in FD_BOUNDARY if one exists.
        /// Returns total number of entities erased.
        /// </summary>
        public int DeleteBoundaryBeam(FoundationContext context)
        {
            if (context?.Document == null)
                return 0;

            using (var lockDoc = context.Document.LockDocument())
            using (var tr = context.Document.Database.TransactionManager.StartTransaction())
            {
                var db = context.Document.Database;
                var rootDict = NODCore.GetOrCreateBoundaryRootDictionary(tr, db, forWrite: true);
                if (rootDict == null || rootDict.Count == 0)
                    return 0;

                // --- Get first boundary beam handle
                string handle = null;
                foreach (DictionaryEntry de in rootDict)
                {
                    handle = de.Key as string;
                    if (!string.IsNullOrWhiteSpace(handle))
                        break;
                }

                if (!string.IsNullOrWhiteSpace(handle))
                    DeleteBoundaryBeamNodeOnly(context, tr, handle);

                tr.Commit();
                return 1; // node removed
            }
        }

        /// <summary>
        /// Deletes the FD_BOUNDARY node for the given handle, but keeps the AutoCAD centerline entity.
        /// </summary>
        /// <summary>
        /// Deletes the FD_BOUNDARY node for the given handle, but keeps the AutoCAD centerline entity.
        /// </summary>
        private void DeleteBoundaryBeamNodeOnly(
            FoundationContext context,
            Transaction tr,
            string handle)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (string.IsNullOrWhiteSpace(handle)) throw new ArgumentNullException(nameof(handle));

            // --- Get FD_BOUNDARY root dictionary
            var db = context.Document.Database;
            var rootDict = NODCore.GetOrCreateBoundaryRootDictionary(tr, db, forWrite: true);
            if (rootDict == null || !rootDict.Contains(handle))
                return;

            // --- Get the beam node dictionary
            DBDictionary beamNode;
            if (!NODCore.TryGetGradeBeamPerimeterBeamNode(tr, db, out beamNode))
                return;

            // --- Recursively erase subdictionaries, but do NOT delete AutoCAD entities
            int edgesDeleted = 0;
            int beamsDeleted = 0;
            NODCore.EraseDictionaryRecursive(tr, db, beamNode, ref edgesDeleted, ref beamsDeleted, eraseEntities: false);

            // --- Remove the node itself from FD_BOUNDARY
            if (!rootDict.IsWriteEnabled)
                rootDict.UpgradeOpen();
            rootDict.Remove(handle);

            if (!beamNode.IsErased)
                beamNode.Erase();
        }


        /// <summary>
        /// Highlight the centerline of the first (or only) boundary beam in the current document.
        /// </summary>
        public void HighlightBoundary(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var doc = context.Document;
            if (doc == null) return;

            var db = doc.Database;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                // --- Get the FD_BOUNDARY root dictionary
                var rootDict = NODCore.GetOrCreateBoundaryRootDictionary(tr, db);
                if (rootDict == null || rootDict.Count == 0)
                    return;

                // --- Get the first (and only) boundary beam handle
                string handle = null;
                foreach (DictionaryEntry de in rootDict)
                {
                    handle = de.Key as string;
                    if (!string.IsNullOrWhiteSpace(handle))
                        break;
                }

                if (string.IsNullOrWhiteSpace(handle))
                    return;

                // --- Use NODCore to get the centerline ObjectId
                if (NODCore.TryGetObjectIdFromHandleString(tr, db, handle, out ObjectId clId)
                    && clId.IsValid && !clId.IsErased)
                {
                    SelectionService.FocusAndHighlight(context, new[] { clId }, "HighlightGradeBeam");
                }

                tr.Commit();
            }
        }


        /// <summary>
        /// Returns the upper-right corner of the boundary beam's centerline.
        /// </summary>
        public Point3d? GetBoundaryUpperRight(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var doc = context.Document;
            if (doc == null) return null;

            var db = doc.Database;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                // --- Get the FD_BOUNDARY root dictionary
                var rootDict = NODCore.GetOrCreateBoundaryRootDictionary(tr, db);
                if (rootDict == null || rootDict.Count == 0)
                    return null;

                // --- Get the first (and only) boundary beam handle
                string handle = null;
                foreach (DictionaryEntry de in rootDict)
                {
                    handle = de.Key as string;
                    if (!string.IsNullOrWhiteSpace(handle))
                        break;
                }

                if (string.IsNullOrWhiteSpace(handle))
                    return null;

                // --- Get centerline ObjectId
                if (NODCore.TryGetObjectIdFromHandleString(tr, db, handle, out ObjectId clId)
                    && clId.IsValid && !clId.IsErased)
                {
                    var pl = tr.GetObject(clId, OpenMode.ForRead) as Polyline;
                    if (pl != null)
                    {
                        try
                        {
                            return pl.GeometricExtents.MaxPoint;
                        }
                        catch
                        {
                            return null;
                        }
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Zooms the editor view to the boundary beam's centerline.
        /// </summary>
        public void ZoomToBoundary(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var doc = context.Document;
            if (doc == null) return;

            var db = doc.Database;
            var ed = doc.Editor;

            using (var tr = db.TransactionManager.StartTransaction())
            using (doc.LockDocument())
            {
                // --- Get the FD_BOUNDARY root dictionary
                var rootDict = NODCore.GetOrCreateBoundaryRootDictionary(tr, db);
                if (rootDict == null || rootDict.Count == 0)
                    return;

                // --- Get the first (and only) boundary beam handle
                string handle = null;
                foreach (DictionaryEntry de in rootDict)
                {
                    handle = de.Key as string;
                    if (!string.IsNullOrWhiteSpace(handle))
                        break;
                }

                if (string.IsNullOrWhiteSpace(handle))
                    return;

                // --- Get centerline ObjectId
                if (NODCore.TryGetObjectIdFromHandleString(tr, db, handle, out ObjectId clId) &&
                    clId.IsValid && !clId.IsErased)
                {
                    var pl = tr.GetObject(clId, OpenMode.ForRead) as Polyline;
                    if (pl == null) return;

                    try
                    {
                        ed.SetImpliedSelection(new ObjectId[] { pl.ObjectId });
                        Extents3d ext = pl.GeometricExtents;
                        if (ext.MinPoint.DistanceTo(ext.MaxPoint) < 1e-6) return;

                        var view = ed.GetCurrentView();
                        Point2d center = new Point2d(
                            (ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                            (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0
                        );

                        double width = ext.MaxPoint.X - ext.MinPoint.X;
                        double height = ext.MaxPoint.Y - ext.MinPoint.Y;
                        if (width < 1e-6) width = 1.0;
                        if (height < 1e-6) height = 1.0;

                        view.CenterPoint = center;
                        view.Width = width * 1.1;
                        view.Height = height * 1.1;

                        ed.SetCurrentView(view);
                    }
                    catch { }
                }

                tr.Commit();
            }
        }
        #endregion

        #region Geometry Snapshot Helper (lightweight similarity checks)

        /// <summary>
        /// Lightweight geometry snapshot extracted from a polyline: vertex count, centroid, polygon area, and extents size.
        /// </summary>
        private class GeometrySnapshot
        {
            public int VertexCount { get; private set; }
            public Point2d Centroid { get; private set; }
            public double Area { get; private set; }           // absolute area
            public double ExtentsWidth { get; private set; }
            public double ExtentsHeight { get; private set; }

            public static GeometrySnapshot FromPolyline(Polyline pl)
            {
                var snap = new GeometrySnapshot
                {
                    VertexCount = pl.NumberOfVertices
                };

                // centroid & area via shoelace (2D)
                var pts = new List<Point2d>(pl.NumberOfVertices);
                for (int i = 0; i < pl.NumberOfVertices; i++)
                {
                    pts.Add(pl.GetPoint2dAt(i));
                }

                // Calculate area and centroid
                double area = 0;
                double cx = 0, cy = 0;
                for (int i = 0; i < pts.Count; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % pts.Count];
                    double cross = a.X * b.Y - b.X * a.Y;
                    area += cross;
                    cx += (a.X + b.X) * cross;
                    cy += (a.Y + b.Y) * cross;
                }

                area *= 0.5;
                double absArea = Math.Abs(area);
                snap.Area = absArea;

                if (Math.Abs(area) > 1e-9)
                {
                    cx /= (6.0 * area);
                    cy /= (6.0 * area);
                    snap.Centroid = new Point2d(cx, cy);
                }
                else
                {
                    // fallback centroid = average points
                    double avgX = pts.Average(p => p.X);
                    double avgY = pts.Average(p => p.Y);
                    snap.Centroid = new Point2d(avgX, avgY);
                }

                var ext = pl.GeometricExtents;
                snap.ExtentsWidth = Math.Abs(ext.MaxPoint.X - ext.MinPoint.X);
                snap.ExtentsHeight = Math.Abs(ext.MaxPoint.Y - ext.MinPoint.Y);

                return snap;
            }

            /// <summary>
            /// Compare a candidate polyline to the snapshot with tolerances.
            /// </summary>
            public static bool IsSimilar(Polyline candidate, GeometrySnapshot snap)
            {
                if (candidate == null || snap == null) return false;

                // Vertex count must be reasonably close (exact is best)
                if (Math.Abs(candidate.NumberOfVertices - snap.VertexCount) > 2)
                    return false;

                // Compute candidate area and centroid quickly
                var pts = new List<Point2d>(candidate.NumberOfVertices);
                for (int i = 0; i < candidate.NumberOfVertices; i++)
                    pts.Add(candidate.GetPoint2dAt(i));

                double area = 0;
                double cx = 0, cy = 0;
                for (int i = 0; i < pts.Count; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % pts.Count];
                    double cross = a.X * b.Y - b.X * a.Y;
                    area += cross;
                    cx += (a.X + b.X) * cross;
                    cy += (a.Y + b.Y) * cross;
                }

                area *= 0.5;
                double absArea = Math.Abs(area);

                // Tolerance rules:
                // - area within 2% or absolute 1.0 unit (whichever larger)
                double areaTol = Math.Max(Math.Abs(snap.Area) * 0.02, 1.0);
                if (Math.Abs(absArea - snap.Area) > areaTol) return false;

                // centroid within reasonable tolerance (based on extents)
                Point2d candCentroid;
                if (Math.Abs(area) > 1e-9)
                {
                    cx /= (6.0 * area);
                    cy /= (6.0 * area);
                    candCentroid = new Point2d(cx, cy);
                }
                else
                {
                    candCentroid = new Point2d(pts.Average(p => p.X), pts.Average(p => p.Y));
                }

                double dist = Math.Sqrt(
                    Math.Pow(candCentroid.X - snap.Centroid.X, 2) +
                    Math.Pow(candCentroid.Y - snap.Centroid.Y, 2)
                );

                double centTol = Math.Max(snap.ExtentsWidth, snap.ExtentsHeight) * 0.1 + 0.5; // 10% of size + 0.5 units
                if (dist > centTol) return false;

                // extents width/height should be similar within 10-12%
                var ext = candidate.GeometricExtents;
                double w = Math.Abs(ext.MaxPoint.X - ext.MinPoint.X);
                double h = Math.Abs(ext.MaxPoint.Y - ext.MinPoint.Y);
                if (Math.Abs(w - snap.ExtentsWidth) > Math.Max(snap.ExtentsWidth * 0.12, 0.5)) return false;
                if (Math.Abs(h - snap.ExtentsHeight) > Math.Max(snap.ExtentsHeight * 0.12, 0.5)) return false;

                return true;
            }
        }

        #endregion

        #region Nested helpers & small utility types

        // Per-document command-scoped tracking
        private class CommandState
        {
            public string CurrentCommand { get; set; } = string.Empty;
            public HashSet<ObjectId> AppendedIds { get; } = new HashSet<ObjectId>();
            public HashSet<ObjectId> ErasedIds { get; } = new HashSet<ObjectId>();
            public void Clear()
            {
                CurrentCommand = string.Empty;
                AppendedIds.Clear();
                ErasedIds.Clear();
            }
        }

        // Simple axis-aligned rectangle helper for extents overlap checks
        private class Rectangle
        {
            public Point3d Min { get; }
            public Point3d Max { get; }

            public Rectangle(Point3d min, Point3d max)
            {
                Min = min; Max = max;
            }

            public bool Overlaps(Rectangle other)
            {
                if (other == null) return false;
                if (Max.X < other.Min.X || other.Max.X < Min.X) return false;
                if (Max.Y < other.Min.Y || other.Max.Y < Min.Y) return false;
                return true;
            }
        }

        #endregion

        /// <summary>
        /// The primary function to select a polyline to be added as the boundary beam
        /// </summary>
        /// <param name="context"></param>
        /// <param name="result"></param>
        /// <param name="status"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public bool DefineBoundary(FoundationContext context, PromptEntityResult result, out string status)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;

            status = null;

            if (doc == null) return false;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                using (doc.LockDocument())
                // Start transaction
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    // Open the selected object for read
                    Polyline boundary = tr.GetObject(result.ObjectId, OpenMode.ForRead) as Polyline;

                    if (boundary == null)
                    {
                        status = "Selected object is not a polyline.";
                        return false;
                    }

                    if (!boundary.Closed)
                    {
                        boundary.UpgradeOpen();
                        status = "Polyline is not closed.  Creating closed polyline.";
                        boundary.Closed = true;
                    }

                    // ✅ You now have a real AutoCAD Polyline object
                    double area = boundary.Area;
                    int vertexCount = boundary.NumberOfVertices;

                    status = $"Polyline selected. Area = {area}";

                    // Register the beam object into the NOD tree.
                    RegisterGradeBeamPerimeterBeam(context, boundary, tr, appendToModelSpace: true);

                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nBoundary selection failed: {ex.Message}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Attempts to retrieve the boundary polyline for the current document.
        /// Opens a transaction internally and reads the FD_BOUNDARY dictionary.
        /// </summary>
        /// <param name="context">Foundation context</param>
        /// <param name="boundary">Outputs the boundary polyline if found</param>
        /// <returns>True if a valid boundary polyline was retrieved, false otherwise</returns>
        /// <summary>
        /// Attempts to retrieve the boundary polyline for the current document.
        /// </summary>
        /// <param name="context">Foundation context</param>
        /// <param name="boundary">Outputs the boundary polyline if found</param>
        /// <returns>True if a valid boundary polyline was retrieved, false otherwise</returns>
        public bool TryGetBoundary(FoundationContext context, out Polyline boundary)
        {
            boundary = null;
            if (context == null) throw new ArgumentNullException(nameof(context));
            var doc = context.Document;
            if (doc == null) return false;

            var db = doc.Database;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    // --- Get the FD_BOUNDARY root dictionary
                    DBDictionary boundaryRoot;
                    if (!NODCore.TryGetBoundaryRoot(tr, db, out boundaryRoot))
                        return false;

                    if (boundaryRoot.Count == 0)
                        return false;

                    // --- The first key of the root dictionary is the polyline handle
                    string handleStr = null;
                    foreach (DictionaryEntry entry in boundaryRoot)
                    {
                        handleStr = entry.Key as string;
                        if (!string.IsNullOrWhiteSpace(handleStr))
                            break;
                    }

                    if (string.IsNullOrWhiteSpace(handleStr))
                        return false;

                    // --- Convert handle string to ObjectId
                    ObjectId plId;
                    if (!NODCore.TryGetObjectIdFromHandleString(tr, db, handleStr, out plId))
                        return false;

                    // --- Open the polyline
                    var pl = tr.GetObject(plId, OpenMode.ForRead) as Polyline;
                    if (pl == null || pl.IsErased)
                        return false;

                    boundary = pl;
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// helper function to retrieve the section dimensions of the boundary beam
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public (double, double) GetBoundaryBeamDimensions(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var doc = context.Document;
            if (doc == null) throw new ArgumentNullException(nameof(context));

            var db = doc.Database;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                double width = DEFAULT_WIDTH;
                double depth = DEFAULT_DEPTH;

                foreach (var (handle, _) in BoundaryNOD.EnumerateBoundaryBeam(context, tr))
                {
                    var boundaryNodeDict = NODCore.GetOrCreateBoundaryGradeBeamNode(tr, db, handle);

                    foreach (DictionaryEntry entry in boundaryNodeDict)
                    {
                        System.Diagnostics.Debug.WriteLine($"Key: {entry.Key}");
                    }

                    DBDictionary metaDict;
                    if (NODCore.TryGetGradeBeamMeta(tr, boundaryNodeDict, out metaDict))
                    {
                        if (NODCore.TryGetGradeBeamSectionFromMetaDict(tr, boundaryNodeDict, out var sectionDict))
                        {
                            width = NODCore.GetXRecordValue(tr, sectionDict, NODCore.KEY_SECTION_WIDTH.ToString()) ?? DEFAULT_WIDTH;
                            depth = NODCore.GetXRecordValue(tr, sectionDict, NODCore.KEY_SECTION_DEPTH.ToString()) ?? DEFAULT_DEPTH;
                        }
                    }

                    // assume only one boundary → stop early
                    break;
                }

                tr.Commit();

                return (width, depth);
            }
        }

        /// <summary>
        /// Sets the boundary beam dimensions, ensuring valid non-null values
        /// </summary>
        /// <param name="context"></param>
        /// <param name="width">Width to set (must be > 0)</param>
        /// <param name="depth">Depth to set (must be > 0)</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public void SetBoundaryBeamDimensions(FoundationContext context, double width, double depth)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var doc = context.Document;
            if (doc == null) throw new ArgumentNullException(nameof(context));

            // ensure width/depth are valid numbers
            if (double.IsNaN(width) || width <= 0) width = DEFAULT_WIDTH;
            if (double.IsNaN(depth) || depth <= 0) depth = DEFAULT_DEPTH;

            var db = doc.Database;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var (handle, _) in BoundaryNOD.EnumerateBoundaryBeam(context, tr))
                {
                    var boundaryNodeDict = NODCore.GetOrCreateBoundaryGradeBeamNode(tr, db, handle);

                    // get or create section dictionary
                    DBDictionary metaDict;
                    if(NODCore.TryGetGradeBeamMeta(tr, boundaryNodeDict, out metaDict))
                    {
                        if (NODCore.TryGetGradeBeamSectionFromMetaDict(tr, boundaryNodeDict, out var sectionDict))
                        {
                            // set values safely
                            NODCore.SetXRecordValue(tr, sectionDict, NODCore.KEY_SECTION_WIDTH.ToString(), width);
                            NODCore.SetXRecordValue(tr, sectionDict, NODCore.KEY_SECTION_DEPTH.ToString(), depth);
                        }
                        else
                        {
                            doc.Editor.WriteMessage("Unable to find SECTION subdictionary for boundary beam");
                        }
                    } 


                    // only one boundary assumed
                    break;
                }

                tr.Commit();
            }
        }
    }
}
