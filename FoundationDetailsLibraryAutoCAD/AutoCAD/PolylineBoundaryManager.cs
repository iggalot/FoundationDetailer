using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;

namespace FoundationDetailer.Utilities
{
    /// <summary>
    /// Manages a single closed polyline boundary per document, including palette-safe storage and Xrecord persistence.
    /// </summary>
    public static class PolylineBoundaryManager
    {
        private static readonly Dictionary<Document, ObjectId> _storedBoundaries = new Dictionary<Document, ObjectId>();
        private static ObjectId _storedBoundaryId = ObjectId.Null;

        public const string XrecordKey = "FD_BOUNDARY";

        private static readonly string[] MonitoredCommands =
        {
            "MOVE", "STRETCH", "GRIP_STRETCH", "PEDIT", "EXPLODE", "GRIPS", "GRIP_MOVE"
        };

        public static event EventHandler BoundaryChanged;

        #region --- Palette-safe API ---

        /// <summary>
        /// Sets the active boundary (palette-safe). Queues Xrecord write to persist.
        /// </summary>
        public static bool TrySetBoundary(ObjectId id, out string error)
        {
            error = "";
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) { error = "No active document."; return false; }

            try
            {
                var db = doc.Database;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var pl = tr.GetObject(id, OpenMode.ForRead) as Polyline;
                    if (pl == null) { error = "Selected object is not a polyline."; return false; }
                    if (!pl.Closed) { error = "Polyline must be closed."; return false; }

                    Polyline cleaned = FixBoundary(pl);

                    // Update in-memory storage
                    _storedBoundaryId = cleaned.ObjectId;
                    _storedBoundaries[doc] = _storedBoundaryId;

                    tr.Commit();
                }

                // Notify palette UI
                BoundaryChanged?.Invoke(null, EventArgs.Empty);

                // Queue persistent Xrecord write
                QueueStoreXrecord(_storedBoundaryId);

                return true;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Gets the currently stored boundary polyline.
        /// </summary>
        public static bool TryGetBoundary(out Polyline pl)
        {
            pl = null;
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;

            if (!TryGetBoundaryId(out ObjectId id) || id.IsNull) return false;

            var db = doc.Database;
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    pl = tr.GetObject(id, OpenMode.ForRead) as Polyline;
                    return pl != null;
                }
            }
            catch { return false; }
        }


        /// <summary>
        /// Gets stored ObjectId for the active document.
        /// </summary>
        public static bool TryGetBoundaryId(out ObjectId id)
        {
            id = ObjectId.Null;
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;

            // Try in-memory first
            if (_storedBoundaries.TryGetValue(doc, out ObjectId stored) && !stored.IsNull && !stored.IsErased)
            {
                id = stored;
                return true;
            }

            // Palette-safe read from Xrecord
            ObjectId xId = ReadStoredBoundaryFromXrecord();
            if (!xId.IsNull && !xId.IsErased)
            {
                _storedBoundaries[doc] = xId;  // cache in memory
                _storedBoundaryId = xId;
                id = xId;
                return true;
            }

            return false;
        }


        /// <summary>
        /// Highlights the stored boundary in AutoCAD.
        /// </summary>
        public static void HighlightBoundary()
        {
            if (!TryGetBoundary(out Polyline pl)) return;
            var doc = Application.DocumentManager.MdiActiveDocument;
            doc.Editor.SetImpliedSelection(new ObjectId[] { pl.ObjectId });
        }

        /// <summary>
        /// Zooms the current view to the stored boundary.
        /// </summary>
        public static void ZoomToBoundary()
        {
            if (!TryGetBoundary(out Polyline pl)) return;
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            try
            {
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

        #endregion

        #region --- Palette-safe Xrecord persistence ---

        /// <summary>
        /// Stores ObjectId to a temporary variable and queues a command to persist Xrecord safely.
        /// </summary>
        private static void QueueStoreXrecord(ObjectId id)
        {
            if (id.IsNull) return;
            TempIdStore.IdToSave = id;

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            // FD_SaveBoundary is a command that runs outside the palette lock
            doc.SendStringToExecute("_.FD_SaveBoundary ", true, false, true);
        }

        /// <summary>
        /// Command method that runs in AutoCAD command context to write the Xrecord safely.
        /// </summary>
        [CommandMethod("FD_SaveBoundary")]
        public static void FD_SaveBoundary()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var db = doc.Database;
            ObjectId id = TempIdStore.IdToSave;
            if (id.IsNull) return;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

                Xrecord xr;
                if (nod.Contains(XrecordKey))
                {
                    xr = (Xrecord)tr.GetObject(nod.GetAt(XrecordKey), OpenMode.ForWrite);
                }
                else
                {
                    xr = new Xrecord();
                    nod.SetAt(XrecordKey, xr);
                    tr.AddNewlyCreatedDBObject(xr, true);
                }

                xr.Data = new ResultBuffer(new TypedValue((int)DxfCode.Handle, id.Handle.Value));
                tr.Commit();
            }
        }

        #endregion

        #region --- Internal Helpers ---

        private static Polyline FixBoundary(Polyline pl)
        {
            if (pl == null) return null;
            if (!IsCounterClockwise(pl))
            {
                pl.UpgradeOpen();
                pl.ReverseCurve();
            }
            return pl;
        }

        private static bool IsCounterClockwise(Polyline pl)
        {
            double sum = 0;
            for (int i = 0; i < pl.NumberOfVertices; i++)
            {
                Point2d a = pl.GetPoint2dAt(i);
                Point2d b = pl.GetPoint2dAt((i + 1) % pl.NumberOfVertices);
                sum += (b.X - a.X) * (b.Y + a.Y);
            }
            return sum < 0;
        }

        #endregion

    /// <summary>
    /// Temporary static store to pass ObjectId to command thread.
    /// </summary>
    public static class TempIdStore
    {
        public static ObjectId IdToSave = ObjectId.Null;
    }

    /// <summary>
/// Reads the stored polyline ObjectId from the Named Objects Dictionary (Xrecord).
/// Palette-safe: only reads (no UpgradeOpen, no LockDocument)
/// </summary>
private static ObjectId ReadStoredBoundaryFromXrecord()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return ObjectId.Null;

            var db = doc.Database;
            ObjectId id = ObjectId.Null;

            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                    if (!nod.Contains(XrecordKey)) { tr.Commit(); return ObjectId.Null; }

                    var xr = (Xrecord)tr.GetObject(nod.GetAt(XrecordKey), OpenMode.ForRead);
                    if (xr.Data == null) { tr.Commit(); return ObjectId.Null; }

                    var arr = xr.Data.AsArray();
                    if (arr.Length == 0) { tr.Commit(); return ObjectId.Null; }

                    var tv = arr[0];
                    if (tv.TypeCode != (int)DxfCode.Handle) { tr.Commit(); return ObjectId.Null; }

                    Handle h = new Handle(Convert.ToInt64(tv.Value));
                    id = db.GetObjectId(false, h, 0);

                    tr.Commit();
                }
            }
            catch
            {
                id = ObjectId.Null;
            }

            return id;
        }

    }
}
