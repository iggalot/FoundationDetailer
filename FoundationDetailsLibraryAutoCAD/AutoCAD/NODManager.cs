using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FoundationDetailer.UI.Windows;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

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
        public const string KEY_BEAMSTRAND = "FD_BEAMSTRAND";
        public const string KEY_SLABSTRAND = "FD_SLABSTRAND";
        public const string KEY_REBAR = "FD_REBAR";

        private static readonly string[] KNOWN_SUBDIRS = { KEY_BOUNDARY,  KEY_GRADEBEAM, KEY_BEAMSTRAND, KEY_SLABSTRAND, KEY_REBAR };


        // ==========================================================
        //  COMMAND: INITIALIZE FOUNDATION STRUCTURE
        // ==========================================================
        [CommandMethod("InitFoundationNOD")]
        public static void InitFoundationNOD(FoundationContext context, Transaction tr)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));


            if (tr == null) throw new ArgumentNullException(nameof(tr));

            var doc = context.Document;
            var model = context.Model;
            var db = doc.Database;
            var ed = doc.Editor;

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

        public static class HandleStatus
        {
            public const string Valid = "Valid";
            public const string Missing = "Missing";
            public const string Invalid = "Invalid";
            public const string Error = "Error";
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
        //  VIEW NOD CONTENT helper function
        // ==========================================================
        [CommandMethod("ViewFoundationNOD")]
        public static void ViewFoundationNOD(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var model = context.Model;
            var db = doc.Database;

            // Get all entries across all subdictionaries
            var entries = IterateFoundationNod(context, cleanStale: true);

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

            //MessageBox.Show(sb.ToString(), "EE_Foundation Viewer");
            ScrollableMessageBox.Show(sb.ToString());

        }

        // ==========================================================
        //  CLEAN STALE HANDLES
        // ==========================================================
        [CommandMethod("CleanFoundationNOD")]
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
        //  REMOVE ENTIRE btnQueryNOD_Click STRUCTURE
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
        public static void ExportFoundationNOD(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var model = context.Model;
            var db = doc.Database;

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
        public static void ImportFoundationNOD(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var model = context.Model;
            var db = doc.Database;

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
            var importData = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(json);
            if (importData == null)
            {
                MessageBox.Show("JSON format invalid.");
                return;
            }

            using (doc.LockDocument())
            {
                // Ensure the NOD exists
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    InitFoundationNOD(context, tr);
                    tr.Commit();
                }

                // Import the entities from JSON
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                        DBDictionary root = (DBDictionary)tr.GetObject(nod.GetAt(ROOT), OpenMode.ForWrite);

                        foreach (var kvp in importData)
                        {
                            string subName = kvp.Key;
                            List<string> handle_strings = kvp.Value ?? new List<string>();

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

                            foreach (string handle_string in handle_strings)
                            {
                                if (!TryParseHandle(context,handle_string, out Handle handle))
                                    continue;

                                AddHandleToDictionary(tr, subDict, handle_string);

                                if (!TryGetObjectIdFromHandle(db, handle, out ObjectId id))
                                    continue;

                                if (!IsValidReadableObject(tr, id))
                                    continue;

                                Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                                if (ent == null)
                                    continue;

                                // Attach entity-side Foundation data
                                FoundationEntityData.Write(tr, ent, subName);
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

                // Clean stale handles
                CleanFoundationNOD(context);
            }
        }

        // ==========================================================
        //  SAMPLE DATA CREATION
        // ==========================================================
        [CommandMethod("CreateSampleFoundationForNOD")]
        public void CreateSampleFoundationForNOD(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var model = context.Model;
            var db = doc.Database;
            var ed = doc.Editor;

            using (doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    InitFoundationNOD(context, tr);
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

                        FoundationEntityData.Write(tr, boundary, KEY_BOUNDARY);
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

        /// <summary>
        /// Removes a handle from a specified sub-dictionary programmatically.
        /// </summary>
        /// <param name="tr">Active transaction.</param>
        /// <param name="db">Database containing the sub-dictionary.</param>
        /// <param name="subDictName">Name of the sub-dictionary.</param>
        /// <param name="handleStr">Handle string of the entry to remove.</param>
        /// <returns>True if removal succeeded; false if sub-dictionary or handle was not found.</returns>
        internal bool RemoveHandleFromSubDictionary(FoundationContext context, Transaction tr, Database db, string subDictName, string handleStr)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (string.IsNullOrWhiteSpace(subDictName)) throw new ArgumentException("Sub-dictionary name required", nameof(subDictName));
            if (string.IsNullOrWhiteSpace(handleStr)) throw new ArgumentException("Handle string required", nameof(handleStr));

            // Trim and normalize handle
            handleStr = handleStr.Trim();

            // Attempt to parse handle
            Handle handle;
            if (!TryParseHandle(context, handleStr, out handle))
                return false;

            // Get sub-dictionary
            DBDictionary subDict = GetSubDictionary(tr, db, subDictName);
            if (subDict == null || !subDict.Contains(handleStr))
                return false;

            // Erase the Xrecord associated with the handle
            try
            {
                Xrecord xr = (Xrecord)tr.GetObject(subDict.GetAt(handleStr), OpenMode.ForWrite);
                xr.Erase();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Removes a handle from any known sub-dictionary in the EE_Foundation dictionary if it exists.
        /// Assumes the transaction is already started.
        /// </summary>
        internal bool RemoveSingleHandleFromKnownSubDictionaries(FoundationContext context, Transaction tr, Database db, string handleStr)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (string.IsNullOrWhiteSpace(handleStr)) throw new ArgumentException("Handle string required", nameof(handleStr));

            handleStr = handleStr.Trim();
            Handle handle;
            if (!TryParseHandle(context,handleStr, out handle))
                return false;

            foreach (string subDictName in KNOWN_SUBDIRS)
            {
                DBDictionary subDict = GetSubDictionary(tr, db, subDictName);
                if (subDict != null && subDict.Contains(handleStr))
                {
                    try
                    {
                        Xrecord xr = (Xrecord)tr.GetObject(subDict.GetAt(handleStr), OpenMode.ForWrite);
                        xr.Erase();
                        return true;
                    }
                    catch
                    {
                        // Ignore errors, continue to next sub-dictionary
                    }
                }
            }

            return false; // Not found
        }

        /// <summary>
        /// Removes multiple handles from all known sub-dictionaries in a single transaction.
        /// Leverages RemoveSingleHandleFromKnownSubDictionaries internally.
        /// </summary>
        internal int RemoveMultipleHandlesFromKnownSubDictionaries(FoundationContext context, Database db, IEnumerable<string> handleStrings)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var model = context.Model;

            if (doc == null) return 0;

            if (db == null) throw new ArgumentNullException(nameof(db));
            if (handleStrings == null) throw new ArgumentNullException(nameof(handleStrings));

            int removedCount = 0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (string handleStr in handleStrings)
                {
                    if (!string.IsNullOrWhiteSpace(handleStr))
                    {
                        if (RemoveSingleHandleFromKnownSubDictionaries(context, tr, db, handleStr.Trim()))
                        {
                            removedCount++;
                        }
                    }
                }

                tr.Commit();
            }

            return removedCount;
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

            // Get subdictionary
            DBDictionary subDict = GetSubDictionary(tr, db, subDictName);
            if (subDict == null)
                return false;

            // Must open for write
            if (!subDict.IsWriteEnabled)
                subDict.UpgradeOpen();

            // Collect keys first (cannot modify while iterating)
            var keys = new List<string>();
            foreach (DBDictionaryEntry entry in subDict)
            {
                keys.Add(entry.Key);
            }

            // Remove each entry
            foreach (string key in keys)
            {
                try
                {
                    DBObject obj = tr.GetObject(subDict.GetAt(key), OpenMode.ForWrite);
                    obj.Erase();
                }
                catch
                {
                    // Ignore individual failures, continue clearing
                }
            }

            return true;
        }

        internal static bool ClearFoundationSubDictionary(FoundationContext context,
            Database db,
            string subDictName)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));

            Document doc = context.Document;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                bool result =
                    ClearFoundationSubDictionaryInternal(tr, db, subDictName);

                tr.Commit();
                return result;
            }
        }

        [CommandMethod("ClearFoundationSubDict")]
        public static void ClearFoundationSubDictCommand()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptStringOptions pso =
                new PromptStringOptions("\nEnter sub-dictionary to clear:");
            pso.AllowSpaces = false;

            PromptResult res = ed.GetString(pso);
            if (res.Status != PromptStatus.OK)
                return;

            string subName = res.StringResult.Trim().ToUpperInvariant();

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                if (ClearFoundationSubDictionaryInternal(tr, db, subName))
                {
                    ed.WriteMessage($"\nSubdictionary {subName} cleared.");
                }
                else
                {
                    ed.WriteMessage($"\nSubdictionary {subName} not found.");
                }

                tr.Commit();
            }
        }





        public class HandleEntry
        {
            public string GroupName { get; set; }   // FD_BOUNDARY, FD_GRADEBEAM, etc.
            public string HandleKey { get; set; }   // handle string
            public string Status { get; set; }      // Valid | Missing | Invalid | Error
            public ObjectId Id { get; set; }         // only set when valid
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
        public static bool TryGetFirstEntity(FoundationContext context,
            Transaction tr, Database db, string subDictKey, out ObjectId oid)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));

            oid = ObjectId.Null;

            // Retrieve the requested sub-dictionary
            var subDict = GetSubDictionary(tr, db, subDictKey);
            if (subDict == null || subDict.Count == 0)
                return false;

            // Evaluate the first entry only
            foreach (DBDictionaryEntry entry in subDict)
            {
                if (TryGetObjectIdFromHandleString(context, db, entry.Key, out oid)
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
        [CommandMethod("RemoveNODRecordManual")]
        public void RemoveNODRecordManual(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var model = context.Model;

            if (doc == null) return;

            var db = doc.Database;
            var ed = doc.Editor;

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
            if (!TryParseHandle(context, handleStr, out handle))
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

        /// <summary>
        /// Recursively traverses a DBDictionary and its subdictionaries, 
        /// invoking a callback for each entity found.
        /// </summary>
        /// <param name="tr">Active transaction</param>
        /// <param name="dict">Dictionary to traverse</param>
        /// <param name="db">Database reference</param>
        /// <param name="callback">Action to invoke per entity (Entity, handle string)</param>
        internal static void TraverseDictionary(
            FoundationContext context,
            Transaction tr,
            DBDictionary dict,
            Database db,
            Action<TraversalResult> callback)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (dict == null) throw new ArgumentNullException(nameof(dict));
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            foreach (DBDictionaryEntry entry in dict)
            {
                DBObject obj = tr.GetObject(entry.Value, OpenMode.ForRead);

                if (obj is DBDictionary subDict)
                {
                    TraverseDictionary(context, tr, subDict, db, callback);
                    continue;
                }

                // ----- LEAF SAFETY BEGINS (unchanged in spirit) -----

                if (!TryParseHandle(context, entry.Key, out Handle handle))
                {
                    callback(TraversalResult.InvalidHandle(entry.Key));
                    continue;
                }

                if (!db.TryGetObjectId(handle, out ObjectId id))
                {
                    callback(TraversalResult.MissingObjectId(entry.Key, handle));
                    continue;
                }

                if (!id.IsValid || id.IsErased)
                {
                    callback(TraversalResult.Erased(entry.Key, handle, id));
                    continue;
                }

                DBObject dbObj = tr.GetObject(id, OpenMode.ForRead);
                if (!(dbObj is Entity ent))
                {
                    callback(TraversalResult.NotEntity(entry.Key, handle, id));
                    continue;
                }

                callback(TraversalResult.Success(entry.Key, handle, id, ent));
            }
        }

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

            if (!nod.Contains(NODManager.ROOT))
                return null;

            return (DBDictionary)tr.GetObject(
                nod.GetAt(NODManager.ROOT),
                OpenMode.ForRead);
        }

        /// <summary>
        /// Deletes all entities referenced by a foundation subdictionary.
        /// Optionally removes the handle records from the dictionary as well.
        /// </summary>
        internal static int DeleteEntitiesFromFoundationSubDictionary(FoundationContext context,
            Transaction tr,
            Database db,
            string subDictName,
            bool removeHandlesFromNod = true)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var model = context.Model;

            var ed = doc.Editor;


            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (string.IsNullOrWhiteSpace(subDictName))
                throw new ArgumentException(nameof(subDictName));

            int deletedCount = 0;

            DBDictionary subDict = GetSubDictionary(tr, db, subDictName);
            if (subDict == null)
                return 0;

            // Collect handle keys first (safe iteration)
            var handles = new List<string>();
            foreach (DBDictionaryEntry entry in subDict)
                handles.Add(entry.Key);

            foreach (string handleStr in handles)
            {
                if (!TryGetObjectIdFromHandleString(context, db, handleStr, out ObjectId id))
                    continue;

                if (!IsValidReadableObject(tr, id))
                    continue;

                try
                {
                    Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                    if (ent == null)
                        continue;

                    ent.Erase();
                    deletedCount++;

                    // Remove NOD record if requested
                    if (removeHandlesFromNod && subDict.Contains(handleStr))
                    {
                        DBObject xr =
                            tr.GetObject(subDict.GetAt(handleStr), OpenMode.ForWrite);
                        xr.Erase();
                    }
                }
                catch
                {
                    // Ignore individual failures and continue
                }
            }

            return deletedCount;
        }

        internal int DeleteEntitiesFromFoundationSubDictionary(FoundationContext context,
            Database db,
            string subDictName,
            bool removeHandlesFromNod = true)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                int count = DeleteEntitiesFromFoundationSubDictionary(
                    context,
                    tr,
                    db,
                    subDictName,
                    removeHandlesFromNod);

                tr.Commit();
                return count;
            }
        }

        [CommandMethod("DeleteFoundationEntities")]
        public void DeleteFoundationEntitiesCommand(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var doc = context.Document;
            var model = context.Model;

            if (doc == null) return;

            var db = doc.Database;
            var ed = doc.Editor;

            PromptStringOptions pso =
                new PromptStringOptions("\nEnter foundation sub-dictionary:");
            pso.AllowSpaces = false;

            var res = ed.GetString(pso);
            if (res.Status != PromptStatus.OK)
                return;

            string sub = res.StringResult.Trim().ToUpperInvariant();

            int count = DeleteEntitiesFromFoundationSubDictionary(
                context,
                doc.Database,
                sub,
                removeHandlesFromNod: true);

            ed.WriteMessage($"\nDeleted {count} entities from {sub}.");
        }

        internal enum TraversalStatus
        {
            Success,
            InvalidHandle,
            MissingObjectId,
            ErasedObject,
            NotEntity
        }

        internal sealed class TraversalResult
        {
            public string Key { get; }
            public Handle Handle { get; }
            public ObjectId ObjectId { get; }
            public Entity Entity { get; }
            public TraversalStatus Status { get; }

            private TraversalResult(
                string key,
                TraversalStatus status,
                Handle handle = default,
                ObjectId objectId = default,
                Entity entity = null)
            {
                Key = key;
                Status = status;
                Handle = handle;
                ObjectId = objectId;
                Entity = entity;
            }

            public static TraversalResult Success(
                string key, Handle handle, ObjectId id, Entity ent) =>
                new TraversalResult(key, TraversalStatus.Success, handle, id, ent);

            public static TraversalResult InvalidHandle(string key) =>
                new TraversalResult(key, TraversalStatus.InvalidHandle);

            public static TraversalResult MissingObjectId(string key, Handle handle) =>
                new TraversalResult(key, TraversalStatus.MissingObjectId, handle);

            public static TraversalResult Erased(string key, Handle handle, ObjectId id) =>
                new TraversalResult(key, TraversalStatus.ErasedObject, handle, id);

            public static TraversalResult NotEntity(string key, Handle handle, ObjectId id) =>
                new TraversalResult(key, TraversalStatus.NotEntity, handle, id);
        }
    }
}
