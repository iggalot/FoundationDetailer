using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FoundationDetailer.UI.Windows;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using static FoundationDetailsLibraryAutoCAD.AutoCAD.NOD.HandleHandler;

[assembly: CommandClass(typeof(FoundationDetailsLibraryAutoCAD.AutoCAD.NOD.NODCore))]

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.NOD
{
    public partial class NODCore
    {
        // ==========================================================
        //  CONSTANTS
        // ==========================================================
        public const string ROOT = "EE_Foundation";
        public const string KEY_BOUNDARY_SUBDICT = "FD_BOUNDARY";
        public const string KEY_GRADEBEAM_SUBDICT = "FD_GRADEBEAM";
        public const string KEY_BEAMSTRAND_SUBDICT = "FD_BEAMSTRAND";
        public const string KEY_SLABSTRAND_SUBDICT = "FD_SLABSTRAND";
        public const string KEY_REBAR = "FD_REBAR";

        public const string KEY_CENTERLINE = "FD_CENTERLINE";
        public const string KEY_EDGES_SUBDICT = "FD_EDGES";
        public const string KEY_METADATA_SUBDICT = "FD_METADATA"; // for storing future data in the NOD

        public static readonly string[] KNOWN_SUBDIRS = { KEY_BOUNDARY_SUBDICT, KEY_GRADEBEAM_SUBDICT, KEY_BEAMSTRAND_SUBDICT, KEY_SLABSTRAND_SUBDICT, KEY_REBAR };

        public static void InitFoundationNOD(FoundationContext context, Transaction tr)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));

            var db = context.Document.Database;
            var ed = context.Document.Editor;

            try
            {
                // Ensure the root dictionary exists
                var rootDict = NODCore.GetOrCreateNestedSubDictionary(
                    tr,
                    (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite),
                    NODCore.ROOT
                );

                // Ensure all known subdirectories exist under the root
                foreach (var subKey in KNOWN_SUBDIRS)
                {
                    NODCore.GetOrCreateNestedSubDictionary(tr, rootDict, subKey);
                }

                ed.WriteMessage("\nEE_Foundation NOD structure initialized successfully.");
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                ed.WriteMessage($"\nTransaction failed: {ex.Message}");
            }
        }



        public static List<HandleEntry> ScanFoundationNod(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var model = context.Model;
            var db = doc.Database;

            var results = new List<HandleEntry>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    DBDictionary root = GetFoundationRootDictionary(tr, db);
                    if (root == null)
                        return results;

                    foreach (DBDictionaryEntry groupEntry in root)
                    {
                        ScanGroupDictionary(
                            context,
                            tr,
                            db,
                            groupEntry,
                            results
                        );
                    }

                    tr.Commit(); // read-only but still correct
                }
                catch (System.Exception ex)
                {
                    doc.Editor.WriteMessage(
                        $"\n[EE_FOUNDATION] Scan failed: {ex.Message}"
                    );
                }
            }

            return results;
        }
        private static DBDictionary GetFoundationRootDictionary(
        Transaction tr,
        Database db)
        {
            var nod = tr.GetObject(
                db.NamedObjectsDictionaryId,
                OpenMode.ForRead) as DBDictionary;

            if (nod == null || !nod.Contains(ROOT))
                return null;

            return tr.GetObject(
                nod.GetAt(ROOT),
                OpenMode.ForRead) as DBDictionary;
        }

        private static void ScanGroupDictionary(FoundationContext context,
    Transaction tr,
    Database db,
    DBDictionaryEntry groupEntry,
    List<HandleEntry> results)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var model = context.Model;

            if (doc == null) return;

            var subDict = tr.GetObject(
                groupEntry.Value,
                OpenMode.ForRead) as DBDictionary;

            if (subDict == null)
                return;

            foreach (DBDictionaryEntry entry in subDict)
            {
                HandleEntry result = ValidateHandle(
                    context,
                    tr,
                    db,
                    groupEntry.Key,
                    entry.Key
                );

                results.Add(result);
            }
        }

        private static HandleEntry ValidateHandle(FoundationContext context,
            Transaction tr,
            Database db,
            string groupName,
            string handleStr)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var result = new HandleEntry
            {
                GroupName = groupName,
                HandleKey = handleStr
            };

            // 1. Handle format check (HEX only)
            if (!IsValidHexHandle(handleStr))
            {
                result.Status = HandleStatus.Invalid;
                return result;
            }

            // 2. Resolve handle -> ObjectId
            ObjectId id;
            try
            {
                if (!TryGetObjectIdFromHandleString(context, db, handleStr, out id))
                {
                    result.Status = HandleStatus.Invalid;
                    return result;
                }
            }
            catch
            {
                // Unexpected failure resolving handle
                result.Status = HandleStatus.Error;
                return result;
            }

            result.Id = id;

            // 3. Attempt to open object
            try
            {
                DBObject obj = tr.GetObject(id, OpenMode.ForRead, false);

                if (obj == null || obj.IsErased)
                {
                    result.Status = HandleStatus.Missing;
                    return result;
                }

                result.Status = HandleStatus.Valid;
                return result;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // Expected AutoCAD case:
                // hard-erased / purged / zombie ObjectId
                result.Status = HandleStatus.Missing;
                return result;
            }
            catch
            {
                // Unexpected failure (transaction misuse, DB corruption, etc.)
                result.Status = HandleStatus.Error;
                return result;
            }
        }

        private static bool IsValidHexHandle(string handle)
        {
            if (string.IsNullOrWhiteSpace(handle))
                return false;

            for (int i = 0; i < handle.Length; i++)
            {
                char c = handle[i];

                bool isHex =
                    (c >= '0' && c <= '9') ||
                    (c >= 'A' && c <= 'F') ||
                    (c >= 'a' && c <= 'f');

                if (!isHex)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Recursively scans any dictionary or subtree in the NOD for a stored handle.
        /// </summary>
        /// <param name="tr">Active transaction</param>
        /// <param name="rootDictId">ObjectId of the dictionary to scan (can be NamedObjectsDictionary or any sub-dictionary)</param>
        /// <param name="targetHandle">Handle stored as text in XRecord</param>
        /// <returns>True if handle exists anywhere in the subtree</returns>
        internal static bool IsHandleAlreadyInTree(FoundationContext context,
            Transaction tr,
            ObjectId rootDictId,
            string targetHandle)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (rootDictId.IsNull || !rootDictId.IsValid)
                return false;

            DBObject rootObj = tr.GetObject(rootDictId, OpenMode.ForRead);
            if (!(rootObj is DBDictionary rootDict))
                return false;

            return ScanDictionaryForHandle(context, tr, rootDict, targetHandle);
        }

        private static bool ScanDictionaryForHandle(
            FoundationContext context,
            Transaction tr,
            DBDictionary dict,
            string targetHandle)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            foreach (DBDictionaryEntry entry in dict)
            {
                DBObject obj =
                    tr.GetObject(entry.Value, OpenMode.ForRead);

                if (obj is DBDictionary subDict)
                {
                    if (ScanDictionaryForHandle(context, tr, subDict, targetHandle))
                        return true;
                }
                else if (obj is Xrecord xrec)
                {
                    if (XrecordContainsHandle(xrec, targetHandle))
                        return true;
                }
            }

            return false;
        }

        private static bool XrecordContainsHandle(
            Xrecord xrec,
            string targetHandle)
        {
            if (xrec.Data == null)
                return false;

            foreach (TypedValue tv in xrec.Data)
            {
                if (tv.TypeCode == (int)DxfCode.Text &&
                    tv.Value is string s &&
                    s.Equals(targetHandle, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static void CleanupFoundationNod(
            FoundationContext context,
            IEnumerable<HandleEntry> scanResults)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var model = context.Model;
            var db = doc.Database;

            if (scanResults == null)
                return;

            using (doc.LockDocument()) // REQUIRED
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        DBDictionary root = GetFoundationRootDictionary(tr, db);
                        if (root == null)
                            return;

                        foreach (var group in scanResults
                            .Where(r => r.Status != "Valid")
                            .GroupBy(r => r.GroupName))
                        {
                            CleanupGroup(
                                tr,
                                root,
                                group.Key,
                                group.Select(r => r.HandleKey)
                            );
                        }

                        tr.Commit();
                    }
                    catch (System.Exception ex)
                    {
                        doc.Editor.WriteMessage(
                            $"\n[EE_FOUNDATION] Cleanup failed: {ex.Message}"
                        );
                    }
                }
            }
        }

        private static void CleanupGroup(
            Transaction tr,
            DBDictionary root,
            string groupName,
            IEnumerable<string> handleKeys)
        {
            if (!root.Contains(groupName))
                return;

            var subDict = tr.GetObject(
                root.GetAt(groupName),
                OpenMode.ForRead) as DBDictionary;

            if (subDict == null)
                return;

            subDict.UpgradeOpen();

            foreach (string handle in handleKeys)
            {
                if (subDict.Contains(handle))
                {
                    subDict.Remove(handle);
                }
            }
        }

        // ==========================================================
        //  ITERATE AND CLEAN HANDLES
        // ==========================================================
        public static List<HandleEntry> IterateFoundationNod(FoundationContext context, bool cleanStale = false)
        {
            // PASS 1 — Read-only scan
            List<HandleEntry> results = ScanFoundationNod(context);

            // PASS 2 — Explicit cleanup (only if requested)
            if (cleanStale && results.Count > 0)
            {
                CleanupFoundationNod(context, results);

                // Rescan so results reflect cleaned NOD
                results = ScanFoundationNod(context);
            }

            return results;
        }

        // ==========================================================
        //  CLEAN STALE HANDLES
        // ==========================================================
        public static void CleanFoundationNOD(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var model = context.Model;
            var db = doc.Database;

            var entries = IterateFoundationNod(context, cleanStale: true);
            CleanupFoundationNod(context, entries);
        }

        // ==========================================================
        //  REMOVE ENTIRE BtnQueryNOD_Click STRUCTURE
        // ==========================================================



        /// <summary>
        /// Recursively exports a DBDictionary, serializing Entities, XRecords, and subdictionaries.
        /// </summary>
        internal static Dictionary<string, object> ToDictionaryRepresentation(DBDictionary dict, Transaction tr)
        {
            var result = new Dictionary<string, object>();

            foreach (DBDictionaryEntry entry in dict)
            {
                DBObject obj = tr.GetObject(entry.Value, OpenMode.ForRead);

                if (obj is DBDictionary subDict)
                {
                    // Recurse into subdictionary
                    result[entry.Key] = ToDictionaryRepresentation(subDict, tr);
                }
                else if (obj is Entity ent)
                {
                    // Leaf entity (e.g., FD_CENTERLINE)
                    result[entry.Key] = new Dictionary<string, string>
            {
                { "Type", ent.GetType().Name }, // e.g., "Polyline"
                { "Handle", ent.Handle.ToString().ToUpperInvariant() }
            };
                }
                else if (obj is Xrecord xr)
                {
                    object[] data = new object[0];
                    if (xr.Data != null)
                    {
                        data = xr.Data.Cast<TypedValue>().Select(v => v.Value?.ToString() ?? "").ToArray();
                    }

                    result[entry.Key] = new Dictionary<string, object>
                    {
                        { "Type", "XRecord" },
                        { "Data", data }
                    };
                }
                else
                {
                    // Unknown object type, just store type name
                    result[entry.Key] = new Dictionary<string, string>
            {
                { "Type", obj.GetType().Name }
            };
                }
            }

            return result;
        }

        // ==========================================================
        //  GET SUBDICTIONARY
        // ==========================================================
        /// <summary>
        /// Utility function that return all handle_strings of subdictionary of a specified name.
        /// </summary>
        /// <param name="tr"></param>
        /// <param name="name"></param>
        /// <param name="createIfMissing"></param>
        /// <returns></returns>
        // --------------------------------------------------------
        //  Get all handle strings from a subdictionary
        // --------------------------------------------------------
        // ==========================================================
        //  HELPER UTILITIES
        // ==========================================================

        /// <summary>
        /// Ensures a subdictionary exists under another subdictionary (nested).
        /// </summary>


        internal static DBDictionary GetOrCreateNestedSubDictionary(Transaction tr, DBDictionary root, params string[] keys)
        {
            if (tr == null || root == null || keys == null || keys.Length == 0)
                return null;

            DBDictionary current = root;

            foreach (string key in keys)
            {
                if (!current.Contains(key))
                {
                    var sub = new DBDictionary();
                    current.SetAt(key, sub);
                    tr.AddNewlyCreatedDBObject(sub, true);
                    current = sub;
                }
                else
                {
                    current = (DBDictionary)tr.GetObject(current.GetAt(key), OpenMode.ForWrite);
                }
            }

            return current;
        }



        internal static Xrecord GetOrCreateMetadataXrecord(
            Transaction tr,
            DBDictionary parent,
            string key)
        {
            if (tr == null || parent == null || string.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException();

            // Create if missing
            if (!parent.Contains(key))
            {
                Xrecord newRecord = new Xrecord();
                parent.SetAt(key, newRecord);
                tr.AddNewlyCreatedDBObject(newRecord, true);
                return newRecord;
            }

            ObjectId id = parent.GetAt(key);
            DBObject obj = tr.GetObject(id, OpenMode.ForWrite);

            // Correct type → return
            Xrecord existingRecord = obj as Xrecord;
            if (existingRecord != null)
                return existingRecord;

            // Wrong type → repair
            obj.Erase();

            Xrecord repairedRecord = new Xrecord();
            parent.SetAt(key, repairedRecord);
            tr.AddNewlyCreatedDBObject(repairedRecord, true);

            return repairedRecord;
        }

        // --------------------------------------------------------
        //  Generic helper to get or create a subdictionary under "EE_Foundation"
        // --------------------------------------------------------


        /// <summary>
        /// Adds a handle as an Xrecord to a dictionary if it doesn't exist.
        /// </summary>
        internal static void AddHandleToMetadataDictionary(Transaction tr, DBDictionary dict, string handle)
        {
            if (!dict.Contains(handle))
            {
                Xrecord xr = new Xrecord
                {
                    Data = new ResultBuffer(new TypedValue((int)DxfCode.Handle, handle))
                };
                dict.SetAt(handle, xr);
                tr.AddNewlyCreatedDBObject(xr, true);
            }
        }

        /// <summary>
        /// Removes all entries from a subdictionary under EE_Foundation
        /// without deleting the subdictionary itself.
        /// </summary>
        internal static bool ClearFoundationSubDictionaryInternal(
            Transaction tr,
            Database db,
            string subDictName)
        {
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (string.IsNullOrWhiteSpace(subDictName))
                throw new ArgumentException("Subdictionary name required.", nameof(subDictName));

            // Normalize name
            subDictName = subDictName.Trim().ToUpperInvariant();

            // Open the top-level NOD
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

            // Get the EE_Foundation root dictionary
            var root = NODCore.GetOrCreateNestedSubDictionary(tr, nod, NODCore.ROOT);
            if (root == null)
                return false;

            // Get the requested subdictionary (no creation, just read/write if exists)
            DBDictionary subDict;
            try
            {
                subDict = (DBDictionary)tr.GetObject(root.GetAt(subDictName), OpenMode.ForWrite);
            }
            catch
            {
                // Subdictionary does not exist
                return false;
            }

            // Collect keys first (cannot modify while iterating)
            var keys = new List<string>();
            foreach (DBDictionaryEntry entry in subDict)
                keys.Add(entry.Key);

            // Remove each entry
            foreach (string key in keys)
            {
                try
                {
                    var obj = tr.GetObject(subDict.GetAt(key), OpenMode.ForWrite);
                    obj.Erase();
                }
                catch
                {
                    // Ignore individual failures
                }
            }

            return true;
        }


        /// <summary>
        /// Returns all valid, non-erased ObjectIds from handle strings stored in a sub-dictionary.
        /// Invalid handle_strings, erased objects, or stale references are ignored.
        /// </summary>
        /// <param name="tr">Active AutoCAD transaction for object validation.</param>
        /// <param name="db">Database in which handle_strings are resolved.</param>
        /// <param name="subDict">Sub-dictionary containing handle strings; null returns empty list.</param>
        /// <returns>List of valid, readable ObjectIds.</returns>

        internal static List<ObjectId> GetAllValidObjectIdsFromSubDictionary(FoundationContext context,
            Transaction tr, Database db, DBDictionary subDict)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var validIds = new List<ObjectId>();

            // Missing sub-dictionary is not an error; return empty result
            if (subDict == null)
                return validIds;

            foreach (DBDictionaryEntry entry in subDict)
            {
                // Attempt to resolve the dictionary key into an ObjectId
                if (!TryGetObjectIdFromHandleString(context, db, entry.Key, out ObjectId id))
                    continue;

                // Verify the ObjectId can be opened and is not erased
                if (IsValidReadableObject(tr, id))
                    validIds.Add(id);
            }

            return validIds;
        }

        /// <summary>
        /// Attempts to resolve a handle string into a valid ObjectId in the database.
        /// Supports hex (with or without "0x") and decimal formats; no transaction required.
        /// </summary>
        /// <param name="db">Database in which to resolve the handle.</param>
        /// <param name="handleStr">String representation of the handle.</param>
        /// <param name="id">Receives the resolved ObjectId, or ObjectId.Null if unsuccessful.</param>
        /// <returns>True if parsing and resolution succeed; otherwise false.</returns>
        internal static bool TryGetObjectIdFromHandleString(FoundationContext context,
            Database db, string handleStr, out ObjectId id)
        {
            id = ObjectId.Null;

            // Parse the string into a Handle structure
            if (!TryParseHandle(context, handleStr, out Handle handle))
                return false;

            // Resolve the Handle into an ObjectId
            return TryGetObjectIdFromHandle(db, handle, out id);
        }

        /// <summary>
        /// Attempts to parse a string into an AutoCAD Handle, trying hex first, then decimal as fallback.
        /// </summary>
        /// <param name="handleString">String representation of the handle.</param>
        /// <param name="handle">Receives the parsed Handle if successful.</param>
        /// <returns>True if parsing succeeds; otherwise false.</returns>
        internal static bool TryParseHandle(FoundationContext context, string handleString, out Handle handle)
        {
            handle = default;

            if (string.IsNullOrWhiteSpace(handleString))
                return false;

            string s = handleString.Trim();

            // Remove optional "0x" prefix if present
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(2);

            // 1) Try hexadecimal parsing (canonical AutoCAD format)
            if (long.TryParse(
                    s,
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out long hexValue))
            {
                handle = new Handle(hexValue);
                return true;
            }

            // 2) Defensive fallback: decimal parsing
            if (long.TryParse(
                    s,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out long decValue))
            {
                handle = new Handle(decValue);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if an ObjectId is valid, readable, and not erased within a transaction.
        /// </summary>
        /// <param name="tr">Active AutoCAD transaction.</param>
        /// <param name="id">ObjectId to validate.</param>
        /// <returns>True if valid and readable; otherwise false.</returns>
        internal static bool IsValidReadableObject(Transaction tr, ObjectId id)
        {
            // Quick structural checks
            if (id.IsNull || !id.IsValid)
                return false;

            try
            {
                // Attempt to open the object for read
                var obj = tr.GetObject(id, OpenMode.ForRead, false);
                return obj != null && !obj.IsErased;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // Covers stale ObjectIds, wrong database, or invalid access
                return false;
            }
        }

        /// <summary>
        /// Resolves an AutoCAD Handle to a valid ObjectId in the database.
        /// </summary>
        /// <param name="db">Database in which to resolve the handle.</param>
        /// <param name="handle">Handle to resolve.</param>
        /// <param name="id">Receives the resolved ObjectId if successful.</param>
        /// <returns>True if resolution succeeds; otherwise false.</returns>
        internal static bool TryGetObjectIdFromHandle(
            Database db, Handle handle, out ObjectId id)
        {
            id = ObjectId.Null;

            try
            {
                id = db.GetObjectId(false, handle, 0);
                return id.IsValid && !id.IsNull;
            }
            catch
            {
                id = ObjectId.Null;
                return false;
            }
        }

        /// <summary>
        /// Retrieves the first valid, readable entity from a named sub-dictionary.
        /// </summary>
        /// <param name="tr">Active AutoCAD transaction.</param>
        /// <param name="db">Database containing the sub-dictionary.</param>
        /// <param name="subDictKey">Key identifying the sub-dictionary.</param>
        /// <param name="oid">Receives the ObjectId of the first valid entity if found.</param>
        /// <returns>True if a valid entity is found; otherwise false.</returns>
        public static bool TryGetFirstEntity(
            FoundationContext context,
            Transaction tr,
            Database db,
            string subDictKey,
            out ObjectId oid)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (string.IsNullOrWhiteSpace(subDictKey))
            {
                oid = ObjectId.Null;
                return false;
            }

            oid = ObjectId.Null;

            // Get the subdictionary under the root
            var rootDict = NODCore.GetOrCreateNestedSubDictionary(
                tr,
                (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite),
                NODCore.ROOT
            );

            if (rootDict == null || !rootDict.Contains(subDictKey))
                return false;

            var subDict = (DBDictionary)tr.GetObject(rootDict.GetAt(subDictKey), OpenMode.ForRead);
            if (subDict == null || subDict.Count == 0)
                return false;

            // Take the first valid ObjectId
            foreach (DBDictionaryEntry entry in subDict)
            {
                if (TryGetObjectIdFromHandleString(context, db, entry.Key, out oid)
                    && IsValidReadableObject(tr, oid))
                {
                    return true;
                }

                break; // Only check the first entry
            }

            return false;
        }


        // ==========================================================
        // REMOVE SPECIFIC HANDLE (Dynamic Sub-Dictionary)
        // Using user entered handles.  
        // ==========================================================
        internal static DBDictionary GetFoundationRoot(FoundationContext context, Transaction tr)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));

            var doc = context.Document;
            var model = context.Model;
            var db = doc.Database;

            var nod = (DBDictionary)tr.GetObject(
                db.NamedObjectsDictionaryId,
                OpenMode.ForRead);

            if (!nod.Contains(NODCore.ROOT))
                return null;

            return (DBDictionary)tr.GetObject(
                nod.GetAt(NODCore.ROOT),
                OpenMode.ForRead);
        }

        /// <summary>
        /// Deletes all entities referenced by a foundation subdictionary.
        /// Optionally removes the handle records from the dictionary as well.
        /// </summary>
        internal static int DeleteEntitiesInSubDictionary(
            FoundationContext context,
            Transaction tr,
            Database db,
            string subDictName,
            bool removeHandlesFromNod = true)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (string.IsNullOrWhiteSpace(subDictName))
                throw new ArgumentException("Subdictionary name required.", nameof(subDictName));

            int deletedCount = 0;

            // Get the subdictionary using the nested helper
            DBDictionary subDict = NODCore.GetOrCreateNestedSubDictionary(
                tr,
                (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite),
                NODCore.ROOT,
                subDictName
            );

            if (subDict == null || subDict.Count == 0)
                return 0;

            // Collect keys first (handles) to avoid modifying dictionary while iterating
            var handles = new List<string>();
            foreach (DictionaryEntry entry in subDict)
            {
                if (entry.Key is string key)
                    handles.Add(key);
            }
            foreach (string handleStr in handles)
            {
                if (!TryGetObjectIdFromHandleString(context, db, handleStr, out ObjectId id))
                    continue;

                if (!IsValidReadableObject(tr, id))
                    continue;

                try
                {
                    // Erase the entity
                    if (id.IsValid && !id.IsErased)
                    {
                        Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                        ent?.Erase();
                        deletedCount++;
                    }

                    // Optionally remove the Xrecord / handle from the NOD
                    if (removeHandlesFromNod && subDict.Contains(handleStr))
                    {
                        DBObject obj = tr.GetObject(subDict.GetAt(handleStr), OpenMode.ForWrite);
                        obj?.Erase();
                    }
                }
                catch
                {
                    // Ignore individual failures
                }
            }

            return deletedCount;
        }



        internal static int DeleteEntitiesFromFoundationSubDictionary(FoundationContext context,
            Database db,
            string subDictName,
            bool removeHandlesFromNod = true)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            Document doc = context.Document;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                int count = DeleteEntitiesInSubDictionary(
                    context,
                    tr,
                    db,
                    subDictName,
                    removeHandlesFromNod);

                tr.Commit();
                return count;
            }
        }
    }
}