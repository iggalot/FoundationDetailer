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
        /// Function to add a polyline boundary handle to the NOD
        /// </summary>
        /// <param name="id"></param>
        public static void AddBoundaryHandle(ObjectId id)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application
                                .DocumentManager.MdiActiveDocument;
            Database db = doc.Database;

            using (doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // Ensure NOD structure exists
                    InitFoundationNOD(tr);

                    DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

                    DBDictionary root = (DBDictionary)tr.GetObject(nod.GetAt(ROOT), OpenMode.ForWrite);

                    DBDictionary boundaryDict = (DBDictionary)tr.GetObject(root.GetAt(KEY_BOUNDARY), OpenMode.ForWrite);

                    // Store handle (uppercase, same as everywhere else)
                    string handleStr = id.Handle.ToString().ToUpperInvariant();

                    // Use your existing helper
                    AddHandleToDictionary(tr, boundaryDict, handleStr);

                    tr.Commit();
                }
            }
        }

        /// <summary>
        /// Adds a grade beam polyline handle to the EE_Foundation NOD under FD_GRADEBEAM.
        /// </summary>
        /// <param name="id">The ObjectId of the grade beam polyline.</param>
        public static void AddGradeBeamHandle(ObjectId id)
        {
            if (id.IsNull || !id.IsValid) return;

            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;

            using (doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // Ensure EE_Foundation NOD and subdictionaries exist
                    InitFoundationNOD(tr);

                    DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                    DBDictionary root = (DBDictionary)tr.GetObject(nod.GetAt(ROOT), OpenMode.ForWrite);
                    DBDictionary gradebeamDict = (DBDictionary)tr.GetObject(root.GetAt(KEY_GRADEBEAM), OpenMode.ForWrite);

                    // Convert ObjectId handle to uppercase string
                    string handleStr = id.Handle.ToString().ToUpperInvariant();

                    // Add to NOD using existing helper
                    AddHandleToDictionary(tr, gradebeamDict, handleStr);

                    tr.Commit();
                }
            }
        }



        /// <summary>
        /// Validates that a string can be a hex handle.
        /// </summary>
        private static bool IsValidHexHandleString(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
            {
                bool digit = c >= '0' && c <= '9';
                bool hex = (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
                if (!digit && !hex) return false;
            }
            return true;
        }

        /// <summary>
        /// Attempts to parse a handle string to a Handle object.
        /// Accepts hex or decimal strings.
        /// </summary>
        private static bool TryParseHandleString(string s, out Handle handle)
        {
            handle = new Handle(0L);
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();

            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(2);

            if (long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long value) ||
                long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                handle = new Handle(value);
                return true;
            }

            return false;
        }

        // ==========================================================
        //  QueryNOD ENTRY REPRESENTATION
        // ==========================================================
        public class HandleEntry
        {
            public string GroupName;
            public string HandleKey;
            public ObjectId Id;
            public string Status; // "Valid", "Missing", "Invalid", "Error"
        }

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
                "Removed stale handles:\n" + string.Join("\n", removed) :
                "No stale handles found.";

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
                            List<string> handles = kvp.Value ?? new List<string>();

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

                            // Import handles
                            foreach (string handle in handles)
                            {
                                if (IsValidHexHandleString(handle))
                                {
                                    AddHandleToDictionary(tr, subDict, handle);
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

                CleanFoundationNOD();  // clean up stale handles that don't have an associated drawing object in the drawing.
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
        //  REMOVE SPECIFIC HANDLE
        // ==========================================================
        [CommandMethod("RemoveNODRecord")]
        public void RemoveNODRecord()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // Prompt for subdictionary
            PromptStringOptions pso_sub = new PromptStringOptions("\nEnter subdictionary to remove from [FD_BOUNDARY/FD_GRADEBEAM]:")
            {
                AllowSpaces = false
            };

            PromptResult resSub = ed.GetString(pso_sub);
            if (resSub.Status != PromptStatus.OK) return;

            string subDictName = resSub.StringResult.Trim().ToUpperInvariant();
            if (subDictName != KEY_BOUNDARY && subDictName != KEY_GRADEBEAM)
            {
                ed.WriteMessage("\nInvalid subdictionary. Must be FD_BOUNDARY or FD_GRADEBEAM.");
                return;
            }

            // Prompt for handle
            PromptStringOptions pso = new PromptStringOptions("\nEnter handle to remove:")
            {
                AllowSpaces = false
            };

            PromptResult resHandle = ed.GetString(pso);
            if (resHandle.Status != PromptStatus.OK) return;

            string handleStr = resHandle.StringResult.ToUpperInvariant();
            if (!IsValidHexHandleString(handleStr)) { ed.WriteMessage("\nInvalid handle string."); return; }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                    if (!nod.Contains(ROOT)) { ed.WriteMessage("\nEE_Foundation dictionary does not exist."); return; }

                    DBDictionary root = (DBDictionary)tr.GetObject(nod.GetAt(ROOT), OpenMode.ForWrite);
                    if (!root.Contains(subDictName)) { ed.WriteMessage($"\nSubdictionary {subDictName} does not exist."); return; }

                    DBDictionary subDict = (DBDictionary)tr.GetObject(root.GetAt(subDictName), OpenMode.ForWrite);
                    if (!subDict.Contains(handleStr)) { ed.WriteMessage($"\nHandle {handleStr} not found in {subDictName}."); return; }

                    Xrecord xr = (Xrecord)tr.GetObject(subDict.GetAt(handleStr), OpenMode.ForWrite);
                    xr.Erase();

                    tr.Commit();
                    ed.WriteMessage($"\nHandle {handleStr} removed from {subDictName}.");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nTransaction failed: {ex.Message}");
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
        /// Utility function that return a subdictionary of a specified name.
        /// </summary>
        /// <param name="tr"></param>
        /// <param name="name"></param>
        /// <param name="createIfMissing"></param>
        /// <returns></returns>
        // --------------------------------------------------------
        //  Get all handle strings from a subdictionary
        // --------------------------------------------------------
        internal static List<string> GetHandles(Transaction tr, DBDictionary subDict)
        {
            List<string> result = new List<string>();
            foreach (DBDictionaryEntry entry in subDict)
            {
                result.Add(entry.Key);
            }
            return result;
        }

        // --------------------------------------------------------
        //  Try convert handle string → ObjectId
        // --------------------------------------------------------
        internal static bool TryGetObjectIdFromHandleString(
            Database db, string handleStr, out ObjectId id)
        {
            id = ObjectId.Null;

            if (string.IsNullOrWhiteSpace(handleStr))
                return false;

            try
            {
                handleStr = handleStr.Trim();
                if (handleStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    handleStr = handleStr.Substring(2);

                long handleValue;
                if (!long.TryParse(handleStr, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out handleValue))
                    return false;

                Handle h = new Handle(handleValue);
                id = db.GetObjectId(false, h, 0);
                return id.IsValid;
            }
            catch
            {
                id = ObjectId.Null;
                return false;
            }
        }

        // --------------------------------------------------------
        //  Get all valid ObjectIds from a subdictionary
        // --------------------------------------------------------
        internal static List<ObjectId> GetValidObjectIds(
            Transaction tr, Database db, DBDictionary subDict)
        {
            List<ObjectId> validIds = new List<ObjectId>();
            List<string> handles = GetHandles(tr, subDict);

            foreach (string handleStr in handles)
            {
                ObjectId id;
                if (TryGetObjectIdFromHandleString(db, handleStr, out id))
                {
                    try
                    {
                        DBObject obj = tr.GetObject(id, OpenMode.ForRead, false);
                        if (obj != null && !obj.IsErased)
                            validIds.Add(id);
                    }
                    catch
                    {
                        // ignore invalid objects
                    }
                }
            }

            return validIds;
        }

        // --------------------------------------------------------
        //  Get a single ObjectId from a subdictionary (FD_BOUNDARY)
        // --------------------------------------------------------
        internal static ObjectId GetSingleObjectId(
            Transaction tr, Database db, DBDictionary subDict)
        {
            List<ObjectId> ids = GetValidObjectIds(tr, db, subDict);
            if (ids.Count > 0)
                return ids[0];
            return ObjectId.Null;
        }

        internal static bool TryParseHandle(string handleString, out Handle handle)
        {
            handle = new Handle(0);
            if (string.IsNullOrWhiteSpace(handleString)) return false;

            // Try hex (without prefix)
            if (long.TryParse(handleString, System.Globalization.NumberStyles.HexNumber, null, out long hexValue))
            {
                handle = new Handle(hexValue);
                return true;
            }

            // Try decimal
            if (long.TryParse(handleString, out long dec))
            {
                handle = new Handle(dec);
                return true;
            }

            // Try hex with "0x" prefix
            try
            {
                var trimmed = handleString.Trim();
                if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                    long.TryParse(trimmed.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out long prefixed))
                {
                    handle = new Handle(prefixed);
                    return true;
                }
            }
            catch { }

            return false;
        }


    }
}
