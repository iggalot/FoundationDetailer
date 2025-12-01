using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;

namespace FoundationDetailer.Utilities
{
    public static class PolylineBoundaryManager
    {
        private static readonly Dictionary<Document, ObjectId> _storedBoundaries = new Dictionary<Document, ObjectId>();
        private const string XrecordKey = "FD_BOUNDARY";

        // Single boundary per active document
        private static ObjectId _storedBoundaryId = ObjectId.Null;

        private static readonly string[] MonitoredCommands =
        {
            "MOVE", "STRETCH", "GRIP_STRETCH", "PEDIT", "EXPLODE", "GRIPS", "GRIP_MOVE"
        };

        public static event EventHandler BoundaryChanged;

        #region --- Initialization ---

        static PolylineBoundaryManager()
        {
            Initialize();
        }

        public static void Initialize()
        {
            // Attach to all open documents
            foreach (Document doc in Application.DocumentManager)
                AttachDocumentEvents(doc);

            // Hook document creation
            Application.DocumentManager.DocumentCreated -= DocManager_DocumentCreated;
            Application.DocumentManager.DocumentCreated += DocManager_DocumentCreated;

            // Hook active document change
            // Hook active document change
            Application.DocumentManager.DocumentActivated -= DocManager_ActiveDocumentChanged;
            Application.DocumentManager.DocumentActivated += DocManager_ActiveDocumentChanged;


            // Load boundary for active document at startup
            LoadBoundaryForActiveDocument();
        }

        private static void DocManager_DocumentCreated(object sender, DocumentCollectionEventArgs e)
        {
            AttachDocumentEvents(e.Document);
        }

        private static void DocManager_ActiveDocumentChanged(object sender, EventArgs e)
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            // Load boundary for the active document
            LoadBoundary(doc);

            // Highlight and zoom if boundary exists
            if (_storedBoundaries.ContainsKey(doc))
            {
                ObjectId id = _storedBoundaries[doc];
                if (!id.IsNull)
                {
                    try
                    {
                        using (doc.LockDocument())
                        using (var tr = doc.TransactionManager.StartTransaction())
                        {
                            var pl = tr.GetObject(id, OpenMode.ForRead) as Polyline;
                            if (pl != null)
                            {
                                HighlightBoundary();
                                ZoomToBoundary();
                            }
                            tr.Commit();
                        }
                    }
                    catch
                    {
                        // Fail silently
                    }
                }
            }

            // Notify subscribers
            BoundaryChanged?.Invoke(null, EventArgs.Empty);
        }


        private static void AttachDocumentEvents(Document doc)
        {
            if (doc == null) return;
            var db = doc.Database;

            db.ObjectErased -= Db_ObjectErased;
            db.ObjectErased += Db_ObjectErased;

            db.ObjectModified -= Db_ObjectModified;
            db.ObjectModified += Db_ObjectModified;

            doc.CommandEnded -= Doc_CommandEnded;
            doc.CommandEnded += Doc_CommandEnded;

            // Load boundary for this document
            if (doc == Application.DocumentManager.MdiActiveDocument)
                LoadBoundary(doc);
        }

        private static void LoadBoundaryForActiveDocument()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
                LoadBoundary(doc);
        }

        #endregion

        #region --- Dispose ---

        public static void Dispose()
        {
            foreach (Document doc in Application.DocumentManager)
            {
                var db = doc.Database;
                db.ObjectErased -= Db_ObjectErased;
                db.ObjectModified -= Db_ObjectModified;
                doc.CommandEnded -= Doc_CommandEnded;
            }

            Application.DocumentManager.DocumentCreated -= DocManager_DocumentCreated;
            Application.DocumentManager.DocumentActivated -= DocManager_ActiveDocumentChanged;

            _storedBoundaryId = ObjectId.Null;
            BoundaryChanged = null;
        }

        #endregion

        #region --- Public API ---

        public static bool TrySetBoundary(ObjectId id, out string error)
        {
            error = "";
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) { error = "No active document."; return false; }

            try
            {
                using (doc.LockDocument())
                using (var tr = doc.TransactionManager.StartTransaction())
                {
                    var pl = tr.GetObject(id, OpenMode.ForRead) as Polyline;
                    if (pl == null) { error = "Selected object is not a polyline."; return false; }
                    if (!pl.Closed) { error = "Polyline must be closed."; return false; }

                    Polyline cleaned = FixBoundary(pl);

                    // Update stored ID
                    _storedBoundaryId = cleaned.ObjectId;

                    // Persist in XRecord
                    StorePolylineId(doc, _storedBoundaryId, tr);

                    tr.Commit();
                }

                BoundaryChanged?.Invoke(null, EventArgs.Empty);
                return true;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TryGetBoundary(out Polyline pl)
        {
            pl = null;
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;
            var db = doc.Database;

            if (_storedBoundaryId == ObjectId.Null) return false;

            try
            {
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    pl = tr.GetObject(_storedBoundaryId, OpenMode.ForRead) as Polyline;
                    return pl != null;
                }
            }
            catch { return false; }
        }

        public static void HighlightBoundary()
        {
            if (!TryGetBoundary(out Polyline pl)) return;
            var doc = Application.DocumentManager.MdiActiveDocument;
            doc.Editor.SetImpliedSelection(new ObjectId[] { pl.ObjectId });
        }

        public static void ZoomToBoundary()
        {
            if (!TryGetBoundary(out Polyline pl)) return;
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            try
            {
                using (doc.LockDocument())
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
            }
            catch { }
        }

        #endregion

        #region --- Event Handlers ---

        private static void Db_ObjectErased(object sender, ObjectErasedEventArgs e)
        {
            if (e.DBObject == null) return;
            if (e.DBObject.ObjectId == _storedBoundaryId)
            {
                _storedBoundaryId = ObjectId.Null;
                BoundaryChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        private static void Db_ObjectModified(object sender, ObjectEventArgs e)
        {
            if (e.DBObject == null) return;
            if (e.DBObject.ObjectId == _storedBoundaryId)
            {
                BoundaryChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        private static void Doc_CommandEnded(object sender, CommandEventArgs e)
        {
            string cmd = e.GlobalCommandName.ToUpperInvariant();
            foreach (var monitored in MonitoredCommands)
            {
                if (cmd == monitored)
                {
                    BoundaryChanged?.Invoke(null, EventArgs.Empty);
                    break;
                }
            }
        }

        #endregion

        #region --- Internal Helpers ---

        private static void StorePolylineId(Document doc, ObjectId id, Transaction tr)
        {
            var db = doc.Database;
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
        }

        private static void LoadBoundary(Document doc)
        {
            var db = doc.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                if (!nod.Contains(XrecordKey)) { _storedBoundaryId = ObjectId.Null; tr.Commit(); return; }

                var xr = (Xrecord)tr.GetObject(nod.GetAt(XrecordKey), OpenMode.ForRead);
                if (xr.Data == null) { _storedBoundaryId = ObjectId.Null; tr.Commit(); return; }

                var arr = xr.Data.AsArray();
                if (arr.Length == 0) { _storedBoundaryId = ObjectId.Null; tr.Commit(); return; }

                var tv = arr[0];
                if (tv.TypeCode != (int)DxfCode.Handle) { _storedBoundaryId = ObjectId.Null; tr.Commit(); return; }

                Handle h = new Handle(Convert.ToInt64(tv.Value));
                _storedBoundaryId = db.GetObjectId(false, h, 0);

                tr.Commit();
            }
        }

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
    }
}
