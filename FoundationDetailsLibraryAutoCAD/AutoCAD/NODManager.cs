using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

[assembly: CommandClass(typeof(FoundationDetailsLibraryAutoCAD.AutoCAD.NODManager))]

namespace FoundationDetailsLibraryAutoCAD.AutoCAD
{
    public class NODManager
    {
        // ==========================================================
        //  CONSTANTS
        // ==========================================================
        public const string ROOT = "EE_Foundation";
        public const string KEY_BOUNDARY = "FD_BOUNDARY";
        public const string KEY_GRADEBEAM = "FD_GRADEBEAM";

        private static readonly string[] KNOWN_SUBDIRS = { KEY_BOUNDARY,  KEY_GRADEBEAM };



        // ==========================================================
        //  COMMAND: INITIALIZE FOUNDATION STRUCTURE
        // ==========================================================
        [CommandMethod("InitFoundationNOD")]
        public static void InitFoundationNOD(Transaction tr)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            if (tr == null) return;

            try
            {
                // create the root dictionary
                GetOrCreateRootDictionary(tr, db);

                // create the sub dictionaries
                foreach(var sub_dir in KNOWN_SUBDIRS)
                {
                    GetOrCreateSubDictionary(tr, db, sub_dir);
                }

                ed.WriteMessage("\nEE_Foundation NOD structure initialized successfully.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nTransaction failed: {ex.Message}");
            }
        }

        // ==========================================================
        //  ITERATE AND CLEAN HANDLES
        // ==========================================================
        public static List<HandleEntry> IterateFoundationNod(bool cleanStale = false)
        {
            List<HandleEntry> results = new List<HandleEntry>();
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    DBDictionary nod = tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead) as DBDictionary;
                    if (nod == null || !nod.Contains(ROOT)) return results;

                    DBDictionary root = tr.GetObject(nod.GetAt(ROOT), OpenMode.ForRead) as DBDictionary;

