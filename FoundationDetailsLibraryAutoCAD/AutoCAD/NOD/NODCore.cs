// NOD RULES:
// current NOD structure should be this.
//NOD
//└─ EE_Foundation
//   │
//   ├─ FD_GRADEBEAM
//   │   │
//   │   ├─ 2A31
//   │   │   ├─ EDGES
//   │   │   │   ├─ e1
//   │   │   │   └─ e2
//   │   │   │
//   │   │   └─ METADATA
//   │   │       └─ SECTION
//   │   │           ├─ Width
//   │   │           └─ Depth
//   │   │
//   │   └─ <additional grade beam -- same structure>
//   │
//   └─ FD_BOUNDARY
//   │   │
//   │   └─ 3B11
//   │       ├─ EDGES
//   │       └─ METADATA
//   │       
//   └─ FD_SLABSTRAND
//   │   
//   └─ FD_REBAR

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

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

        // Primary subdicts under the ROOT ofthe NOD
        public const string KEY_BOUNDARY_SUBDICT = "FD_BOUNDARY";
        public const string KEY_GRADEBEAM_SUBDICT = "FD_GRADEBEAM";
        public const string KEY_SLABSTRAND_SUBDICT = "FD_SLABSTRAND";
        public const string KEY_REBAR_SUBDICT = "FD_REBAR";

        // The gradebam NODE subdicts -- 
        public const string KEY_EDGES_SUBDICT = "FD_EDGES";
        public const string KEY_BEAMSTRAND_SUBDICT = "FD_BEAMSTRAND";
        public const string KEY_METADATA_SUBDICT = "FD_METADATA";

        // Items under the SECTION subdict of the meta subdict
        public const string KEY_META_SECTION_SUBDICT = "SECTION";
        public const string KEY_SECTION_WIDTH = "WIDTH";
        public const string KEY_SECTION_DEPTH = "DEPTH";

        // Items under the ANALYSIS subdict of the meta subdict
        public const string KEY_META_ANALYSIS_SUBDICT = "ANALYSIS";
        public const string KEY_ANALYSIS_DESIGN = "DESIGN";
        public const string KEY_ANALYSIS_STATUS = "STATUS";

        // list of primary subdirectories under EE_FDN
        public static readonly string[] KNOWN_ROOT_SUBDIRS =
        {
            KEY_BOUNDARY_SUBDICT,
            KEY_GRADEBEAM_SUBDICT,
            KEY_SLABSTRAND_SUBDICT,
            KEY_REBAR_SUBDICT
        };
        #endregion


        #region Enumeration Helpers
        internal static IEnumerable<(string Key, ObjectId Id)> EnumerateDictionary(DBDictionary dict, string parentName = "<root>", int depth = 0)
        {
            if (dict == null)
            {
                System.Diagnostics.Debug.WriteLine($"{new string(' ', depth * 2)}[DEBUG] Dictionary '{parentName}' is null.");
                yield break;
            }

            if (dict.IsErased)
            {
                System.Diagnostics.Debug.WriteLine($"{new string(' ', depth * 2)}[DEBUG] Dictionary '{parentName}' is erased.");
                yield break;
            }

            string indent = new string(' ', depth * 2);

            foreach (DictionaryEntry entry in dict)
            {
                if (!(entry.Key is string key))
                {
                    System.Diagnostics.Debug.WriteLine($"{indent}[DEBUG] Skipping non-string key in '{parentName}'.");
                    continue;
                }

                if (!(entry.Value is ObjectId id))
                {
                    System.Diagnostics.Debug.WriteLine($"{indent}[DEBUG] Key '{key}' in '{parentName}' is not an ObjectId.");
                    continue;
                }

                string status = $"Key='{key}' | Id={id.Handle.ToString()} | IsValid={id.IsValid} | IsNull={id.IsNull} | IsErased={id.IsErased}";
                System.Diagnostics.Debug.WriteLine($"{indent}[DEBUG] Enumerating entry: {status}");

                if (!id.IsValid || id.IsNull || id.IsErased)
                {
                    System.Diagnostics.Debug.WriteLine($"{indent}[DEBUG] Skipping entry '{key}' because Id is invalid or erased.");
                    continue;
                }

                yield return (key, id);
            }
        }

        #endregion

        #region NOD Structure Creation and Retrieval
        /// <summary>
        /// Initialize the NOD tree structure
        /// </summary>
        /// <param name="context"></param>
        /// <param name="tr"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static DBDictionary InitFoundationNOD(FoundationContext context, Transaction tr)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));

            var db = context.Document.Database;

            var nod = (DBDictionary)tr.GetObject(
                db.NamedObjectsDictionaryId, OpenMode.ForWrite);

            // Make the root dictionary
            var root = GetOrCreateNestedSubDictionary(tr, nod, ROOT);

            // Get or create each known root subdirectory; existing ones are preserved
            foreach (var key in KNOWN_ROOT_SUBDIRS)
                GetOrCreateNestedSubDictionary(tr, root, key);

            return root;
        }

        /// <summary>
        /// Creates or retrieves a grade beam node by handle within the specified subdictionary.
        /// Ensures required subdictionaries (edges, strands) exist.
        /// </summary>
        internal static DBDictionary InitGradeBeamNode_Internal(
            Transaction tr,
            DBDictionary parentDict,
            string centerlineHandle)
        {
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (parentDict == null) throw new ArgumentNullException(nameof(parentDict));
            if (string.IsNullOrWhiteSpace(centerlineHandle)) throw new ArgumentException("Centerline handle is required.", nameof(centerlineHandle));

            // --- Try to get existing node
            if (TryGetNestedSubDictionary(tr, parentDict, out var existingNode, centerlineHandle))
                return existingNode;

            // --- Upgrade parent dictionary for writing
            if (!parentDict.IsWriteEnabled)
                parentDict.UpgradeOpen();

            // --- Create new grade beam node
            var gbNode = new DBDictionary();
            parentDict.SetAt(centerlineHandle, gbNode);
            tr.AddNewlyCreatedDBObject(gbNode, true);

            // --- Ensure required subdictionaries exist
            GetOrCreateNestedSubDictionary(tr, gbNode, KEY_EDGES_SUBDICT);
            GetOrCreateNestedSubDictionary(tr, gbNode, KEY_BEAMSTRAND_SUBDICT);

            // --- Always create META dictionary and SECTION subdictionary
            var metaDict = GetOrCreateNestedSubDictionary(tr, gbNode, KEY_METADATA_SUBDICT);
            GetOrCreateNestedSubDictionary(tr, metaDict, KEY_META_SECTION_SUBDICT);

            return gbNode;
        }

        #endregion

        #region GET OR CREATE functions
        /// <summary>
        /// Return the FD_GRADEBEAM subdictionary under root
        /// </summary>
        /// <param name="tr"></param>
        /// <param name="db"></param>
        /// <param name="forWrite"></param>
        /// <returns></returns>        
        internal static DBDictionary GetOrCreateGradeBeamRootDictionary(
            Transaction tr,
            Database db,
            bool forWrite = false)
        {
            if (tr == null || db == null)
                return null;

            var rootDict = GetFoundationRootDictionary(tr, db);
            if (rootDict == null)
                return null;

            // If we are allowed to create it, delegate to helper
            if (forWrite)
            {
                return GetOrCreateNestedSubDictionary(
                    tr,
                    rootDict,
                    NODCore.KEY_GRADEBEAM_SUBDICT);
            }

            // Otherwise only return if it exists
            if (rootDict.Contains(NODCore.KEY_GRADEBEAM_SUBDICT))
            {
                return tr.GetObject(
                    rootDict.GetAt(NODCore.KEY_GRADEBEAM_SUBDICT),
                    OpenMode.ForRead) as DBDictionary;
            }

            return null;
        }

        /// <summary>
        /// Return the FD_BOUNDARY subdictionary
        /// </summary>
        /// <param name="tr"></param>
        /// <param name="db"></param>
        /// <param name="forWrite"></param>
        /// <returns></returns>        
        internal static DBDictionary GetOrCreateBoundaryBeamRootDictionary(
            Transaction tr,
            Database db,
            bool forWrite = false)
        {
            if (tr == null || db == null)
                return null;

            var rootDict = GetFoundationRootDictionary(tr, db);
            if (rootDict == null)
                return null;

            // If we are allowed to create it, delegate to helper
            if (forWrite)
            {
                return GetOrCreateNestedSubDictionary(
                    tr,
                    rootDict,
                    NODCore.KEY_BOUNDARY_SUBDICT);
            }

            // Otherwise only return if it exists
            if (rootDict.Contains(NODCore.KEY_BOUNDARY_SUBDICT))
            {
                return tr.GetObject(
                    rootDict.GetAt(NODCore.KEY_BOUNDARY_SUBDICT),
                    OpenMode.ForRead) as DBDictionary;
            }

            return null;
        }

        /// <summary>
        /// Return the FD_SLABSTRAND subdictionary
        /// </summary>
        /// <param name="tr"></param>
        /// <param name="db"></param>
        /// <param name="forWrite"></param>
        /// <returns></returns>        
        internal static DBDictionary GetOrCreateSlabStrandRootDictionary(
            Transaction tr,
            Database db,
            bool forWrite = false)
        {
            if (tr == null || db == null)
                return null;

            var rootDict = GetFoundationRootDictionary(tr, db);
            if (rootDict == null)
                return null;

            // If we are allowed to create it, delegate to helper
            if (forWrite)
            {
                return GetOrCreateNestedSubDictionary(
                    tr,
                    rootDict,
                    NODCore.KEY_SLABSTRAND_SUBDICT);
            }

            // Otherwise only return if it exists
            if (rootDict.Contains(NODCore.KEY_SLABSTRAND_SUBDICT))
            {
                return tr.GetObject(
                    rootDict.GetAt(NODCore.KEY_SLABSTRAND_SUBDICT),
                    OpenMode.ForRead) as DBDictionary;
            }

            return null;
        }

        /// <summary>
        /// Return the FD_REBAR subdictionary
        /// </summary>
        /// <param name="tr"></param>
        /// <param name="db"></param>
        /// <param name="forWrite"></param>
        /// <returns></returns>        
        internal static DBDictionary GetOrCreateRebarRootDictionary(
            Transaction tr,
            Database db,
            bool forWrite = false)
        {
            if (tr == null || db == null)
                return null;

            var rootDict = GetFoundationRootDictionary(tr, db);
            if (rootDict == null)
                return null;

            // If we are allowed to create it, delegate to helper
            if (forWrite)
            {
                return GetOrCreateNestedSubDictionary(
                    tr,
                    rootDict,
                    NODCore.KEY_SLABSTRAND_SUBDICT);
            }

            // Otherwise only return if it exists
            if (rootDict.Contains(NODCore.KEY_REBAR_SUBDICT))
            {
                return tr.GetObject(
                    rootDict.GetAt(NODCore.KEY_REBAR_SUBDICT),
                    OpenMode.ForRead) as DBDictionary;
            }

            return null;
        }

        /// <summary>
        /// Get or create a grade beam node under FD_BOUNDARY (single entry)
        /// </summary>
        internal static DBDictionary GetOrCreateBoundaryGradeBeamNode(Transaction tr, Database db, string handle)
        {
            if (string.IsNullOrWhiteSpace(handle))
                throw new ArgumentException("Handle is required", nameof(handle));

            var root = GetOrCreateBoundaryBeamRootDictionary(tr, db, forWrite: true);
            return CreateNODBeamNode_Internal(tr, root, handle);
        }

        /// <summary>
        /// Get or create a grade beam node under FD_GRADEBEAMS (multiple entries)
        /// </summary>
        internal static DBDictionary GetOrCreateGradeBeamNode(Transaction tr, Database db, string handle)
        {
            if (string.IsNullOrWhiteSpace(handle))
                throw new ArgumentException("Handle is required", nameof(handle));

            var root = GetOrCreateGradeBeamRootDictionary(tr, db, forWrite: true);
            return CreateNODBeamNode_Internal(tr, root, handle);
        }

        /// <summary>
        /// Creates (or gets if it already exists) a grade beam node by handle within the specified parent dictionary.
        /// Ensures the required subdictionaries structure is correctly created.
        internal static DBDictionary CreateNODBeamNode_Internal(
            Transaction tr,
            DBDictionary parentDict,
            string centerlineHandle)
        {
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (parentDict == null) throw new ArgumentNullException(nameof(parentDict));
            if (string.IsNullOrWhiteSpace(centerlineHandle)) throw new ArgumentNullException(nameof(centerlineHandle));

            // --- Check if the node already exists
            DBDictionary gbNode = null;
            if (TryGetNestedSubDictionary(tr, parentDict, out gbNode, centerlineHandle))
            {
                return gbNode;
            }

            // --- Upgrade parent for writing
            parentDict.UpgradeOpen();

            // --- Create the new grade beam node
            gbNode = new DBDictionary();
            parentDict.SetAt(centerlineHandle, gbNode);
            tr.AddNewlyCreatedDBObject(gbNode, true);

            // --- Ensure required subdictionaries exist under gbNode
            var edgesSubDict = GetOrCreateNestedSubDictionary(tr, gbNode, NODCore.KEY_EDGES_SUBDICT);
            var beamStrandSubDict = GetOrCreateNestedSubDictionary(tr, gbNode, NODCore.KEY_BEAMSTRAND_SUBDICT);
            var metaDict = GetOrCreateNestedSubDictionary(tr, gbNode, NODCore.KEY_METADATA_SUBDICT);

            // --- Ensure that the subdicts under META are made
            var sectionDict = GetOrCreateNestedSubDictionary(tr, metaDict, NODCore.KEY_META_SECTION_SUBDICT);

            return gbNode;
        }
        #endregion


        #region TRY GET functions
        /// <summary>
        /// Gets the EE_FOUNDATION root subdictionary
        /// </summary>
        /// <param name="tr"></param>
        /// <param name="db"></param>
        /// <returns></returns>
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
        /// Tries to get the FD_BOUNDARY root dictionary under ROOT.
        /// </summary>
        internal static bool TryGetBoundaryBeamRoot(Transaction tr, Database db, out DBDictionary result)
        {
            result = null;

            if (tr == null || db == null)
                return false;

            var rootDict = GetFoundationRootDictionary(tr, db);
            if (rootDict == null)
                return false;

            return TryGetNestedSubDictionary(tr, rootDict, out result, KEY_BOUNDARY_SUBDICT);
        }

        /// <summary>
        /// Try to get a grade beam node under FD_BOUNDARY by centerline handle
        /// </summary>
        internal static bool TryGetBoundaryBeamNode(Transaction tr, Database db, out DBDictionary result)
        {
            result = null;

            // --- Validate inputs
            if (tr == null || db == null)
                return false;

            // --- Get the FD_BOUNDARY root dictionary
            if (!TryGetBoundaryBeamRoot(tr, db, out var boundaryRoot))
                return false;

            if (boundaryRoot == null || boundaryRoot.Count == 0)
                return false;

            // --- Get the first (and only) grade beam subdictionary safely
            foreach (DictionaryEntry entry in boundaryRoot)
            {
                if (entry.Value is ObjectId dictId && dictId.IsValid && !dictId.IsErased)
                {
                    try
                    {
                        var subDict = tr.GetObject(dictId, OpenMode.ForRead) as DBDictionary;
                        if (subDict != null)
                        {
                            result = subDict;
                            return true;
                        }
                    }
                    catch
                    {
                        // Ignore invalid objects and continue
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Try to get FD_GRADEBEAMS root dictionary under ROOT
        /// </summary>
        internal static bool TryGetGradeBeamsRoot(Transaction tr, Database db, out DBDictionary result)
        {
            result = null;
            var root = GetFoundationRootDictionary(tr, db);
            if (root == null || !root.Contains(KEY_GRADEBEAM_SUBDICT))
                return false;

            result = tr.GetObject(root.GetAt(KEY_GRADEBEAM_SUBDICT), OpenMode.ForRead) as DBDictionary;
            return result != null;
        }

        /// <summary>
        /// Try to get a grade beam node under FD_GRADEBEAMS by centerline handle
        /// </summary>
        internal static bool TryGetGradeBeamNode(Transaction tr, Database db, string handle, out DBDictionary result)
        {
            result = null;
            if (!TryGetGradeBeamsRoot(tr, db, out var gradeBeamsRoot))
                return false;

            return TryGetNestedSubDictionary(tr, gradeBeamsRoot, out result, handle);
        }

        /// <summary>
        /// Tries to get the edges subdictionary under a grade beam node.
        /// </summary>
        /// <param name="tr">Open transaction.</param>
        /// <param name="gradeBeamNode">The grade beam node dictionary.</param>
        /// <param name="edgesDict">Outputs the edges dictionary if found.</param>
        /// <returns>True if edges dictionary exists, false otherwise.</returns>
        internal static bool TryGetBeamEdges(Transaction tr, DBDictionary gradeBeamNode, out DBDictionary edgesDict)
        {
            edgesDict = null;
            if (tr == null || gradeBeamNode == null)
                return false;

            return TryGetNestedSubDictionary(tr, gradeBeamNode, out edgesDict, NODCore.KEY_EDGES_SUBDICT);
        }

        /// <summary>
        /// Tries to get the META subdictionary under a grade beam node.
        /// </summary>
        /// <param name="tr">Open transaction.</param>
        /// <param name="gradeBeamNode">The grade beam node dictionary.</param>
        /// <param name="metaDict">Outputs the META dictionary if found.</param>
        /// <returns>True if META dictionary exists, false otherwise.</returns>
        internal static bool TryGetGradeBeamMeta(Transaction tr, DBDictionary gradeBeamNode, out DBDictionary metaDict)
        {
            metaDict = null;
            if (tr == null || gradeBeamNode == null)
                return false;

            return TryGetNestedSubDictionary(tr, gradeBeamNode, out metaDict, NODCore.KEY_METADATA_SUBDICT);
        }

        /// <summary>
        /// Tries to get the SECTION subdictionary under the META dictionary of a grade beam node.
        /// </summary>
        /// <param name="tr">Open transaction.</param>
        /// <param name="gradeBeamNode">The grade beam node dictionary.</param>
        /// <param name="sectionDict">Outputs the SECTION dictionary if found.</param>
        /// <returns>True if SECTION dictionary exists, false otherwise.</returns>
        internal static bool TryGetGradeBeamSectionFromMetaDict(
            Transaction tr,
            DBDictionary gradeBeamNode,
            out DBDictionary sectionDict)
        {
            sectionDict = null;

            if (tr == null || gradeBeamNode == null)
                return false;

            // Try to get the META dictionary under the grade beam node
            if (!TryGetGradeBeamMeta(tr, gradeBeamNode, out var metaDict))
                return false;

            // Try to get the SECTION dictionary under META
            return TryGetNestedSubDictionary(tr, metaDict, out sectionDict, NODCore.KEY_META_SECTION_SUBDICT);
        }

        internal static bool TryGetObjectIdFromHandleString(
            Transaction tr,
            Database db,
            string handleStr,
            out ObjectId result)
        {
            result = ObjectId.Null;

            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (string.IsNullOrWhiteSpace(handleStr))
                return false;

            try
            {
                // --- Convert hex string back to Handle
                long handleValue = Convert.ToInt64(handleStr, 16); // <-- use Int64
                Handle handle = new Handle(handleValue);

                // --- Get ObjectId from handle
                ObjectId id = db.GetObjectId(false, handle, 0);
                if (id.IsNull || id.IsErased)
                    return false;

                // --- Open object to ensure it exists
                var obj = tr.GetObject(id, OpenMode.ForRead, false);
                if (obj == null || obj.IsErased)
                    return false;

                result = id;
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region PRIMARY UTILITY FUNCTIONS
        /// <summary>
        /// Ensures a subdictionary exists under another subdictionary (nested).
        /// </summary>
        internal static DBDictionary GetOrCreateNestedSubDictionary(
            Transaction tr,
            DBDictionary root,
            params string[] keys)
        {
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (keys == null || keys.Length == 0)
                throw new ArgumentException("At least one key is required.", nameof(keys));

            DBDictionary current = root;

            foreach (var key in keys)
            {
                if (current.Contains(key))
                {
                    var obj = tr.GetObject(current.GetAt(key), OpenMode.ForRead) as DBDictionary;

                    if (obj == null)
                        throw new InvalidOperationException(
                            $"Key '{key}' exists but is not a DBDictionary.");

                    current = obj;
                }
                else
                {
                    if (!current.IsWriteEnabled)
                        current.UpgradeOpen();

                    var sub = new DBDictionary();
                    current.SetAt(key, sub);
                    tr.AddNewlyCreatedDBObject(sub, true);

                    current = sub;
                }
            }

            return current;
        }

        internal static bool TryGetNestedSubDictionary(
            Transaction tr,
            DBDictionary root,
            out DBDictionary result,
            params string[] keys)
        {
            result = null;

            if (tr == null || root == null || keys == null || keys.Length == 0)
                return false;

            DBDictionary current = root;

            foreach (string key in keys)
            {
                if (!current.Contains(key))
                    return false;

                var obj = tr.GetObject(current.GetAt(key), OpenMode.ForRead) as DBDictionary;
                if (obj == null)
                    return false;

                current = obj;
            }

            result = current;
            return true;
        }


        // --- Helper to recursively erase all entries in a dictionary
        internal static void EraseDictionaryRecursive(
            Transaction tr,
            Database db,
            DBDictionary dict,
            ref int edgesDeleted,
            ref int beamsDeleted,
            bool eraseEntities = true)
        {
            if (tr == null || db == null || dict == null)
                return;

            var ids = new List<ObjectId>();
            foreach (DictionaryEntry entry in dict)
                if (entry.Value is ObjectId id && id.IsValid && !id.IsNull)
                    ids.Add(id);

            foreach (var id in ids)
            {
                if (id.IsErased)
                    continue;

                var obj = tr.GetObject(id, OpenMode.ForWrite, false);

                if (obj is DBDictionary subDict)
                {
                    EraseDictionaryRecursive(tr, db, subDict, ref edgesDeleted, ref beamsDeleted, eraseEntities);
                    subDict.Erase();
                }
                else if (obj is Xrecord xrec)
                {
                    if (xrec.Data != null)
                    {
                        foreach (TypedValue tv in xrec.Data)
                        {
                            if (!eraseEntities) continue; // <-- skip entities if false

                            if (tv.TypeCode == (int)DxfCode.Text && tv.Value is string handleStr)
                            {
                                try
                                {
                                    var handle = new Handle(Convert.ToInt64(handleStr, 16));
                                    ObjectId entId = db.GetObjectId(false, handle, 0);

                                    if (entId.IsValid && !entId.IsErased)
                                    {
                                        (tr.GetObject(entId, OpenMode.ForWrite) as Entity)?.Erase();
                                        edgesDeleted++;
                                    }
                                }
                                catch { }
                            }
                        }
                    }

                    xrec.Erase();
                }
            }
        }

        #endregion

        internal static void SetRealValue(
Transaction tr,
DBDictionary dict,
string key,
double value)
        {
            if (!dict.IsWriteEnabled)
                dict.UpgradeOpen(); // <--- critical

            Xrecord xr;

            if (dict.Contains(key))
            {
                xr = (Xrecord)tr.GetObject(dict.GetAt(key), OpenMode.ForWrite);
            }
            else
            {
                xr = new Xrecord();
                dict.SetAt(key, xr);
                tr.AddNewlyCreatedDBObject(xr, true);
            }

            xr.Data = new ResultBuffer(
                new TypedValue((int)DxfCode.Real, value));
        }

        internal static double? GetRealValue(
    Transaction tr,
    DBDictionary dict,
    string key)
        {
            if (!dict.Contains(key))
                return null;

            var xr = (Xrecord)tr.GetObject(dict.GetAt(key), OpenMode.ForRead);

            return xr.Data?
                .AsArray()
                .FirstOrDefault(tv => tv.TypeCode == (int)DxfCode.Real)
                .Value as double?;
        }

        /// <summary>
        /// Counts the number of grade beam nodes under FD_GRADEBEAM in the current document.
        /// </summary>
        /// <param name="tr">Open transaction</param>
        /// <param name="db">Database</param>
        /// <returns>Number of grade beam subdictionaries</returns>
        public static int CountGradeBeams(Transaction tr, Database db)
        {
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (db == null) throw new ArgumentNullException(nameof(db));

            // Try to get the FD_GRADEBEAM root dictionary
            DBDictionary gradeBeamRoot = GetOrCreateGradeBeamRootDictionary(tr, db, forWrite: false);
            if (gradeBeamRoot == null || gradeBeamRoot.Count == 0)
                return 0;

            int count = 0;

            foreach (DictionaryEntry entry in gradeBeamRoot)
            {
                if (entry.Value is ObjectId id && id.IsValid && !id.IsErased)
                {
                    var subDict = tr.GetObject(id, OpenMode.ForRead) as DBDictionary;
                    if (subDict != null)
                        count++;
                }
            }

            return count;
        }
    }
}