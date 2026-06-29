// NOD RULES:
// current NOD structure should be this.
//NOD
//└─ EE_Foundation
//   │
//   └─ FD_BOUNDARY (dictionary)
//   │   │
//   │   └─ 3B11 (dictionary handle)
//   │       ├─ EDGES (not used)
//   │       │   ├─ e1 -- xrecord with handle
//   │       │   └─ e2  -- xrecord with handle
//   │       └─ BEAMSTRAND (dictionary)
//   │          ├─ 2A31 -- xrecord with handle
//   │       └─ METADATA (dictionary)
//   │ 
//   ├─ FD_GRADEBEAM_PERIMETER (dictionary)
//   │   │
//   │   ├─ 2A31 (dictionary -- name is handle for the centerline object)
//   │       ├─ EDGES (dictionary)
//   │       │   ├─ e1 -- xrecord with handle
//   │       │   └─ e2  -- xrecord with handle
//   │       └─ METADATA (dictionary)
//   │           └─ SECTION (dictionary)
//   │               ├─ Width -- xrecord with double
//   │               └─ Depth -- xrecord with double
//   │
//   ├─ FD_GRADEBEAM_INTERIOR (dictionary)
//   │   │
//   │   ├─ 2A33 (dictionary -- name is handle for the centerline object)
//   │       ├─ EDGES (dictionary)
//   │       │   ├─ e1 -- xrecord with handle
//   │       │   └─ e2  -- xrecord with handle
//   │       └─ METADATA (dictionary)
//   │           └─ SECTION (dictionary)
//   │               ├─ Width -- xrecord with double
//   │               └─ Depth -- xrecord with double
//   │   │
//   │   └─ <additional grade beam dictionary -- same structure>
//   │      
//   └─ FD_SLABSTRAND (dictionary)
//   │   ├─ 2A33 (dictionary -- name is handle for the centerline object)
//   │       └─ PullEnd -- xrecord with handle
//   │       └─ DeadEnd -- xrecord with handle
//   │       └─ Label -- xrecord with handle
//   │   
//   └─ FD_REBAR (dictionary)
//   │       ├─ REBAR (dictionary)
//   │       │   ├─ b1 -- xrecord with handle
//   │       │   └─ b2  -- xrecord with handle

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
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
        public const string KEY_GRADEBEAM_INTERIOR_SUBDICT = "FD_GRADEBEAM_INTERIOR";
        public const string KEY_GRADEBEAM_PERIMETER_SUBDICT = "FD_GRADEBEAM_PERIMETER";

        public const string KEY_SLABSTRAND_SUBDICT = "FD_SLABSTRAND";
        public const string KEY_REBAR_SUBDICT = "FD_REBAR";

        // The gradebam NODE subdicts -- 
        public const string KEY_EDGES_SUBDICT = "EDGES";
        public const string KEY_BEAMSTRAND_SUBDICT = "BEAMSTRAND";
        public const string KEY_METADATA_SUBDICT = "METADATA";

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
            KEY_GRADEBEAM_PERIMETER_SUBDICT,
            KEY_GRADEBEAM_INTERIOR_SUBDICT,
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

        #endregion

        #region GET OR CREATE functions
        /// <summary>
        /// Return the FD_GRADEBEAM_INTERIOR subdictionary under root
        /// </summary>
        /// <param name="tr"></param>
        /// <param name="db"></param>
        /// <param name="forWrite"></param>
        /// <returns></returns>        
        internal static DBDictionary GetOrCreateGradeBeamInteriorRootDictionary(
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
                    NODCore.KEY_GRADEBEAM_INTERIOR_SUBDICT);
            }

            // Otherwise only return if it exists
            if (rootDict.Contains(NODCore.KEY_GRADEBEAM_INTERIOR_SUBDICT))
            {
                return tr.GetObject(
                    rootDict.GetAt(NODCore.KEY_GRADEBEAM_INTERIOR_SUBDICT),
                    OpenMode.ForRead) as DBDictionary;
            }

            return null;
        }

        /// <summary>
        /// Return the FD_GRADEBEAM_PERIMETER subdictionary under root
        /// </summary>
        /// <param name="tr"></param>
        /// <param name="db"></param>
        /// <param name="forWrite"></param>
        /// <returns></returns>        
        internal static DBDictionary GetOrCreateGradeBeamPerimeterRootDictionary(
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
                    NODCore.KEY_GRADEBEAM_PERIMETER_SUBDICT);
            }

            // Otherwise only return if it exists
            if (rootDict.Contains(NODCore.KEY_GRADEBEAM_PERIMETER_SUBDICT))
            {
                return tr.GetObject(
                    rootDict.GetAt(NODCore.KEY_GRADEBEAM_PERIMETER_SUBDICT),
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
        internal static DBDictionary GetOrCreateBoundaryRootDictionary(
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
                    NODCore.KEY_REBAR_SUBDICT);
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

            var root = GetOrCreateBoundaryRootDictionary(tr, db, forWrite: true);
            return CreateNODBeamNode_Internal(tr, root, handle);
        }

        /// <summary>
        /// Get or create a grade beam node under FD_GRADEBEAMS_INTERIOR (multiple entries) or FD_GRADEBEAMS_PERIMETER (single)
        /// </summary>
        internal static DBDictionary GetOrCreateInteriorGradeBeamNode(Transaction tr, Database db, string handle)
        {
            if (string.IsNullOrWhiteSpace(handle))
                throw new ArgumentException("Handle is required", nameof(handle));

            var root = GetOrCreateGradeBeamInteriorRootDictionary(tr, db, forWrite: true);
            return CreateNODBeamNode_Internal(tr, root, handle);
        }

        /// <summary>
        /// Get or create a grade beam node under FD_GRADEBEAMS_INTERIOR (multiple entries) or FD_GRADEBEAMS_PERIMETER (single)
        /// </summary>
        internal static DBDictionary GetOrCreatePerimeterGradeBeamNode(Transaction tr, Database db, string handle)
        {
            if (string.IsNullOrWhiteSpace(handle))
                throw new ArgumentException("Handle is required", nameof(handle));

            var root = GetOrCreateGradeBeamPerimeterRootDictionary(tr, db, forWrite: true);
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
        internal static bool TryGetBoundaryRoot(Transaction tr, Database db, out DBDictionary result)
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
        /// Try to get a grade beam node under FD_GRADEBEAM_PERIMETER by centerline handle
        /// </summary>
        internal static bool TryGetGradeBeamPerimeterBeamNode(Transaction tr, Database db, out DBDictionary result)
        {
            result = null;

            // --- Validate inputs
            if (tr == null || db == null)
                return false;

            // --- Get the FD_GRADEBEAM_PERIMETER root dictionary
            if (!TryGetGradeBeamPerimeterRoot(tr, db, out var boundaryRoot))
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
        /// Try to get FD_GRADEBEAMS_PERIMETER root dictionary under ROOT
        /// </summary>
        internal static bool TryGetGradeBeamPerimeterRoot(Transaction tr, Database db, out DBDictionary result)
        {
            result = null;
            var root = GetFoundationRootDictionary(tr, db);
            if (root == null || !root.Contains(KEY_GRADEBEAM_PERIMETER_SUBDICT))
                return false;

            result = tr.GetObject(root.GetAt(KEY_GRADEBEAM_PERIMETER_SUBDICT), OpenMode.ForRead) as DBDictionary;
            return result != null;
        }

        /// <summary>
        /// Try to get FD_GRADEBEAMS_INTERIOR root dictionary under ROOT
        /// </summary>
        internal static bool TryGetGradeBeamInteriorRoot(Transaction tr, Database db, out DBDictionary result)
        {
            result = null;
            var root = GetFoundationRootDictionary(tr, db);
            if (root == null || !root.Contains(KEY_GRADEBEAM_INTERIOR_SUBDICT))
                return false;

            result = tr.GetObject(root.GetAt(KEY_GRADEBEAM_INTERIOR_SUBDICT), OpenMode.ForRead) as DBDictionary;
            return result != null;
        }



        /// <summary>
        /// Try to get a grade beam node under FD_GRADEBEAMS_INTERIOR by centerline handle
        /// </summary>
        internal static bool TryGetGradeBeamInteriorBeamNode(Transaction tr, Database db, string handle, out DBDictionary result)
        {
            result = null;
            if (!TryGetGradeBeamInteriorRoot(tr, db, out var gradeBeamsRoot))
                return false;

            return TryGetNestedSubDictionary(tr, gradeBeamsRoot, out result, handle);
        }

        /// <summary>
        /// Tries to get the edges subdictionary under a grade beam node -- works for interior and perimeter grade beams.
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
        /// Tries to get the META subdictionary under a grade beam node -- works for interior and perimeter gradebeams.
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
        /// -- works for both interior and perimeter gradebeams
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


        internal static void EraseDictionaryRecursive(
            Transaction tr,
            Database db,
            DBDictionary dict,
            ref int edgesDeleted,
            ref int beamsDeleted,
            bool eraseEntities = false, // 🔥 FORCE SAFE DEFAULT
            string parentKey = "<root>",
            int depth = 0)
        {
            if (tr == null || db == null || dict == null)
                return;

            string indent = new string(' ', depth * 2);

            var entries = dict.Cast<DictionaryEntry>()
                              .Select(e => new { Key = e.Key.ToString(), Value = e.Value as ObjectId? })
                              .Where(e => e.Value.HasValue && e.Value.Value.IsValid && !e.Value.Value.IsNull)
                              .ToList();

            foreach (var entry in entries)
            {
                var id = entry.Value.Value;

                if (id.IsErased)
                    continue;

                var obj = tr.GetObject(id, OpenMode.ForWrite, false);

                switch (obj)
                {
                    case DBDictionary subDict:
                        EraseDictionaryRecursive(
                            tr,
                            db,
                            subDict,
                            ref edgesDeleted,
                            ref beamsDeleted,
                            eraseEntities,
                            entry.Key,
                            depth + 1);

                        if (!subDict.IsErased)
                            subDict.Erase();

                        break;

                    case Xrecord xrec:
                        // SAFE MODE: metadata only
                        // DO NOT TOUCH referenced drawing entities

                        if (!xrec.IsErased)
                        {
                            xrec.Erase();
                            beamsDeleted++;
                        }

                        break;

                    default:
                        // NEVER TOUCH UNKNOWN TYPES
                        System.Diagnostics.Debug.WriteLine(
                            $"{indent}Skipping unknown type under NOD: {entry.Key}");
                        break;
                }
            }
        }

        #endregion

        /// <summary>
        /// Get's an Xrecord with a specified key from a dictionary.
        /// This is a WRITE transaction event.
        /// </summary>
        /// <param name="tr"></param>
        /// <param name="dict"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        internal static void SetXRecordValue(
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

        /// <summary>
        /// Gets the value from an Xrecord for a specified key in a dictionary.
        /// </summary>
        /// <param name="tr"></param>
        /// <param name="dict"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        internal static double? GetXRecordValue(
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
        /// Counts the number of grade beam nodes under FD_GRADEBEAM_INTERIOR in the current document.
        /// </summary>
        /// <param name="tr">Open transaction</param>
        /// <param name="db">Database</param>
        /// <returns>Number of grade beam subdictionaries</returns>
        public static int CountInteriorGradeBeams(Transaction tr, Database db)
        {
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (db == null) throw new ArgumentNullException(nameof(db));

            // Try to get the FD_GRADEBEAM root dictionary
            DBDictionary gradeBeamRoot = GetOrCreateGradeBeamInteriorRootDictionary(tr, db, forWrite: false);
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





        internal static void ValidateNodHandleTreeRecursive(
            Transaction tr,
            Database db,
            DBDictionary dict,
            Editor ed,
            string parentKey,
            int depth,
            HashSet<string> valid,
            HashSet<string> missing)
        {
            if (tr == null || db == null || dict == null)
                return;

            string indent = new string(' ', depth * 2);

            foreach (DictionaryEntry entry in dict)
            {
                string key = entry.Key.ToString();
                ObjectId childId = (ObjectId)entry.Value;

                // -----------------------------
                // 1. VALIDATE HANDLE (KEY)
                // -----------------------------
                bool handleOk = TryResolveHandle(db, key, out ObjectId resolvedId);

                if (handleOk)
                    valid.Add(key);
                else
                    missing.Add(key);

                ed.WriteMessage($"\n{indent}{(handleOk ? "[OK]" : "[MISSING]")} {key}");

                // -----------------------------
                // 2. RECURSE SAFELY
                // -----------------------------
                if (!childId.IsValid || childId.IsNull || childId.IsErased)
                    continue;

                DBObject obj = tr.GetObject(childId, OpenMode.ForRead, false);

                if (obj is DBDictionary subDict)
                {
                    ValidateNodHandleTreeRecursive(
                        tr,
                        db,
                        subDict,
                        ed,
                        key,
                        depth + 1,
                        valid,
                        missing);
                }
            }
        }

        public static void CleanupInvalidNodBranches(FoundationContext context)
        {
            if (context?.Document == null)
                return;

            var doc = context.Document;
            var db = doc.Database;
            var ed = doc.Editor;

            int deletedBranches = 0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var root = NODCore.GetFoundationRootDictionary(tr, db);

                if (root == null)
                {
                    ed.WriteMessage("\nEE_Foundation not found.");
                    return;
                }

                CleanupRecursive(tr, db, root, ed, ref deletedBranches, "<root>", 0);

                tr.Commit();
            }

            ed.WriteMessage($"\nCleanup complete. Deleted branches: {deletedBranches}");
        }

        private static void CleanupRecursive(
            Transaction tr,
            Database db,
            DBDictionary dict,
            Editor ed,
            ref int deletedBranches,
            string parentKey,
            int depth)
        {
            if (dict == null)
                return;

            string indent = new string(' ', depth * 2);

            var entries = dict.Cast<DictionaryEntry>().ToList();

            foreach (var entry in entries)
            {
                string key = entry.Key.ToString();          // handle string
                ObjectId childId = (ObjectId)entry.Value;

                // -------------------------------------------------
                // HANDLE VALIDATION (structure source of truth)
                // -------------------------------------------------
                bool handleExists =
                    TryResolveHandle(db, key, out ObjectId resolvedId) &&
                    resolvedId.IsValid &&
                    !resolvedId.IsErased;

                DBObject obj = null;

                if (childId.IsValid && !childId.IsNull && !childId.IsErased)
                {
                    obj = tr.GetObject(childId, OpenMode.ForWrite, false);
                }

                // -------------------------------------------------
                // CASE 1: VALID → KEEP + RECURSE
                // -------------------------------------------------
                if (handleExists)
                {
                    if (obj is DBDictionary subDict)
                    {
                        CleanupRecursive(
                            tr,
                            db,
                            subDict,
                            ed,
                            ref deletedBranches,
                            key,
                            depth + 1);
                    }

                    continue;
                }

                // -------------------------------------------------
                // CASE 2: INVALID → DELETE VIA ENGINE ONLY
                // -------------------------------------------------
                ed.WriteMessage($"\n{indent}[REMOVING INVALID BRANCH] {key}");

                if (obj is DBDictionary badDict)
                {
                    int edgesDeleted = 0;
                    int beamsDeleted = 0;

                    // 🔥 SINGLE SOURCE OF TRUTH DELETION ENGINE
                    NODCore.EraseDictionaryRecursive(
                        tr,
                        db,
                        badDict,
                        ref edgesDeleted,
                        ref beamsDeleted,
                        eraseEntities: false);
                }
                else if (obj is Xrecord xrec)
                {
                    if (!xrec.IsWriteEnabled)
                        xrec.UpgradeOpen();

                    xrec.Erase();
                }

                // -------------------------------------------------
                // REMOVE FROM PARENT DICTIONARY (correct API usage)
                // -------------------------------------------------
                if (!dict.IsWriteEnabled)
                    dict.UpgradeOpen();

                if (dict.Contains(key))
                {
                    dict.Remove(key);
                }

                deletedBranches++;
            }

            // -------------------------------------------------
            // OPTIONAL: CLEAN UP EMPTY NODES
            // -------------------------------------------------
            if (dict.Count == 0 && parentKey != "<root>")
            {
                if (!dict.IsWriteEnabled)
                    dict.UpgradeOpen();

                dict.Erase();
            }
        }

        private static bool TryResolveHandle(Database db, string handleStr, out ObjectId id)
        {
            id = ObjectId.Null;

            if (string.IsNullOrWhiteSpace(handleStr))
                return false;

            try
            {
                long h = Convert.ToInt64(handleStr, 16);
                id = db.GetObjectId(false, new Handle(h), 0);

                return id.IsValid && !id.IsErased;
            }
            catch
            {
                return false;
            }
        }

        public static void ValidateFoundationNOD(FoundationContext context)
        {
            if (context == null || context.Document == null)
                return;

            var doc = context.Document;
            var db = doc.Database;
            var ed = doc.Editor;

            var missing = new HashSet<string>();
            var valid = new HashSet<string>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var root = NODCore.GetFoundationRootDictionary(tr, db);

                if (root == null)
                {
                    ed.WriteMessage("\nEE_Foundation not found.");
                    return;
                }

                ValidateNodHandleTreeRecursive(tr, db, root, ed, "<root>", 0, valid, missing);

                tr.Commit();
            }

            ed.WriteMessage("\n\nValidation complete.");
            ed.WriteMessage($"\nValid handles: {valid.Count}");
            ed.WriteMessage($"\nMissing handles: {missing.Count}");

            if (missing.Count > 0)
            {
                ed.WriteMessage("\n\nBroken references:");
                foreach (var h in missing)
                    ed.WriteMessage($"\n  - {h}");
            }
        }

        public static void PruneInvalidNodBranches(FoundationContext context)
        {
            if (context?.Document == null)
                return;

            var doc = context.Document;
            var db = doc.Database;
            var ed = doc.Editor;

            int deletedBranches = 0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var root = NODCore.GetFoundationRootDictionary(tr, db);

                if (root == null)
                {
                    ed.WriteMessage("\nEE_Foundation not found.");
                    return;
                }

                PruneRecursive(tr, db, root, ed, ref deletedBranches, "<root>", 0);

                tr.Commit();
            }

            ed.WriteMessage($"\nNOD prune complete. Deleted branches: {deletedBranches}");
        }

        private static void PruneRecursive(
            Transaction tr,
            Database db,
            DBDictionary dict,
            Editor ed,
            ref int deletedBranches,
            string parentKey,
            int depth)
        {
            if (dict == null)
                return;

            string indent = new string(' ', depth * 2);

            var entries = dict.Cast<DictionaryEntry>().ToList();

            foreach (var entry in entries)
            {
                string key = entry.Key.ToString();
                ObjectId childId = (ObjectId)entry.Value;

                bool handleExists =
                    TryResolveHandle(db, key, out ObjectId resolvedId) &&
                    resolvedId.IsValid &&
                    !resolvedId.IsErased;

                DBObject obj = null;

                if (childId.IsValid && !childId.IsNull && !childId.IsErased)
                    obj = tr.GetObject(childId, OpenMode.ForWrite, false);

                // -----------------------------
                // KEEP
                // -----------------------------
                if (handleExists)
                {
                    if (obj is DBDictionary subDict)
                    {
                        PruneRecursive(
                            tr,
                            db,
                            subDict,
                            ed,
                            ref deletedBranches,
                            key,
                            depth + 1);
                    }

                    continue;
                }

                // -----------------------------
                // DELETE
                // -----------------------------
                ed.WriteMessage($"\n{indent}[PRUNE INVALID] {key}");

                if (obj is DBDictionary badDict)
                {
                    int edgesDeleted = 0;
                    int beamsDeleted = 0;

                    NODCore.EraseDictionaryRecursive(
                        tr,
                        db,
                        badDict,
                        ref edgesDeleted,
                        ref beamsDeleted,
                        eraseEntities: false);

                    if (!badDict.IsErased)
                    {
                        badDict.UpgradeOpen();
                        badDict.Erase();
                    }
                }
                else if (obj is Xrecord xrec)
                {
                    if (!xrec.IsWriteEnabled)
                        xrec.UpgradeOpen();

                    xrec.Erase();
                }

                if (!dict.IsWriteEnabled)
                    dict.UpgradeOpen();

                dict.Remove((ObjectId)entry.Value);

                deletedBranches++;
            }

            // OPTIONAL CONSISTENCY FIX (same as cleanup)
            if (dict.Count == 0 && parentKey != "<root>")
            {
                if (!dict.IsWriteEnabled)
                    dict.UpgradeOpen();

                dict.Erase();
            }
        }
    }
}