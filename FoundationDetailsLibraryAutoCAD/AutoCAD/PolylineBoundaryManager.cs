using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using System;

namespace FoundationDetailer.Utilities
{
    /// <summary>
    /// Manages the foundation boundary polyline.
    /// Only one polyline per drawing is allowed.
    /// Persists via DWG XRecord.
    /// </summary>
    public static class PolylineBoundaryManager
    {
        private const string XrecordKey = "FD_BOUNDARY";

        #region --- Public API ---

        /// <summary>
        /// Safely sets the boundary from a Polyline object.
        /// Returns true if successfully stored; false otherwise.
        /// </summary>
        public static bool TrySetBoundary(Polyline pl)
        {
            if (pl == null) return false;

            var doc = Application.DocumentManager.MdiActiveDocument;

            try
            {
                using (doc.LockDocument())
                using (var tr = doc.TransactionManager.StartTransaction())
                {
                    return SetBoundaryInternal(pl, tr, doc.Database);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Safely sets the boundary from an ObjectId.
        /// Returns true if valid and saved, false otherwise.
        /// </summary>
        public static bool TrySetBoundary(ObjectId id, out string error)
        {
            error = "";
            var doc = Application.DocumentManager.MdiActiveDocument;

            try
            {
                using (doc.LockDocument())
                using (var tr = doc.TransactionManager.StartTransaction())
                {
                    var pl = tr.GetObject(id, OpenMode.ForRead) as Polyline;
                    if (pl == null)
                    {
                        error = "Selected object is not a polyline.";
                        return false;
                    }

                    if (!pl.Closed)
                    {
                        error = "Polyline must be closed.";
                        return false;
                    }

                    if (!IsCounterClockwise(pl))
                        pl.ReverseCurve();

                    if (!SetBoundaryInternal(pl, tr, doc.Database))
                    {
                        error = "Failed to store boundary.";
                        return false;
                    }

                    tr.Commit();
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Returns the stored boundary polyline if it exists.
        /// </summary>
        public static bool TryGetBoundary(out Polyline pl)
        {
            pl = null;
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;

            try
            {
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    ObjectId plId = GetStoredPolylineId(tr, db);
                    if (plId == ObjectId.Null || plId.IsErased)
                        return false;

                    pl = tr.GetObject(plId, OpenMode.ForRead) as Polyline;
                    return pl != null;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Highlights the boundary in AutoCAD.
        /// </summary>
        public static void HighlightBoundary()
        {
            if (!TryGetBoundary(out Polyline pl)) return;

            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            ed.SetImpliedSelection(new ObjectId[] { pl.ObjectId });
        }

        #endregion

        #region --- Internal Helpers ---

        /// <summary>
        /// Core boundary storage logic.
        /// Must be called inside a transaction.
        /// </summary>
        private static bool SetBoundaryInternal(Polyline pl, Transaction tr, Database db)
        {
            if (pl == null || !pl.Closed) return false;

            var ms = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

            // Remove previous boundary safely
            ObjectId oldId = GetStoredPolylineId(tr, db);
            if (oldId != ObjectId.Null && !oldId.IsErased)
            {
                var oldPl = tr.GetObject(oldId, OpenMode.ForWrite) as Polyline;
                oldPl?.Erase();
            }

            // Clone into current space
            var plClone = (Polyline)pl.Clone();
            ms.AppendEntity(plClone);
            tr.AddNewlyCreatedDBObject(plClone, true);

            // Store ObjectId in XRecord
            StorePolylineId(plClone.ObjectId, tr, db);

            return true;
        }

        /// <summary>
        /// Saves the polyline ObjectId in the Named Objects Dictionary.
        /// </summary>
        private static void StorePolylineId(ObjectId id, Transaction tr, Database db)
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
        }

        /// <summary>
        /// Retrieves the stored polyline ObjectId.
        /// </summary>
        private static ObjectId GetStoredPolylineId(Transaction tr, Database db)
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(XrecordKey)) return ObjectId.Null;

            var xr = (Xrecord)tr.GetObject(nod.GetAt(XrecordKey), OpenMode.ForRead);
            if (xr.Data == null || xr.Data.AsArray().Length == 0) return ObjectId.Null;

            var tv = xr.Data.AsArray()[0];
            if (tv.TypeCode != (int)DxfCode.Handle) return ObjectId.Null;

            Handle h = new Handle(Convert.ToInt64(tv.Value));
            return db.GetObjectId(false, h, 0);
        }

        /// <summary>
        /// Returns true if polyline vertices are counter-clockwise.
        /// </summary>
        private static bool IsCounterClockwise(Polyline pl)
        {
            double sum = 0;
            for (int i = 0; i < pl.NumberOfVertices; i++)
            {
                Point2d a = pl.GetPoint2dAt(i);
                Point2d b = pl.GetPoint2dAt((i + 1) % pl.NumberOfVertices);
                sum += (b.X - a.X) * (b.Y + a.Y);
            }
            return sum < 0; // negative = CCW
        }

        #endregion
    }
}
