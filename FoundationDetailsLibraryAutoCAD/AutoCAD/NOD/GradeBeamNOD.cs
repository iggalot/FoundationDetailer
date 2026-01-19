using Autodesk.AutoCAD.DatabaseServices;
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

            var xrec = tr.GetObject(gradeBeamDict.GetAt(NODCore.KEY_CENTERLINE), OpenMode.ForRead) as Xrecord;
            if (xrec == null || xrec.Data == null)
                return false;

            Database db = context.Document.Database; // <--- get database from context

            foreach (TypedValue tv in xrec.Data)
            {
                if (tv.TypeCode == (int)DxfCode.Text)
                {
                    string handleStr = tv.Value.ToString();

                    // Use context.Document.Database instead of tr.Database
                    if (!NODCore.TryGetObjectIdFromHandleString(context, db, handleStr, out centerlineId))
                        return false;

                    return !centerlineId.IsNull && centerlineId.IsValid && !centerlineId.IsErased;
                }
            }

            return false;
        }




        public static void StoreEdgeObjects(
            FoundationContext context,
            Transaction tr,
            ObjectId centerlineId,
            ObjectId leftEdgeId,
            ObjectId rightEdgeId)
        {
            if (context == null || tr == null) throw new ArgumentNullException();
            if (centerlineId.IsNull) throw new ArgumentNullException(nameof(centerlineId));

            var db = context.Document.Database;
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

            // Use your generic GetOrCreateNestedSubDictionary
            var handleDict = NODCore.GetOrCreateNestedSubDictionary(
                tr,
                nod,
                NODCore.ROOT,
                NODCore.KEY_GRADEBEAM_SUBDICT,
                centerlineId.Handle.ToString()
            );

            // Ensure edges subdictionary exists
            var edgesDict = NODCore.GetOrCreateNestedSubDictionary(
                tr,
                handleDict,
                NODCore.KEY_EDGES_SUBDICT
            );

            // Store left and right edges as Xrecords
            void AddEdge(string key, ObjectId id)
            {
                if (!edgesDict.Contains(key))
                {
                    var xrec = new Xrecord
                    {
                        Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, id.Handle.ToString()))
                    };
                    edgesDict.SetAt(key, xrec);
                    tr.AddNewlyCreatedDBObject(xrec, true);
                }
            }

            AddEdge(NODCore.KEY_EDGES_SUBDICT, leftEdgeId);
            AddEdge(NODCore.KEY_EDGES_SUBDICT, rightEdgeId);
        }
    }
}
