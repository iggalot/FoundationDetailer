using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.NOD
{
    public static class GradeBeamNOD
    {
        public static IEnumerable<(string Handle, DBDictionary Dict)> EnumerateGradeBeams(
            FoundationContext context,
            Transaction tr)
        {
            if (context == null || tr == null)
                yield break;

            var db = context.Document.Database;
            var nod = tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead) as DBDictionary;
            if (nod == null || !nod.Contains(NODCore.ROOT))
                yield break;

            var root = tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForRead) as DBDictionary;
            if (root == null || !root.Contains(NODCore.KEY_GRADEBEAM_SUBDICT))
                yield break;

            var gradeBeamContainer = tr.GetObject(root.GetAt(NODCore.KEY_GRADEBEAM_SUBDICT), OpenMode.ForRead) as DBDictionary;
            if (gradeBeamContainer == null)
                yield break;

            foreach (DBDictionaryEntry entry in gradeBeamContainer)
            {
                if (entry.Value is ObjectId oid)
                {
                    var handleDict = tr.GetObject(oid, OpenMode.ForRead) as DBDictionary;
                    if (handleDict != null)
                        yield return (entry.Key, handleDict);
                }
            }
        }

        public static IEnumerable<ObjectId> EnumerateGradeBeamEdges(
    FoundationContext context,
    Transaction tr,
    DBDictionary gradeBeamDict)
        {
            if (context == null || tr == null || gradeBeamDict == null)
                yield break;

            if (!gradeBeamDict.Contains(NODCore.KEY_EDGES_SUBDICT))
                yield break;

            var edgesDict = tr.GetObject(gradeBeamDict.GetAt(NODCore.KEY_EDGES_SUBDICT), OpenMode.ForRead) as DBDictionary;
            if (edgesDict == null)
                yield break;

            foreach (DBDictionaryEntry entry in edgesDict)
            {
                if (entry.Value is ObjectId oid && oid.IsValid && !oid.IsNull && !oid.IsErased)
                    yield return oid;
            }
        }

        public static IEnumerable<ObjectId> EnumerateAllGradeBeamCenterlines(
    FoundationContext context,
    Transaction tr)
        {
            if (context == null || tr == null)
                yield break;

            foreach (var (handle, dict) in EnumerateGradeBeams(context, tr))
            {
                if (TryGetCenterline(context, tr, dict, out var centerlineId))
                    yield return centerlineId;
            }
        }





        //public static void CreateStructure(...)
        //public static bool TryGetHandleKeys(...)
        //public static List<ObjectId> GetAllValidObjectIds(...)
        //public static void EraseEntry(...)
        //public static void ClearAll(...)

        public static bool TryGetGradeBeamPolylines(
        FoundationContext context,
        out List<Polyline> gradeBeams)
        {
            gradeBeams = new List<Polyline>();

            if (context == null)
                return false;

            var doc = context.Document;
            var db = doc.Database;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (!TryGetAllGradeBeamHandleKeys(context, tr, out var handles))
                    return false;

                foreach (string handleStr in handles)
                {
                    if (!NODCore.TryGetObjectIdFromHandleString(
                            context, db, handleStr, out ObjectId oid))
                        continue;

                    if (oid.IsNull || oid.IsErased || !oid.IsValid)
                        continue;

                    var pl = tr.GetObject(oid, OpenMode.ForRead, false) as Polyline;
                    if (pl != null)
                        gradeBeams.Add(pl);
                }

                return gradeBeams.Count > 0;
            }
        }


        public static bool TryGetAllGradeBeamHandleKeys(
    FoundationContext context,
    Transaction tr,
    out List<string> handleStrings)
        {
            handleStrings = new List<string>();

            if (context == null || tr == null)
                return false;

            var db = context.Document.Database;

            var nod = (DBDictionary)
                tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

            if (!nod.Contains(NODCore.ROOT))
                return false;

            var root = (DBDictionary)
                tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForRead);

            if (!root.Contains(NODCore.KEY_GRADEBEAM_SUBDICT))
                return false;

            var gradeBeamContainer = (DBDictionary)
                tr.GetObject(root.GetAt(NODCore.KEY_GRADEBEAM_SUBDICT), OpenMode.ForRead);

            // Each entry here is a grade beam dictionary named by handle
            foreach (DBDictionaryEntry entry in gradeBeamContainer)
                handleStrings.Add(entry.Key);

            return handleStrings.Count > 0;
        }

        public static List<ObjectId> GetAllValidGradeBeamObjectIdsFromSubDictionary(
    FoundationContext context,
    Transaction tr,
    Database db,
    string subDictKey)
        {
            var result = new List<ObjectId>();

            // GradeBeam-specific enumeration
            if (!TryGetAllGradeBeamHandleKeys(context, tr, out var handles))
                return result;

            foreach (var handleStr in handles)
            {
                if (!NODCore.TryGetObjectIdFromHandleString(context, db, handleStr, out ObjectId oid))
                    continue;

                if (oid.IsNull || oid.IsErased || !oid.IsValid)
                    continue;

                result.Add(oid);
            }

            return result;
        }

        /// <summary>
        /// Adds a grade beam polyline handle to the EE_Foundation NOD under FD_GRADEBEAM.
        /// </summary>
        /// <param name="id">The ObjectId of the grade beam polyline.</param>
        internal static void AddGradeBeamCenterlineHandleToNOD(
            FoundationContext context,
            ObjectId centerlineId,
            Transaction tr)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (centerlineId.IsNull || !centerlineId.IsValid) return;

            var db = context.Document.Database;

            // Use generic helper to create full structure safely
            var handleDict = NODCore.GetOrCreateNestedSubDictionary(
                tr,
                (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite),
                NODCore.ROOT,
                NODCore.KEY_GRADEBEAM_SUBDICT,
                centerlineId.Handle.ToString()
            );

            // Store CENTERLINE Xrecord
            if (!handleDict.Contains(NODCore.KEY_CENTERLINE))
            {
                Xrecord xrec = new Xrecord
                {
                    Data = new ResultBuffer(
                        new TypedValue((int)DxfCode.Text, centerlineId.Handle.ToString()))
                };
                handleDict.SetAt(NODCore.KEY_CENTERLINE, xrec);
                tr.AddNewlyCreatedDBObject(xrec, true);
            }

            // Ensure edges subdictionary exists
            NODCore.GetOrCreateNestedSubDictionary(tr, handleDict, NODCore.KEY_EDGES_SUBDICT);

            // Metadata Xrecord
            NODCore.GetOrCreateMetadataXrecord(tr, handleDict, NODCore.KEY_METADATA_SUBDICT);
        }


        public static void CreateGradeBeamNODStructure(
            FoundationContext context,
            Transaction tr,
            Database db,
            string gradeBeamHandle,
            ObjectId centerlineId)
        {
            if (tr == null || db == null || string.IsNullOrEmpty(gradeBeamHandle))
                throw new ArgumentNullException();

            string centerlineHandle = centerlineId.Handle.ToString();

            // Open the top-level NOD dictionary
            DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

            // Use nested helper to get the handle-level dictionary
            DBDictionary handleDict = NODCore.GetOrCreateNestedSubDictionary(
                tr,
                nod,
                NODCore.ROOT,
                NODCore.KEY_GRADEBEAM_SUBDICT,
                gradeBeamHandle
            );

            // Attach the existing centerline entity as an Xrecord
            if (!handleDict.Contains(NODCore.KEY_CENTERLINE))
            {
                Xrecord xrec = new Xrecord
                {
                    Data = new ResultBuffer(
                        new TypedValue((int)DxfCode.Text, centerlineHandle))
                };

                handleDict.SetAt(NODCore.KEY_CENTERLINE, xrec);
                tr.AddNewlyCreatedDBObject(xrec, true);
            }

            // Ensure FD_EDGES subdictionary exists (nested helper can be used here too)
            NODCore.GetOrCreateNestedSubDictionary(
                tr,
                handleDict,
                NODCore.KEY_EDGES_SUBDICT
            );

            // Add metadata Xrecord for future use
            NODCore.GetOrCreateMetadataXrecord(tr, handleDict, NODCore.KEY_METADATA_SUBDICT);
        }



        internal static void EraseGradeBeamEntry(
    Transaction tr,
    Database db,
    string gradeBeamHandle)
        {
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (string.IsNullOrWhiteSpace(gradeBeamHandle))
                throw new ArgumentException("Invalid grade beam handle.", nameof(gradeBeamHandle));

            gradeBeamHandle = gradeBeamHandle.Trim().ToUpperInvariant();

            DBDictionary nod =
                (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

            if (!nod.Contains(NODCore.ROOT))
                return;

            DBDictionary root =
                (DBDictionary)tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForRead);

            if (!root.Contains(NODCore.KEY_GRADEBEAM_SUBDICT))
                return;

            DBDictionary gradeBeams =
                (DBDictionary)tr.GetObject(root.GetAt(NODCore.KEY_GRADEBEAM_SUBDICT), OpenMode.ForWrite);

            if (!gradeBeams.Contains(gradeBeamHandle))
                return;

            // Erase the grade beam dictionary (children erased automatically)
            DBDictionary gbDict =
                (DBDictionary)tr.GetObject(gradeBeams.GetAt(gradeBeamHandle), OpenMode.ForWrite);

            gbDict.Erase();
        }

        // GradeBeamNOD
        public static bool TryGetCenterlineObjectId(
            FoundationContext context,
            Transaction tr,
            string gradeBeamHandle,
            out ObjectId centerlineId)
        {
            centerlineId = ObjectId.Null;

            if (context == null || tr == null || string.IsNullOrWhiteSpace(gradeBeamHandle))
                return false;

            var db = context.Document.Database;

            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(NODCore.ROOT))
                return false;

            var root = (DBDictionary)tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForRead);
            if (!root.Contains(NODCore.KEY_GRADEBEAM_SUBDICT))
                return false;

            var gbRoot = (DBDictionary)tr.GetObject(
                root.GetAt(NODCore.KEY_GRADEBEAM_SUBDICT),
                OpenMode.ForRead);

            if (!gbRoot.Contains(gradeBeamHandle))
                return false;

            var gbDict = (DBDictionary)tr.GetObject(
                gbRoot.GetAt(gradeBeamHandle),
                OpenMode.ForRead);

            if (!gbDict.Contains(NODCore.KEY_CENTERLINE))
                return false;

            var xr = tr.GetObject(
                gbDict.GetAt(NODCore.KEY_CENTERLINE),
                OpenMode.ForRead) as Xrecord;

            if (xr?.Data == null)
                return false;

            foreach (TypedValue tv in xr.Data)
            {
                if (tv.TypeCode == (int)DxfCode.Text &&
                    NODCore.TryGetObjectIdFromHandleString(
                        context, db, tv.Value.ToString(), out centerlineId))
                {
                    return true;
                }
            }

            return false;
        }

        // GradeBeamNOD
        public static DBDictionary GetEdgesDictionary(
            Transaction tr,
            Database db,
            string gradeBeamHandle,
            bool forWrite)
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            var root = (DBDictionary)tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForRead);
            var gbRoot = (DBDictionary)tr.GetObject(
                root.GetAt(NODCore.KEY_GRADEBEAM_SUBDICT),
                OpenMode.ForRead);

            var gbDict = (DBDictionary)tr.GetObject(
                gbRoot.GetAt(gradeBeamHandle),
                forWrite ? OpenMode.ForWrite : OpenMode.ForRead);

            return (DBDictionary)tr.GetObject(
                gbDict.GetAt(NODCore.KEY_EDGES_SUBDICT),
                OpenMode.ForWrite);
        }

        // GradeBeamNOD
        public static void ClearEdges(
            Transaction tr,
            Database db,
            string gradeBeamHandle)
        {
            var edges = GetEdgesDictionary(tr, db, gradeBeamHandle, true);

            var keys = NODCore.EnumerateDictionary(edges)
                              .Select(e => e.Key)
                              .ToList();

            foreach (var key in keys)
            {
                try
                {
                    var obj = tr.GetObject(edges.GetAt(key), OpenMode.ForWrite);
                    obj.Erase();
                }
                catch { }
            }
        }

        public static bool TryGetCenterline(
            FoundationContext context,
            Transaction tr,
            DBDictionary gradeBeamDict,
            out ObjectId centerlineId)
        {
            centerlineId = ObjectId.Null;

            if (context == null || tr == null || gradeBeamDict == null)
                return false;

            if (!gradeBeamDict.Contains(NODCore.KEY_CENTERLINE))
                return false;

            var xrecObj = gradeBeamDict.GetAt(NODCore.KEY_CENTERLINE);
            if (xrecObj.IsNull || xrecObj.IsErased)
                return false;

            Xrecord xrec;
            try
            {
                xrec = tr.GetObject(xrecObj, OpenMode.ForRead) as Xrecord;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return false;
            }

            if (xrec?.Data == null)
                return false;

            var db = context.Document.Database;

            foreach (TypedValue tv in xrec.Data)
            {
                if (tv.TypeCode != (int)DxfCode.Text)
                    continue;

                var handleStr = tv.Value as string;
                if (string.IsNullOrWhiteSpace(handleStr))
                    continue;

                if (!NODCore.TryGetObjectIdFromHandleString(
                        context, db, handleStr, out var oid))
                    continue;

                if (oid.IsNull || !oid.IsValid)
                    continue;

                DBObject obj;
                try
                {
                    obj = tr.GetObject(oid, OpenMode.ForRead, false);
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    continue;
                }

                if (obj == null || obj.IsErased)
                    continue;

                centerlineId = oid;
                return true;
            }

            return false;
        }



        public static void StoreEdgeObjects(
            FoundationContext context,
            Transaction tr,
            ObjectId centerlineId,
            IEnumerable<ObjectId> leftEdgeIds,
            IEnumerable<ObjectId> rightEdgeIds)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (centerlineId.IsNull) throw new ArgumentNullException(nameof(centerlineId));

            var db = context.Document.Database;
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

            // --- Get or create GradeBeam dictionary (safe here because we are NOT enumerating)
            var gradebeamDict = NODCore.GetOrCreateNestedSubDictionary(
                tr,
                nod,
                NODCore.ROOT,
                NODCore.KEY_GRADEBEAM_SUBDICT,
                centerlineId.Handle.ToString());

            var edgesDict = NODCore.GetOrCreateNestedSubDictionary(
                tr,
                gradebeamDict,
                NODCore.KEY_EDGES_SUBDICT);

            AddEdges(edgesDict, tr, "LEFT", leftEdgeIds);
            AddEdges(edgesDict, tr, "RIGHT", rightEdgeIds);
        }

        private static void AddEdges(
            DBDictionary edgesDict,
            Transaction tr,
            string keyPrefix,
            IEnumerable<ObjectId> ids)
        {
            if (edgesDict == null || ids == null)
                return;

            // 🔓 Ensure write access
            if (!edgesDict.IsWriteEnabled)
                edgesDict.UpgradeOpen();

            // 🔢 Count existing edges with this prefix (NO LINQ CAST)
            int counter = 0;
            foreach (DBDictionaryEntry entry in edgesDict)
            {
                if (entry.Key.StartsWith(keyPrefix))
                    counter++;
            }

            foreach (var id in ids)
            {
                if (id.IsNull)
                    continue;

                string key = $"{keyPrefix}_{counter++}";

                var xrec = new Xrecord
                {
                    Data = new ResultBuffer(
                        new TypedValue((int)DxfCode.Text, id.Handle.ToString()))
                };

                edgesDict.SetAt(key, xrec);
                tr.AddNewlyCreatedDBObject(xrec, true);
            }
        }



        public static bool HasEdges(Transaction tr, DBDictionary gradeBeamDict)
        {
            if (tr == null || gradeBeamDict == null)
                return false;

            if (!NODCore.TryGetNestedSubDictionary(
                    tr,
                    gradeBeamDict,
                    out var edgesDict,
                    NODCore.KEY_EDGES_SUBDICT))
                return false;

            return edgesDict.Count > 0;
        }




        /// <summary>
        /// Deletes a single grade beam node and all its subdictionaries/XRecords.
        /// Only affects the NOD structure; does NOT touch AutoCAD entities.
        /// </summary>
        /// <param name="context">Current foundation context</param>
        /// <param name="centerlineHandle">Handle string of the centerline for the grade beam to delete</param>
        /// <returns>True if deletion succeeded, false otherwise</returns>
        public static bool DeleteGradeBeamNode(FoundationContext context, string centerlineHandle)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(centerlineHandle)) throw new ArgumentNullException(nameof(centerlineHandle));

            Document doc = context.Document;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
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
        }

        /// <summary>
        /// Recursively searches the grade beam container to find which top-level grade beam dictionary owns the handle/key.
        /// </summary>
        internal static string FindGradeBeamKeyForHandle(Transaction tr, DBDictionary gradeBeamContainer, string handleOrKey)
        {
            foreach (var (gradeBeamKey, gbId) in NODCore.EnumerateDictionary(gradeBeamContainer))
            {
                var gbDict = tr.GetObject(gbId, OpenMode.ForRead) as DBDictionary;
                if (gbDict == null) continue;

                if (string.Equals(gradeBeamKey, handleOrKey, StringComparison.OrdinalIgnoreCase))
                    return gradeBeamKey;

                if (NODCore.IsKeyOrHandleInDictionary(tr, gbDict, handleOrKey))
                    return gradeBeamKey;
            }

            return null;
        }
    }
}
