// NOD RULES:
// 1) Never iterate DBDictionary directly
// 2) Always use EnumerateDictionary / EnumerateHandleKeys
// 3) Never assume ObjectId validity
// 4) All writes require DocumentLock

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using static FoundationDetailsLibraryAutoCAD.AutoCAD.NOD.HandleHandler;

[assembly: CommandClass(typeof(FoundationDetailsLibraryAutoCAD.AutoCAD.NOD.NODCore))]

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.NOD
{
    public partial class NODCore
    {
        #region Constants and Configuration
        // ==========================================================
        //  CONSTANTS
        // ==========================================================
        public const string ROOT = "EE_Foundation";

        public const string KEY_BOUNDARY_SUBDICT = "FD_BOUNDARY";
        public const string KEY_GRADEBEAM_SUBDICT = "FD_GRADEBEAM";
        //public const string KEY_SLABSTRAND_SUBDICT = "FD_SLABSTRAND";
        //public const string KEY_REBAR = "FD_REBAR";

        public const string KEY_CENTERLINE = "FD_CENTERLINE";
        public const string KEY_EDGES_SUBDICT = "FD_EDGES";
        public const string KEY_BEAMSTRAND_SUBDICT = "FD_BEAMSTRAND";
        public const string KEY_METADATA_SUBDICT = "FD_METADATA";

        public static readonly string[] KNOWN_ROOT_SUBDIRS =
        {
            KEY_BOUNDARY_SUBDICT,
            KEY_GRADEBEAM_SUBDICT,
            //KEY_SLABSTRAND_SUBDICT,
            //KEY_REBAR
        };
        #endregion


        #region Enumeration Helpers
        internal static IEnumerable<(string Key, ObjectId Id)> EnumerateDictionary(DBDictionary dict)
        {
            if (dict == null)
                yield break;

            foreach (DictionaryEntry entry in dict)
            {
                if (entry.Key is string key &&
                    entry.Value is ObjectId id &&
                    id.IsValid && !id.IsNull)
                {
                    yield return (key, id);
                }
            }
        }
        internal static IEnumerable<ExtensionDataItem> EnumerateDictionaryWithHandles(
            FoundationContext context,
            Transaction tr,
            DBDictionary dict,
            Database db)
        {
            if (context == null || tr == null || dict == null || db == null)
                yield break;

            foreach (DictionaryEntry entry in dict)
            {
                if (!(entry.Key is string key))
                    continue;

                if (!(entry.Value is ObjectId objId))
                    continue; // skip non-ObjectId entries

                var obj = tr.GetObject(objId, OpenMode.ForRead);
                // Subdictionary → recurse
                if (obj is DBDictionary subDict)
                {
                    yield return new ExtensionDataItem
                    {
                        Name = key,
                        Type = "Subdictionary",
                        Children = new ObservableCollection<ExtensionDataItem>(
                            EnumerateDictionaryWithHandles(context, tr, subDict, db))
                    };
                }
                // Xrecord → show typed values
                else if (obj is Xrecord xr)
                {
                    var xrValues = new List<string>();
                    if (xr.Data != null)
                    {
                        foreach (TypedValue tv in xr.Data)
                            xrValues.Add($"[{tv.TypeCode}: {tv.Value}]");
                    }

                    yield return new ExtensionDataItem
                    {
                        Name = key,
                        Type = "XRecord",
                        Value = xrValues
                    };
                }
                // Entity → show handle
                else if (obj is Entity ent)
                {
                    yield return new ExtensionDataItem
                    {
                        Name = key,
                        Type = ent.GetType().Name,
                        ObjectId = ent.ObjectId,
                        Value = new List<string> { $"Handle: {ent.Handle}" }
                    };
                }
                // Fallback for unknown objects
                else
                {
                    yield return new ExtensionDataItem
                    {
                        Name = key,
                        Type = obj?.GetType().Name ?? "Unknown",
                        ObjectId = ObjectId.Null
                    };
                }
            }
        }



        internal static IEnumerable<ObjectId> EnumerateValidEntityIds(
            FoundationContext context,
            Transaction tr,
            Database db,
            DBDictionary dict)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (dict == null) yield break;

            foreach (var id in NODScanner.ProcessDictionaryEntries(
                context, tr, db, dict, "EnumerateValidEntityIds",
                (handleKey, _) =>
                {
                    var entry = ValidateHandleOrId(context, tr, db, "EnumerateValidEntityIds", handleKey);
                    return entry.Status == HandleStatus.Valid ? entry.Id : ObjectId.Null;
                },
                recurseSubDictionaries: true))
            {
                if (!id.IsNull)
                    yield return id;
            }
        }

        #endregion

        #region Validation

        /// <summary>
        /// Checks if a string is a valid hexadecimal handle.
        /// </summary>
        private static bool IsValidHexHandle(string handle)
        {
            if (string.IsNullOrWhiteSpace(handle))
                return false;

            return handle.All(c =>
                (c >= '0' && c <= '9') ||
                (c >= 'A' && c <= 'F') ||
                (c >= 'a' && c <= 'f'));
        }

        /// <summary>
        /// Tries to resolve a handle string to an ObjectId.
        /// </summary>
        private static bool TryResolveHandleToObjectId(
            FoundationContext context,
            Database db,
            string handleStr,
            out ObjectId id)
        {
            id = ObjectId.Null;

            if (!IsValidHexHandle(handleStr))
                return false;

            try
            {
                var handle = new Handle(Convert.ToInt64(handleStr, 16));
                id = db.GetObjectId(false, handle, 0);
                return id.IsValid && !id.IsNull;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Unified validation for either a handle string or an ObjectId.
        /// Returns a <see cref="HandleEntry"/> with status indicating validity, missing, or invalid.
        /// </summary>
        internal static HandleEntry ValidateHandleOrId(
            FoundationContext context,
            Transaction tr,
            Database db,
            string groupName,
            string handleStr,
            ObjectId id = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var result = new HandleEntry
            {
                GroupName = groupName,
                HandleKey = handleStr ?? string.Empty,
                Id = id
            };

            // Resolve handle string to ObjectId if provided
            if (!string.IsNullOrEmpty(handleStr))
            {
                if (!TryResolveHandleToObjectId(context, db, handleStr, out ObjectId resolved))
                {
                    result.Status = HandleStatus.Invalid;
                    return result;
                }
                result.Id = resolved;
            }

            // Check for valid ObjectId
            if (result.Id.IsNull || !result.Id.IsValid)
            {
                result.Status = HandleStatus.Invalid;
                return result;
            }

            try
            {
                // Attempt to fetch the entity
                var obj = tr.GetObject(result.Id, OpenMode.ForRead, false);
                if (obj == null || obj.IsErased)
                {
                    result.Status = HandleStatus.Missing;
                    return result;
                }

                // If it's an Entity, store it
                if (obj is Entity ent)
                    result.Entity = ent;

                result.Status = HandleStatus.Valid;
            }
            catch
            {
                result.Status = HandleStatus.Missing;
                result.Entity = null;
            }

            return result;
        }


        #endregion


        #region Handle Resolution
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

        #endregion

        #region NOD Structure Creation and Retrieval

        public static DBDictionary InitFoundationNOD(FoundationContext context, Transaction tr)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));

            var db = context.Document.Database;

            var nod = (DBDictionary)tr.GetObject(
                db.NamedObjectsDictionaryId, OpenMode.ForWrite);

            var root = GetOrCreateNestedSubDictionary(tr, nod, ROOT);

            foreach (var key in KNOWN_ROOT_SUBDIRS)
                GetOrCreateNestedSubDictionary(tr, root, key);

            return root;
        }

        internal static DBDictionary GetFoundationRoot(Transaction tr, Database db)
        {
            var nod = tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead) as DBDictionary;
            if (nod == null || !nod.Contains(ROOT))
                return null;

            return tr.GetObject(nod.GetAt(ROOT), OpenMode.ForRead) as DBDictionary;
        }

        internal static DBDictionary GetFoundationRootDictionary(
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

        /// <summary>
        /// Ensures a subdictionary exists under another subdictionary (nested).
        /// </summary>
        internal static DBDictionary GetOrCreateNestedSubDictionary(
            Transaction tr,
            DBDictionary root,
            params string[] keys)
        {
            if (tr == null || root == null || keys == null || keys.Length == 0)
                return null;

            var current = root;

            foreach (var key in keys)
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

        internal static DBDictionary GetOrCreateGradeBeamNode(
            Transaction tr,
            DBDictionary gradeBeamRoot,
            string centerlineHandle)
        {
            var gbNode = GetOrCreateNestedSubDictionary(
                tr, gradeBeamRoot, centerlineHandle);

            var cl = GetOrCreateNestedSubDictionary(tr, gbNode, KEY_CENTERLINE);
            var edges = GetOrCreateNestedSubDictionary(tr, gbNode, KEY_EDGES_SUBDICT);
            var strands = GetOrCreateNestedSubDictionary(tr, gbNode, KEY_BEAMSTRAND_SUBDICT);

            AddHandleToMetadataDictionary(tr, cl, centerlineHandle);

            return gbNode;
        }

        internal static void AddHandleToMetadataDictionary(
    Transaction tr,
    DBDictionary dict,
    string handle)
        {
            if (!dict.Contains(handle))
            {
                var xr = new Xrecord
                {
                    Data = new ResultBuffer(
                        new TypedValue((int)DxfCode.Handle, handle))
                };
                dict.SetAt(handle, xr);
                tr.AddNewlyCreatedDBObject(xr, true);
            }
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



        #endregion



        #region Cleanup and Mutation

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
            var keys = EnumerateDictionary(subDict)
                       .Select(e => e.Key)
                       .ToList();

            foreach (string key in keys)
            {
                try
                {
                    var obj = tr.GetObject(subDict.GetAt(key), OpenMode.ForWrite);
                    obj.Erase();
                }
                catch { }
            }

            return true;
        }


        #endregion

        #region Entity Deletion
        internal static int DeleteEntitiesFromDictionaryCore(
    FoundationContext context,
    Transaction tr,
    Database db,
    DBDictionary dict,
    string contextName,
    bool recursive,
    bool removeHandles)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (dict == null) return 0;

            int count = 0;

            // Snapshot keys to avoid mutation issues
            var entries = EnumerateDictionary(dict).ToList();

            foreach (var (key, id) in entries)
            {
                DBObject obj;
                try
                {
                    obj = tr.GetObject(id, OpenMode.ForRead);
                }
                catch
                {
                    continue;
                }

                // Recurse if requested
                if (recursive && obj is DBDictionary subDict)
                {
                    count += DeleteEntitiesFromDictionaryCore(
                        context,
                        tr,
                        db,
                        subDict,
                        contextName,
                        recursive,
                        removeHandles);
                    continue;
                }

                // Validate handle
                HandleEntry entry = ValidateHandleOrId(context, tr, db, contextName, key);
                if (entry.Status != HandleStatus.Valid)
                    continue;

                try
                {
                    if (entry.Id.IsValid && !entry.Id.IsErased)
                    {
                        Entity ent = tr.GetObject(entry.Id, OpenMode.ForWrite) as Entity;
                        ent?.Erase();
                        count++;
                    }

                    if (removeHandles && dict.Contains(key))
                    {
                        DBObject handleObj = tr.GetObject(dict.GetAt(key), OpenMode.ForWrite);
                        handleObj?.Erase();
                    }
                }
                catch
                {
                    // intentionally ignore individual failures
                }
            }

            return count;
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
            if (string.IsNullOrWhiteSpace(subDictName))
                throw new ArgumentException("Subdictionary name required.", nameof(subDictName));

            DBDictionary root = (DBDictionary)tr.GetObject(
                db.NamedObjectsDictionaryId,
                OpenMode.ForWrite);

            DBDictionary subDict = NODCore.GetOrCreateNestedSubDictionary(
                tr,
                root,
                NODCore.ROOT,
                subDictName);

            if (subDict == null || subDict.Count == 0)
                return 0;

            return DeleteEntitiesFromDictionaryCore(
                context,
                tr,
                db,
                subDict,
                subDictName,
                recursive: false,
                removeHandles: removeHandlesFromNod);
        }


        internal static int DeleteEntitiesFromFoundationSubDictionary(
            FoundationContext context,
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


        #endregion

        #region High-Level Operations
        public static List<HandleEntry> IterateFoundationNod(FoundationContext context, bool cleanStale = false)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            // PASS 1 — Read-only scan
            var results = NODScanner.ScanFoundationNod(context);

            // PASS 2 — Cleanup if requested
            if (cleanStale && results.Count > 0)
            {
                CleanupFoundationNod(context, results);

                // Rescan so results reflect cleaned NOD
                results = NODScanner.ScanFoundationNod(context);
            }

            return results;
        }

        public static void CleanFoundationNOD(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            // Iterate with cleanup enabled — no need to call Cleanup again manually
            IterateFoundationNod(context, cleanStale: true);
        }


        #endregion

        #region Serialization

        /// <summary>
        /// Recursively exports a DBDictionary, serializing Entities, XRecords, and subdictionaries.
        /// </summary>
        internal static Dictionary<string, object> ToDictionaryRepresentation(DBDictionary dict, Transaction tr)
        {
            var result = new Dictionary<string, object>();

            foreach (DBDictionaryEntry entry in dict)
            {
                DBObject obj;
                try
                {
                    obj = tr.GetObject(entry.Value, OpenMode.ForRead);
                }
                catch
                {
                    continue; // skip unreadable objects
                }

                if (obj is DBDictionary subDict)
                {
                    // Always mark type as "Dictionary" for nested dictionaries
                    result[entry.Key] = new Dictionary<string, object>
            {
                { "Type", "Dictionary" },
                { "Children", ToDictionaryRepresentation(subDict, tr) } // recurse
            };
                }
                else if (obj is Xrecord xr)
                {
                    var dataList = new List<string>();
                    if (xr.Data != null)
                    {
                        foreach (TypedValue tv in xr.Data)
                            dataList.Add(tv.Value?.ToString());
                    }

                    result[entry.Key] = new Dictionary<string, object>
            {
                { "Type", "XRecord" },
                { "Data", dataList }
            };
                }
                else if (obj is Entity ent)
                {
                    result[entry.Key] = new Dictionary<string, object>
            {
                { "Type", "Entity" },
                { "Handle", ent.Handle.ToString() }
            };
                }
                else
                {
                    // fallback for unexpected object types
                    result[entry.Key] = new Dictionary<string, object>
            {
                { "Type", obj != null ? obj.GetType().Name : "Unknown" }
            };
                }
            }

            return result;
        }



        #endregion
    }
}