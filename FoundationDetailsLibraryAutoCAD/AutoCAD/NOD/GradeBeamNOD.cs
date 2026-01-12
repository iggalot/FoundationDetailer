using Autodesk.AutoCAD.DatabaseServices;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Collections.Generic;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.NOD
{
    public static class GradeBeamNOD
    {
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
            ObjectId id,
            Transaction tr)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (id.IsNull || !id.IsValid) return;

            var doc = context.Document;
            var db = doc.Database;

            // Ensure EE_Foundation NOD exists
            NODCore.InitFoundationNOD(context, tr);

            DBDictionary nod =
                (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

            DBDictionary root =
                (DBDictionary)tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForWrite);

            DBDictionary gradebeamDict =
                (DBDictionary)tr.GetObject(root.GetAt(NODCore.KEY_GRADEBEAM_SUBDICT), OpenMode.ForWrite);

            // Handle string
            string handleStr = id.Handle.ToString().ToUpperInvariant();

            // Create full grade beam structure (safe if already exists)
            CreateGradeBeamNODStructure(context, tr, db, handleStr, id);
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

            DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
            DBDictionary root = NODCore.GetOrCreateSubDictionary(tr, nod, NODCore.ROOT);
            DBDictionary gradebeamDict = NODCore.GetOrCreateSubDictionary(tr, root, NODCore.KEY_GRADEBEAM_SUBDICT);
            DBDictionary handleDict = NODCore.GetOrCreateSubDictionary(tr, gradebeamDict, gradeBeamHandle);

            // Attach the existing centerline entity to the handle directory
            // ------------------------------------------------
            // CENTERLINE HANDLE (XRecord, persistent)
            // ------------------------------------------------
            if (!handleDict.Contains(NODCore.KEY_CENTERLINE))
            {
                Xrecord xrec = new Xrecord();
                xrec.Data = new ResultBuffer(
                    new TypedValue((int)DxfCode.Text, centerlineHandle));

                handleDict.SetAt(NODCore.KEY_CENTERLINE, xrec);
                tr.AddNewlyCreatedDBObject(xrec, true);
            }

            // Ensure FD_EDGES sub dictionar exists
            NODCore.GetOrCreateSubDictionary(tr, handleDict, NODCore.KEY_EDGES_SUBDICT);

            // Add metadata Xrecord for future use -- this is a single xrecord and not a subdictionary at this time
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




    }
}