                    foreach (DBDictionaryEntry group in root)
                    {
                        DBDictionary sub = tr.GetObject(group.Value, OpenMode.ForRead) as DBDictionary;
                        if (sub == null) continue;

                        List<string> keys = new List<string>();
                        foreach (DBDictionaryEntry entry in sub) keys.Add(entry.Key);

                        List<string> keysToRemove = new List<string>();

                        foreach (string handleStr in keys)
                        {
                            ObjectId id;
                            var entryResult = new HandleEntry { GroupName = group.Key, HandleKey = handleStr };

                            if (!TryGetObjectIdFromHandleString(db, handleStr, out id))
                            {
                                entryResult.Status = "Invalid";
                                keysToRemove.Add(handleStr);
                            }
                            else
                            {
                                try
                                {
                                    Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                                    entryResult.Status = (ent == null || ent.IsErased) ? "Missing" : "Valid";
                                    entryResult.Id = id;
                                    if (entryResult.Status != "Valid") keysToRemove.Add(handleStr);
                                }
                                catch
                                {
                                    entryResult.Status = "Error";
                                    keysToRemove.Add(handleStr);
                                }
                            }

                            results.Add(entryResult);
                        }

                        if (cleanStale && keysToRemove.Count > 0)
                        {
                            sub.UpgradeOpen();
                            foreach (string key in keysToRemove) sub.Remove(key);
                        }
                    }
                    tr.Commit();
                }
                catch
                {
                    // transaction auto-aborts on dispose
                }
            }

            return results;
        }

        // ==========================================================
        //  VIEW QueryNOD CONTENT
        // ==========================================================
        [CommandMethod("ViewFoundationNOD")]
        public static void ViewFoundationNOD()
        {
            // Get all entries across all subdictionaries
            var entries = IterateFoundationNod(cleanStale: false);

            // Group entries by subdictionary name
            var grouped = entries
                .GroupBy(x => x.GroupName)
                .ToDictionary(g => g.Key, g => g.ToList());

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== EE_Foundation Contents ===");

            foreach (string subDir in KNOWN_SUBDIRS)
            {
                sb.AppendLine();
                sb.AppendLine($"[{subDir}]");

                if (!grouped.ContainsKey(subDir) || grouped[subDir].Count == 0)
                {
                    sb.AppendLine("   No Objects");
                    continue;
                }

                foreach (var e in grouped[subDir])
                {
                    sb.AppendLine($"   {e.HandleKey} : {e.Status}");
                }
            }

            MessageBox.Show(sb.ToString(), "EE_Foundation Viewer");
        }

        // ==========================================================
        //  CLEAN STALE HANDLES
        // ==========================================================
        [CommandMethod("CleanFoundationNOD")]
        public static void CleanFoundationNOD()
        {
            var entries = IterateFoundationNod(cleanStale: true);
            List<string> removed = new List<string>();
            foreach (var e in entries)
                if (e.Status == "Missing" || e.Status == "Invalid" || e.Status == "Error")
                    removed.Add($"[{e.GroupName}] {e.HandleKey}");

            string msg = removed.Count > 0 ?
                "Removed stale handle_strings:\n" + string.Join("\n", removed) :
                "No stale handle_strings found.";

            MessageBox.Show(msg, "CleanFoundationNOD");
        }

        // ==========================================================
        //  REMOVE ENTIRE QueryNOD STRUCTURE
        // ==========================================================
        [CommandMethod("EraseFoundationNOD")]
        public static void EraseFoundationNOD()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                    if (nod.Contains(ROOT))
                    {
                        DBDictionary root = (DBDictionary)tr.GetObject(nod.GetAt(ROOT), OpenMode.ForWrite);
                        root.Erase();
                    }
                    tr.Commit();
                    MessageBox.Show("EE_Foundation dictionary erased.");
                }
                catch (System.Exception ex)
                {
                    doc.Editor.WriteMessage($"\nTransaction failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Erases a specified subdictionary under EE_Foundation.
        /// </summary>
        public static void EraseFoundationSubDictionary(string subDictionaryName)
        {
            if (string.IsNullOrWhiteSpace(subDictionaryName))
                throw new ArgumentException("Subdictionary name cannot be null or empty.", nameof(subDictionaryName));

            subDictionaryName = subDictionaryName.Trim().ToUpperInvariant();

            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

                    if (!nod.Contains(ROOT))
                    {
                        ed.WriteMessage("\nEE_Foundation root dictionary does not exist.");
                        return;
                    }

                    DBDictionary root = (DBDictionary)tr.GetObject(nod.GetAt(ROOT), OpenMode.ForWrite);

                    if (!root.Contains(subDictionaryName))
                    {
                        ed.WriteMessage($"\nSubdictionary {subDictionaryName} does not exist.");
                        return;
                    }

                    DBDictionary subDict = (DBDictionary)tr.GetObject(root.GetAt(subDictionaryName), OpenMode.ForWrite);
                    subDict.Erase();
                    tr.Commit();

                    ed.WriteMessage($"\nSubdictionary {subDictionaryName} erased.");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nTransaction failed: {ex.Message}");
                }
            }
        }

        // ==========================================================
        //  EXPORT / IMPORT JSON
        // ==========================================================
        [CommandMethod("ExportFoundationNOD")]
        public static void ExportFoundationNOD()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application
                                .DocumentManager.MdiActiveDocument;
            Database db = doc.Database;

            // Build filename in same folder as drawing
            string drawingFolder = Path.GetDirectoryName(doc.Name);
            string drawingName = Path.GetFileNameWithoutExtension(doc.Name);
            string jsonFile = Path.Combine(drawingFolder, $"{drawingName}_FDN_DATA.json");

            // Container for export data
            Dictionary<string, List<string>> exportData =
                new Dictionary<string, List<string>>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    // Open the top-level NOD (Named Objects Dictionary)
                    DBDictionary nod =
                        (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

                    if (!nod.Contains(ROOT))
                    {
                        MessageBox.Show("EE_Foundation dictionary does not exist.");
                        return;
                    }

                    // Open EE_Foundation
                    DBDictionary root =
                        (DBDictionary)tr.GetObject(nod.GetAt(ROOT), OpenMode.ForRead);

                    // Loop through all known subdictionaries so they appear even if empty
                    foreach (string sub in KNOWN_SUBDIRS)
                    {
                        List<string> handles = new List<string>();

                        if (root.Contains(sub))
                        {
                            DBDictionary subDict =
                                (DBDictionary)tr.GetObject(root.GetAt(sub), OpenMode.ForRead);

                            // Collect handle keys
                            foreach (DBDictionaryEntry entry in subDict)
                            {
                                handles.Add(entry.Key);
                            }
                        }

                        // store result (empty list if none)
                        exportData[sub] = handles;
                    }

                    tr.Commit();
                }
                catch (System.Exception ex)
                {
                    doc.Editor.WriteMessage($"\nExport failed: {ex.Message}");
                    return;
                }
            }

            // Convert dictionary → JSON and write to file
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(
                exportData,
                Newtonsoft.Json.Formatting.Indented);

            File.WriteAllText(jsonFile, json);

            MessageBox.Show($"Export complete:\n{jsonFile}");
        }

        /// <summary>
        /// A function that loads the associated JSON file with the NOD data.
        /// </summary>
        [CommandMethod("ImportFoundationNOD")]
        public static void ImportFoundationNOD()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application
                                .DocumentManager.MdiActiveDocument;
            Database db = doc.Database;

            // Build the JSON file name in the drawing’s folder
            string drawingFolder = Path.GetDirectoryName(doc.Name);
            string drawingName = Path.GetFileNameWithoutExtension(doc.Name);
            string jsonFile = Path.Combine(drawingFolder, $"{drawingName}_FDN_DATA.json");

            if (!File.Exists(jsonFile))
            {
                MessageBox.Show($"{jsonFile} not found.");
                return;
            }

            // Read and deserialize JSON
            string json = File.ReadAllText(jsonFile);
            var importData =
                Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(json);
            if (importData == null)
            {
                MessageBox.Show("JSON format invalid.");
                return;
            }

            using (doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    InitFoundationNOD(tr);
                    tr.Commit();
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        // Open the parent NOD for write
                        DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

                        DBDictionary root = (DBDictionary)tr.GetObject(nod.GetAt(ROOT), OpenMode.ForWrite);

                        // Process each group found in the JSON
                        foreach (var kvp in importData)
                        {
                            string subName = kvp.Key;
                            List<string> handle_strings = kvp.Value ?? new List<string>();

                            // Ensure subdictionary exists
                            DBDictionary subDict;
                            if (!root.Contains(subName))
                            {
                                subDict = new DBDictionary();
                                root.SetAt(subName, subDict);
                                tr.AddNewlyCreatedDBObject(subDict, true);
                            }
                            else
                            {
                                subDict = (DBDictionary)tr.GetObject(root.GetAt(subName), OpenMode.ForWrite);
                            }

                            // Import handle_strings
                            foreach (string handle_string in handle_strings)
                            {
                                Handle handle;
                                if (TryParseHandle(handle_string, out handle))
                                {
                                    AddHandleToDictionary(tr, subDict, handle_string);
                                }
                            }
                        }

                        tr.Commit();
                        MessageBox.Show($"{jsonFile} imported successfully.");
                    }
                    catch (System.Exception ex)
                    {
                        doc.Editor.WriteMessage($"\nImport failed: {ex.Message}");
                    }
                }

                CleanFoundationNOD();  // clean up stale handle_strings that don't have an associated drawing object in the drawing.
            }
        }

        // ==========================================================
        //  SAMPLE DATA CREATION
        // ==========================================================
        [CommandMethod("CreateSampleFoundationForNOD")]
        public void CreateSampleFoundationForNOD()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    InitFoundationNOD(tr);
                    tr.Commit();
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                        DBDictionary root = (DBDictionary)tr.GetObject(nod.GetAt(ROOT), OpenMode.ForWrite);

                        DBDictionary boundaryDict = (DBDictionary)tr.GetObject(root.GetAt(KEY_BOUNDARY), OpenMode.ForWrite);
                        DBDictionary gradebeamDict = (DBDictionary)tr.GetObject(root.GetAt(KEY_GRADEBEAM), OpenMode.ForWrite);

                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                        // Create FD_BOUNDARY polyline
                        Polyline boundary = new Polyline();
                        boundary.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
                        boundary.AddVertexAt(1, new Point2d(1000, 0), 0, 0, 0);
                        boundary.AddVertexAt(2, new Point2d(1000, 500), 0, 0, 0);
                        boundary.AddVertexAt(3, new Point2d(0, 500), 0, 0, 0);
                        boundary.Closed = true;

                        ms.AppendEntity(boundary);
                        tr.AddNewlyCreatedDBObject(boundary, true);
                        AddHandleToDictionary(tr, boundaryDict, boundary.Handle.ToString().ToUpperInvariant());

                        // Create 4 FD_GRADEBEAM polylines
                        for (int i = 0; i < 4; i++)
                        {
                            Polyline gb = new Polyline();
                            int y = 10 + i * 10;
                            gb.AddVertexAt(0, new Point2d(10, y), 0, 0, 0);
                            gb.AddVertexAt(1, new Point2d(90, y), 0, 0, 0);

                            ms.AppendEntity(gb);
                            tr.AddNewlyCreatedDBObject(gb, true);
                            AddHandleToDictionary(tr, gradebeamDict, gb.Handle.ToString().ToUpperInvariant());
                        }
                        tr.Commit();
                        ed.WriteMessage("\nSample polylines created for FD_BOUNDARY and FD_GRADEBEAM.");
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage($"\nTransaction failed: {ex.Message}");
                    }
                }
            }
        }



        // ==========================================================
        //  CLEAN ALL QueryNOD ENTRIES
        // ==========================================================
        [CommandMethod("NODCleaner")]
        public static void NODCleanAll()
        {
            IterateFoundationNod(true);
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
        internal static DBDictionary GetOrCreateRootDictionary(Transaction tr, Database db)
        {
            if (tr == null || db == null)
                return null;

            // Open the top-level Named Objects Dictionary
            DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
            if (nod == null)
                return null;

            // Check if EE_Foundation exists
            if (!nod.Contains(ROOT))
            {
                DBDictionary root = new DBDictionary();
                nod.SetAt(ROOT, root);
                tr.AddNewlyCreatedDBObject(root, true);

                Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                doc.Editor.WriteMessage("\nCreated root dictionary: " + ROOT);
                return root;
            }

            // Return existing EE_Foundation
            return (DBDictionary)tr.GetObject(nod.GetAt(ROOT), OpenMode.ForWrite);
        }

        /// <summary>
        /// Ensures a subdictionary exists, creates if missing.
        /// </summary>
        internal static DBDictionary GetOrCreateSubDictionary(Transaction tr, Database db, string subKey)
        {
            if (tr == null || db == null || string.IsNullOrWhiteSpace(subKey))
                return null;

            // Get the top-level NOD
            DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (nod == null || !nod.Contains(ROOT))
                return null;

            // Open root for write
            DBDictionary root = (DBDictionary)tr.GetObject(nod.GetAt(ROOT), OpenMode.ForWrite);
            if (root == null)
                return null;

            // Check if subdictionary exists
            if (!root.Contains(subKey))
            {
                // Create subdictionary under EE_Foundation
                DBDictionary sub = new DBDictionary();
                root.SetAt(subKey, sub);
                tr.AddNewlyCreatedDBObject(sub, true);
                return sub;
            }

            // Return existing subdictionary opened for write
            return (DBDictionary)tr.GetObject(root.GetAt(subKey), OpenMode.ForWrite);
        }

        // --------------------------------------------------------
        //  Generic helper to get or create a subdictionary under "EE_Foundation"
        // --------------------------------------------------------
        internal static DBDictionary GetSubDictionary(Transaction tr, Database db, string subKey)
        {
            if (tr == null || db == null || string.IsNullOrWhiteSpace(subKey))
                return null;

            // Get the top-level NOD
            DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (nod == null || !nod.Contains(ROOT))
                return null;

            // Get the root EE_Foundation dictionary
            DBDictionary root = (DBDictionary)tr.GetObject(nod.GetAt(ROOT), OpenMode.ForRead);
            if (root == null || !root.Contains(subKey))
                return null;

            // Return the subdictionary (FD_BOUNDARY or FD_GRADEBEAM)
            return (DBDictionary)tr.GetObject(root.GetAt(subKey), OpenMode.ForRead);
        }

        /// <summary>
        /// Adds a handle as an Xrecord to a dictionary if it doesn't exist.
        /// </summary>
        internal static void AddHandleToDictionary(Transaction tr, DBDictionary dict, string handle)
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
        public class HandleEntry
        {
            public string GroupName;
            public string HandleKey;
            public ObjectId Id;
            public string Status; // "Valid", "Missing", "Invalid", "Error"
        }

        /// <summary>
        /// Returns all keys from the given AutoCAD sub-dictionary.
        /// Keys are returned as raw strings (typically handle strings) with no validation.
        /// </summary>
        /// <param name="subDict">Sub-dictionary to enumerate (must not be null).</param>
        /// <returns>List of dictionary keys.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="subDict"/> is null.</exception>
        internal static List<string> GetAllHandlesFromSubDictionary(DBDictionary subDict)
        {
            if (subDict == null)
                throw new ArgumentNullException(nameof(subDict));

            var result = new List<string>();

            // Enumerate all dictionary entries and collect their keys
            foreach (DBDictionaryEntry entry in subDict)
            {
                result.Add(entry.Key);
            }

            return result;
        }

        /// <summary>
        /// Returns all valid, non-erased ObjectIds from handle strings stored in a sub-dictionary.
        /// Invalid handle_strings, erased objects, or stale references are ignored.
        /// </summary>
        /// <param name="tr">Active AutoCAD transaction for object validation.</param>
        /// <param name="db">Database in which handle_strings are resolved.</param>
        /// <param name="subDict">Sub-dictionary containing handle strings; null returns empty list.</param>
        /// <returns>List of valid, readable ObjectIds.</returns>

        internal static List<ObjectId> GetAllValidObjectIdsFromSubDictionary(
            Transaction tr, Database db, DBDictionary subDict)
        {
            var validIds = new List<ObjectId>();

            // Missing sub-dictionary is not an error; return empty result
            if (subDict == null)
                return validIds;

            foreach (DBDictionaryEntry entry in subDict)
            {
                // Attempt to resolve the dictionary key into an ObjectId
                if (!TryGetObjectIdFromHandleString(db, entry.Key, out ObjectId id))
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
        internal static bool TryGetObjectIdFromHandleString(
            Database db, string handleStr, out ObjectId id)
        {
            id = ObjectId.Null;

            // Parse the string into a Handle structure
            if (!TryParseHandle(handleStr, out Handle handle))
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
        internal static bool TryParseHandle(string handleString, out Handle handle)
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
            Transaction tr, Database db, string subDictKey, out ObjectId oid)
        {
            oid = ObjectId.Null;

            // Retrieve the requested sub-dictionary
            var subDict = GetSubDictionary(tr, db, subDictKey);
            if (subDict == null || subDict.Count == 0)
                return false;

            // Evaluate the first entry only
            foreach (DBDictionaryEntry entry in subDict)
            {
                if (TryGetObjectIdFromHandleString(db, entry.Key, out oid)
                    && IsValidReadableObject(tr, oid))
                    return true;

                break;
            }

            return false;
        }

        // ==========================================================
        // REMOVE SPECIFIC HANDLE (Dynamic Sub-Dictionary)
        // Using user entered handles.  
        // ==========================================================
        [CommandMethod("RemoveNODRecord")]
        public void RemoveNODRecord()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // Prompt for sub-dictionary name
            PromptStringOptions psoSub = new PromptStringOptions("\nEnter sub-dictionary name:");
            psoSub.AllowSpaces = false;
            PromptResult resSub = ed.GetString(psoSub);
            if (resSub.Status != PromptStatus.OK) return;

            string subDictName = resSub.StringResult.Trim().ToUpperInvariant();

            // Validate against known sub-dictionaries dynamically
            if (Array.IndexOf(KNOWN_SUBDIRS, subDictName) < 0)
            {
                ed.WriteMessage("\nInvalid sub-dictionary. Must be one of: " + string.Join(", ", KNOWN_SUBDIRS));
                return;
            }

            // Prompt for handle to remove
            PromptStringOptions psoHandle = new PromptStringOptions("\nEnter handle to remove:");
            psoHandle.AllowSpaces = false;
            PromptResult resHandle = ed.GetString(psoHandle);
            if (resHandle.Status != PromptStatus.OK) return;

            string handleStr = resHandle.StringResult.Trim();
            Handle handle;
            if (!TryParseHandle(handleStr, out handle))
            {
                ed.WriteMessage("\nInvalid handle string.");
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    // Retrieve the sub-dictionary dynamically
                    DBDictionary subDict = GetSubDictionary(tr, db, subDictName);
                    if (subDict == null || !subDict.Contains(handleStr))
                    {
                        ed.WriteMessage($"\nHandle {handleStr} not found in sub-dictionary {subDictName}.");
                        return;
                    }

                    // Erase the Xrecord associated with the handle
                    Xrecord xr = (Xrecord)tr.GetObject(subDict.GetAt(handleStr), OpenMode.ForWrite);
                    xr.Erase();

                    tr.Commit();
                    ed.WriteMessage($"\nHandle {handleStr} successfully removed from {subDictName}.");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nTransaction failed: {ex.Message}");
                }
            }
        }
    }
}
