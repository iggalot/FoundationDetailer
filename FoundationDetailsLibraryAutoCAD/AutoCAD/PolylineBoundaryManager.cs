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
            try
            {
                return SetBoundaryInternal(pl);
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

            using (var tr = doc.TransactionManager.StartTransaction())
            {
                try
                {
                    var pl = tr.GetObject(id, OpenMode.ForRead) as Polyline;
                    if (pl == null)
                    {
                        error = "Selected object is not a polyline.";
                        return false;
                    }

                    if (!TrySetBoundary(pl))
                    {
                        error = "Polyline must be closed and wound counter-clockwise (CCW).";
                        return false;
                    }

                    tr.Commit();
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
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

            using (var tr = db.TransactionManager.StartTransaction())
            {
                ObjectId plId = GetStoredPolylineId(tr, db);
                if (plId == ObjectId.Null || plId.IsErased)
                    return false;

                pl = tr.GetObject(plId, OpenMode.ForRead) as Polyline;
                return pl != null;
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

        private static bool SetBoundaryInternal(Polyline pl)
        {
            if (pl == null) return false;
            if (!pl.Closed) return false;

            if (!IsCounterClockwise(pl))
            {
                pl.ReverseCurve();
            }

            var doc = Application.DocumentManager.MdiActiveDocument;
            using (var tr = doc.TransactionManager.StartTransaction())
            {
                var db = doc.Database;
                var ms = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                // Remove previous boundary if exists
                if (TryGetBoundary(out Polyline oldPl))
                {
                    oldPl.UpgradeOpen();
                    oldPl.Erase();
                }

                // Clone polyline into current space
                var plClone = (Polyline)pl.Clone();
                ms.AppendEntity(plClone);
                tr.AddNewlyCreatedDBObject(plClone, true);

                // Store its ObjectId in XRecord for persistence
                StorePolylineId(plClone.ObjectId, tr, db);

                tr.Commit();
            }

            return true;
        }

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

        private static ObjectId GetStoredPolylineId(Transaction tr, Database db)
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(XrecordKey)) return ObjectId.Null;

            var xr = (Xrecord)tr.GetObject(nod.GetAt(XrecordKey), OpenMode.ForRead);
            if (xr.Data == null || xr.Data.AsArray().Length == 0) return ObjectId.Null;

            var tv = xr.Data.AsArray()[0];
            if (tv.TypeCode != (int)DxfCode.Handle) return ObjectId.Null;

            Handle h = new Handle((long)tv.Value);
            ObjectId id = db.GetObjectId(false, h, 0);
            return id;
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
            return sum < 0; // negative = CCW
        }

        #endregion
    }
}
