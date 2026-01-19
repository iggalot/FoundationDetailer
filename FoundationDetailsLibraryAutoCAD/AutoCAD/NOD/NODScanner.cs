//results.AddRange(ProcessDictionaryEntries(
//    context,
//    tr,
//    db,
//    subDict,
//    groupName,
//    (handleKey, id) => NODCore.ValidateHandleOrId(context, tr, db, groupName, handleKey),
//    false
//));

using Autodesk.AutoCAD.DatabaseServices;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using static FoundationDetailsLibraryAutoCAD.AutoCAD.NOD.HandleHandler;
using static FoundationDetailsLibraryAutoCAD.Data.FoundationEntityData;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.NOD
{
    internal class NODScanner
    {
        #region  Scan and Query Operations
        /// <summary>
        /// Scans the entire foundation NOD and returns all handle entries.
        /// </summary>
        public static List<HandleEntry> ScanFoundationNod(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var db = context.Document?.Database;
            if (db == null) return new List<HandleEntry>();

            var results = new List<HandleEntry>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    var rootDict = NODCore.GetFoundationRootDictionary(tr, db);
                    if (rootDict == null) return results;

                    foreach (var kvp in EnumerateDictionary(rootDict))
                    {
                        string groupName = kvp.Key;
                        var subDict = tr.GetObject(kvp.Value, OpenMode.ForRead) as DBDictionary;

                        results.AddRange(ProcessDictionaryEntries(
                            context,
                            tr,
                            db,
                            subDict,
                            groupName,
                            (handleKey, id) => NODCore.ValidateHandleOrId(context, tr, db, groupName, handleKey),
                            true
                        ));
                    }

                    tr.Commit();
                }
                catch (Exception ex)
                {
                    context.Document?.Editor.WriteMessage($"\n[EE_FOUNDATION] Scan failed: {ex.Message}");
                }
            }
            return results;
        }

        /// <summary>
        /// Recursively scans a dictionary and all sub-dictionaries for a target handle string.
        /// </summary>
        internal static bool ContainsHandle(FoundationContext context, Transaction tr, DBDictionary dict, string targetHandle)
        {
            foreach (var found in ProcessDictionaryEntries(
                context,
                tr,
                null,
                dict,
                null,
                (handleKey, objId) =>
                {
                    var obj = tr.GetObject(objId, OpenMode.ForRead);
                    if (obj is Xrecord xrec && xrec.Data != null)
                    {
                        foreach (TypedValue tv in xrec.Data)
                        {
                            if (tv.TypeCode == (int)DxfCode.Text &&
                                tv.Value is string s &&
                                s.Equals(targetHandle, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                    return false;
                },
                true // recurse
            ))
            {
                if (found is bool b && b) return true;
            }

            return false;
        }


        /// <summary>
        /// Enumerates a DBDictionary as key-value pairs.
        /// C# 7.3 compatible (no tuple deconstruction).
        /// </summary>
        internal static IEnumerable<KeyValuePair<string, ObjectId>> EnumerateDictionary(DBDictionary dict)
        {
            foreach (DBDictionaryEntry entry in dict)
            {
                yield return new KeyValuePair<string, ObjectId>(entry.Key, entry.Value);
            }
        }

        /// <summary>
        /// Equivalent of TryGetFirstEntity
        /// </summary>
        public static bool TryGetFirstEntity(
            FoundationContext context,
            Transaction tr,
            Database db,
            string subDictKey,
            out ObjectId oid)
        {
            oid = ObjectId.Null;
            if (string.IsNullOrWhiteSpace(subDictKey))
                return false;

            // Get the root dictionary
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

            // Iterate dictionary and return the first valid entity
            foreach (var kvp in EnumerateDictionary(subDict))
            {
                HandleEntry entry = NODCore.ValidateHandleOrId(context, tr, db, subDictKey, kvp.Key);

                if (entry.Status == HandleStatus.Valid)
                {
                    oid = entry.Id;
                    return true;
                }
            }

            oid = ObjectId.Null;
            return false;
        }


        /// <summary>
        /// Enumerates all entries in a <see cref="DBDictionary"/> and applies a handler function to each,
        /// returning a sequence of results of type <typeparamref name="TResult"/>.  
        /// Optionally, recursively processes nested sub-dictionaries.
        /// </summary>
        /// <typeparam name="TResult">The type returned by <paramref name="entryHandler"/> for each entry.</typeparam>
        /// <param name="context">The <see cref="FoundationContext"/> (cannot be null).</param>
        /// <param name="tr">The active <see cref="Transaction"/> for safely reading objects.</param>
        /// <param name="db">The <see cref="Database"/> containing the dictionary, passed to the handler.</param>
        /// <param name="dict">The <see cref="DBDictionary"/> to process. Null returns an empty sequence.</param>
        /// <param name="groupName">Optional tag for grouping entries, passed to the handler.</param>
        /// <param name="entryHandler">Function that takes a key and <see cref="ObjectId"/> and returns <typeparamref name="TResult"/>.</param>
        /// <param name="recurseSubDictionaries">If true, nested dictionaries are recursively processed; otherwise skipped.</param>
        /// <returns>An <see cref="IEnumerable{TResult}"/> of results from the handler.</returns>
        /// <remarks>
        /// General-purpose helper for processing entries in AutoCAD's Named Objects Dictionary (NOD) or any sub-dictionary.  
        /// Non-dictionary entries are passed to the handler. Nested dictionaries are processed if <paramref name="recurseSubDictionaries"/> is true.
        /// </remarks>
        internal static ObservableCollection<ExtensionDataItem> ProcessDictionary(
            FoundationContext context,
            Transaction tr,
            DBDictionary dict,
            Database db)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (dict == null) throw new ArgumentNullException(nameof(dict));
            if (db == null) throw new ArgumentNullException(nameof(db));

            var items = new ObservableCollection<ExtensionDataItem>();

            foreach (var kvp in NODCore.EnumerateDictionary(dict))
            {
                string key = kvp.Key;
                ObjectId id = kvp.Id;

                DBObject obj = null;
                try
                {
                    obj = tr.GetObject(id, OpenMode.ForRead);
                }
                catch
                {
                    items.Add(new ExtensionDataItem
                    {
                        Name = key,
                        Type = "Unreadable"
                    });
                    continue;
                }

                // ---- SUBDICTIONARY ----
                if (obj is DBDictionary subDict)
                {
                    items.Add(new ExtensionDataItem
                    {
                        Name = key,
                        Type = "Subdictionary",
                        Children = ProcessDictionary(context, tr, subDict, db)
                    });
                }
                // ---- XRECORD storing a single handle ----
                else if (obj is Xrecord xr)
                {
                    // Each Xrecord is treated as a single handle entry
                    string handle = xr.Data?.AsArray().Length > 0 ? xr.Data.AsArray()[0].Value?.ToString() : "";

                    items.Add(new ExtensionDataItem
                    {
                        Name = key,
                        Type = "XRecord",
                        Value = new List<string> { handle }
                    });
                }
                // ---- ENTITY ----
                else if (obj is Entity ent)
                {
                    var entry = NODCore.ValidateHandleOrId(context, tr, db, "ProcessDictionary", key);

                    items.Add(new ExtensionDataItem
                    {
                        Name = key,
                        Type = ent.GetType().Name,
                        ObjectId = entry.Status == HandleStatus.Valid ? entry.Id : ObjectId.Null
                    });
                }
                // ---- FALLBACK ----
                else
                {
                    items.Add(new ExtensionDataItem
                    {
                        Name = key,
                        Type = obj?.GetType().Name ?? "Unknown",
                        ObjectId = ObjectId.Null
                    });
                }
            }

            return items;
        }




        /// <summary>
        /// Iterates over all entries in a DBDictionary and applies a handler function to each entry,
        /// returning a sequence of results. Can recursively process nested dictionaries.
        /// </summary>
        internal static IEnumerable<TResult> ProcessDictionaryEntries<TResult>(
            FoundationContext context,
            Transaction tr,
            Database db,
            DBDictionary dict,
            string groupName,
            Func<string, ObjectId, TResult> entryHandler,
            bool recurseSubDictionaries = false)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (dict == null) yield break;

            foreach (var kvp in EnumerateDictionary(dict))
            {
                ObjectId objId = kvp.Value;

                DBObject obj;
                try
                {
                    obj = tr.GetObject(objId, OpenMode.ForRead);
                }
                catch
                {
                    // skip unreadable objects
                    continue;
                }

                if (obj is DBDictionary subDict)
                {
                    if (recurseSubDictionaries)
                    {
                        foreach (var nested in ProcessDictionaryEntries(context, tr, db, subDict, groupName, entryHandler, true))
                        {
                            yield return nested;
                        }
                    }
                }
                else
                {
                    yield return entryHandler(kvp.Key, objId);
                }
            }
        }




        #endregion
    }
}
