using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
        private const string ROOT = "EE_Foundation";
        private const string KEY_BOUNDARY = "FD_BOUNDARY";
        private const string KEY_GRADEBEAM = "FD_GRADEBEAM";

        // ==========================================================
        //  HELPER UTILITIES
        // ==========================================================

        /// <summary>
        /// Ensures a subdictionary exists, creates if missing.
        /// </summary>
        private static DBDictionary EnsureDictionary(Transaction tr, DBDictionary parent, string name)
        {
            if (!parent.Contains(name))
            {
                parent.UpgradeOpen();
                DBDictionary sub = new DBDictionary();
                parent.SetAt(name, sub);
                tr.AddNewlyCreatedDBObject(sub, true);

                Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                Editor ed = doc.Editor;
                ed.WriteMessage("\nAdded dictionary " + name);
                return sub;
            }
            return (DBDictionary)tr.GetObject(parent.GetAt(name), OpenMode.ForWrite);
        }

        /// <summary>
        /// Adds a handle as an Xrecord to a dictionary if it doesn't exist.
        /// </summary>
        private static void AddHandleToDictionary(Transaction tr, DBDictionary dict, string handle)
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

        /// <summary>
        /// Attempts to get an ObjectId from a handle string.
        /// </summary>
        private static bool TryGetObjectIdFromHandleString(Database db, string handleStr, out ObjectId id)
        {
            id = ObjectId.Null;
            if (string.IsNullOrWhiteSpace(handleStr)) return false;
            if (!TryParseHandleString(handleStr, out Handle h)) return false;

            try
            {
                id = db.GetObjectId(false, h, 0);
                return true;
            }
            catch
            {
                return false;
            }
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
        public static void InitFoundationNOD()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                    DBDictionary root = EnsureDictionary(tr, nod, ROOT);
                    EnsureDictionary(tr, root, KEY_BOUNDARY);
                    EnsureDictionary(tr, root, KEY_GRADEBEAM);
                    tr.Commit();
                    ed.WriteMessage("\nEE_Foundation NOD structure initialized successfully.");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nTransaction failed: {ex.Message}");
                }
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
            var entries = IterateFoundationNod(cleanStale: false);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== EE_Foundation Contents ===");
            foreach (var e in entries)
                sb.AppendLine($"[{e.GroupName}] {e.HandleKey} : {e.Status}");
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
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                    if (!nod.Contains(ROOT)) { MessageBox.Show("EE_Foundation dictionary does not exist."); return; }

                    DBDictionary root = (DBDictionary)tr.GetObject(nod.GetAt(ROOT), OpenMode.ForRead);
                    Dictionary<string, List<string>> data = new Dictionary<string, List<string>>();

                    foreach (DBDictionaryEntry group in root)
                    {
                        DBDictionary sub = (DBDictionary)tr.GetObject(group.Value, OpenMode.ForRead);
                        List<string> handles = new List<string>();
                        foreach (DBDictionaryEntry entry in sub) handles.Add(entry.Key);
                        data[group.Key] = handles;
                    }

                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(data, Newtonsoft.Json.Formatting.Indented);
                    File.WriteAllText("EE_Foundation.json", json);
                    tr.Commit();
                    MessageBox.Show("Exported to EE_Foundation.json");
                }
                catch (System.Exception ex)
                {
                    doc.Editor.WriteMessage($"\nTransaction failed: {ex.Message}");
                }
            }
        }

        [CommandMethod("ImportFoundationNOD")]
        public static void ImportFoundationNOD()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;

            if (!File.Exists("EE_Foundation.json")) { MessageBox.Show("EE_Foundation.json not found."); return; }

            string json = File.ReadAllText("EE_Foundation.json");
            var data = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(json);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                    DBDictionary root = EnsureDictionary(tr, nod, ROOT);

                    foreach (var kvp in data)
                    {
                        DBDictionary sub = EnsureDictionary(tr, root, kvp.Key);
                        foreach (string handle in kvp.Value)
                            if (IsValidHexHandleString(handle))
                                AddHandleToDictionary(tr, sub, handle);
                    }
                    tr.Commit();
                    MessageBox.Show("EE_Foundation.json imported.");
                }
                catch (System.Exception ex)
                {
                    doc.Editor.WriteMessage($"\nTransaction failed: {ex.Message}");
                }
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

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                    DBDictionary root = EnsureDictionary(tr, nod, ROOT);
                    DBDictionary boundaryDict = EnsureDictionary(tr, root, KEY_BOUNDARY);
                    DBDictionary gradebeamDict = EnsureDictionary(tr, root, KEY_GRADEBEAM);

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    // Create FD_BOUNDARY polyline
                    Polyline boundary = new Polyline();
                    boundary.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
                    boundary.AddVertexAt(1, new Point2d(100, 0), 0, 0, 0);
                    boundary.AddVertexAt(2, new Point2d(100, 50), 0, 0, 0);
                    boundary.AddVertexAt(3, new Point2d(0, 50), 0, 0, 0);
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
    }
}
