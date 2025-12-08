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
        public const string ROOT = "EE_Foundation";
        public const string KEY_BOUNDARY = "FD_BOUNDARY";
        public const string KEY_GRADEBEAM = "FD_GRADEBEAM";
        // ==========================================================
        //  KNOWN SUBDICTIONARIES
        // ==========================================================
        private static readonly string[] KNOWN_SUBDICTIONARIES = { KEY_BOUNDARY, KEY_GRADEBEAM };

        // ==========================================================
        //  HELPER UTILITIES
        // ==========================================================

        /// <summary>
        /// Ensures a subdictionary exists under a parent dictionary. Returns the dictionary and a flag indicating if it was newly created.
        /// This method is eLock-safe: it upgrades to write only if necessary.
        /// </summary>
        private static (DBDictionary Dict, bool Created) EnsureDictionary(Transaction tr, DBDictionary parent, string name)
        {
            if (parent.Contains(name))
            {
                DBDictionary existing = (DBDictionary)tr.GetObject(parent.GetAt(name), OpenMode.ForWrite);
                return (existing, false);
            }

            if (!parent.IsWriteEnabled)
                parent.UpgradeOpen();

            DBDictionary sub = new DBDictionary();
            parent.SetAt(name, sub);
            tr.AddNewlyCreatedDBObject(sub, true);

            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            ed.WriteMessage("\nAdded dictionary " + name);

            return (sub, true);
        }

        /// <summary>
        /// Adds a handle as an Xrecord to a dictionary if it doesn't exist. Automatically upgrades dictionary to write if needed.
        /// </summary>
        private static void AddHandleToDictionary(Transaction tr, DBDictionary dict, string handle)
        {
            if (!dict.IsWriteEnabled)
                dict.UpgradeOpen();

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
        //  TRANSACTION GUARD
        // ==========================================================
        private class TransactionGuard : IDisposable
        {
            [ThreadStatic] private static int _activeTransactionCount;
            public bool IsOuterTransaction { get; }
            public Transaction Tr { get; }

            public TransactionGuard(Database db, bool forWrite = false)
            {
                if (_activeTransactionCount > 0)
                    throw new InvalidOperationException("Nested transaction detected!");

                IsOuterTransaction = true;
                _activeTransactionCount++;
                Tr = db.TransactionManager.StartTransaction();
            }

            public void Commit() => Tr?.Commit();

            public void Dispose()
            {
                Tr?.Dispose();
                if (IsOuterTransaction)
                    _activeTransactionCount--;
            }

            public static bool HasActiveTransaction => _activeTransactionCount > 0;
        }

        // ==========================================================
        //  QueryNODData ENTRY REPRESENTATION
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

            using (var guard = new TransactionGuard(db))
            {
                try
                {
                    DBDictionary nod = (DBDictionary)guard.Tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

                    var (root, createdRoot) = EnsureDictionary(guard.Tr, nod, ROOT);
                    if (createdRoot) ed.WriteMessage("\nCreated ROOT dictionary");

                    var (_, _) = EnsureDictionary(guard.Tr, root, KEY_BOUNDARY);
                    var (_, _) = EnsureDictionary(guard.Tr, root, KEY_GRADEBEAM);

                    guard.Commit();
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

            using (var guard = new TransactionGuard(db))
            {
                try
                {
                    DBDictionary nod = guard.Tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead) as DBDictionary;
                    if (nod == null || !nod.Contains(ROOT)) return results;

                    DBDictionary root = guard.Tr.GetObject(nod.GetAt(ROOT), OpenMode.ForRead) as DBDictionary;

                    foreach (DBDictionaryEntry group in root)
                    {
                        DBDictionary sub = guard.Tr.GetObject(group.Value, OpenMode.ForRead) as DBDictionary;
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
                                    Entity ent = guard.Tr.GetObject(id, OpenMode.ForRead) as Entity;
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
                    guard.Commit();
                }
                catch
                {
                    // transaction auto-aborts on dispose
                }
            }

            return results;
        }

        // ==========================================================
        //  VIEW QueryNODData CONTENT
        // ==========================================================
        [CommandMethod("ViewFoundationNOD")]
        public static void ViewFoundationNOD()
        {
            var entries = IterateFoundationNod(cleanStale: false);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== EE_Foundation Contents ===");

            foreach (string subDict in KNOWN_SUBDICTIONARIES)
            {
                var subEntries = entries.FindAll(e => e.GroupName.Equals(subDict, StringComparison.OrdinalIgnoreCase));

                if (subEntries.Count == 0)
                    sb.AppendLine($"[{subDict}] No records");
                else
                    foreach (var e in subEntries)
                        sb.AppendLine($"[{e.GroupName}] {e.HandleKey} : {e.Status}");
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
        //  REMOVE ENTIRE QueryNODData STRUCTURE
        // ==========================================================
        [CommandMethod("EraseFoundationNOD")]
        public static void EraseFoundationNOD()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;

            using (var guard = new TransactionGuard(db))
            {
                try
                {
                    DBDictionary nod = (DBDictionary)guard.Tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                    if (nod.Contains(ROOT))
                    {
                        DBDictionary root = (DBDictionary)guard.Tr.GetObject(nod.GetAt(ROOT), OpenMode.ForWrite);
                        root.Erase();
                    }
                    guard.Commit();
                    MessageBox.Show("EE_Foundation dictionary erased.");
                }
                catch (System.Exception ex)
                {
                    doc.Editor.WriteMessage($"\nTransaction failed: {ex.Message}");
                }
            }
        }

        public static void EraseFoundationSubDictionary(string subDictionaryName)
        {
            if (string.IsNullOrWhiteSpace(subDictionaryName))
                throw new ArgumentException("Subdictionary name cannot be null or empty.", nameof(subDictionaryName));

            subDictionaryName = subDictionaryName.Trim().ToUpperInvariant();

            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (var guard = new TransactionGuard(db))
            {
                try
                {
                    DBDictionary nod = (DBDictionary)guard.Tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

                    if (!nod.Contains(ROOT))
                    {
                        ed.WriteMessage("\nEE_Foundation root dictionary does not exist.");
                        return;
                    }

                    DBDictionary root = (DBDictionary)guard.Tr.GetObject(nod.GetAt(ROOT), OpenMode.ForWrite);

                    if (!root.Contains(subDictionaryName))
                    {
                        ed.WriteMessage($"\nSubdictionary {subDictionaryName} does not exist.");
                        return;
                    }

                    DBDictionary subDict = (DBDictionary)guard.Tr.GetObject(root.GetAt(subDictionaryName), OpenMode.ForWrite);
                    subDict.Erase();
                    guard.Commit();

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

            string drawingFolder = Path.GetDirectoryName(doc.Name);
            string drawingName = Path.GetFileNameWithoutExtension(doc.Name);
            string jsonFileName = drawingName + "_FDN_DATA.json";
            string jsonFilePath = Path.Combine(drawingFolder, jsonFileName);

            using (var guard = new TransactionGuard(db))
            {
                try
                {
                    DBDictionary nod = (DBDictionary)guard.Tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

                    if (!nod.Contains(ROOT))
                    {
                        MessageBox.Show("EE_Foundation dictionary does not exist.");
                        return;
                    }

                    DBDictionary root = (DBDictionary)guard.Tr.GetObject(nod.GetAt(ROOT), OpenMode.ForRead);

                    Dictionary<string, List<string>> data = new Dictionary<string, List<string>>();

                    foreach (DBDictionaryEntry group in root)
                    {
                        DBDictionary sub = (DBDictionary)guard.Tr.GetObject(group.Value, OpenMode.ForRead);
                        List<string> handles = new List<string>();
                        foreach (DBDictionaryEntry entry in sub)
                            handles.Add(entry.Key);

                        data[group.Key] = handles;
                    }

                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(data, Newtonsoft.Json.Formatting.Indented);
                    File.WriteAllText(jsonFilePath, json);

                    guard.Commit();
                    MessageBox.Show($"Exported EE_Foundation data to:\n{jsonFilePath}");
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

            string drawingFolder = Path.GetDirectoryName(doc.Name);
            string drawingName = Path.GetFileNameWithoutExtension(doc.Name);

            string jsonFileName = Path.Combine(drawingFolder, $"{drawingName}_FDN_DATA.json");

            if (!File.Exists(jsonFileName))
            {
                MessageBox.Show($"{jsonFileName} not found.");
                return;
            }

            string json = File.ReadAllText(jsonFileName);
            var data = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(json);

            using (var guard = new TransactionGuard(db))
            {
                try
                {
                    DBDictionary nod = (DBDictionary)guard.Tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                    var (root, _) = EnsureDictionary(guard.Tr, nod, ROOT);

                    foreach (var kvp in data)
                    {
                        var (sub, _) = EnsureDictionary(guard.Tr, root, kvp.Key);

                        foreach (string handle in kvp.Value)
                        {
                            if (IsValidHexHandleString(handle))
                                AddHandleToDictionary(guard.Tr, sub, handle);
                        }
                    }

                    guard.Commit();
                    MessageBox.Show($"{jsonFileName} imported successfully.");
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

            using (var guard = new TransactionGuard(db))
            {
                try
                {
                    DBDictionary nod = (DBDictionary)guard.Tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                    var (root, _) = EnsureDictionary(guard.Tr, nod, ROOT);
                    var (boundaryDict, _) = EnsureDictionary(guard.Tr, root, KEY_BOUNDARY);
                    var (gradebeamDict, _) = EnsureDictionary(guard.Tr, root, KEY_GRADEBEAM);

                    BlockTable bt = (BlockTable)guard.Tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord ms = (BlockTableRecord)guard.Tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    Polyline boundary = new Polyline();
                    boundary.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
                    boundary.AddVertexAt(1, new Point2d(100, 0), 0, 0, 0);
                    boundary.AddVertexAt(2, new Point2d(100, 50), 0, 0, 0);
                    boundary.AddVertexAt(3, new Point2d(0, 50), 0, 0, 0);
                    boundary.Closed = true;

                    ms.AppendEntity(boundary);
                    guard.Tr.AddNewlyCreatedDBObject(boundary, true);
                    AddHandleToDictionary(guard.Tr, boundaryDict, boundary.Handle.ToString().ToUpperInvariant());

                    for (int i = 0; i < 4; i++)
                    {
                        Polyline gb = new Polyline();
                        int y = 10 + i * 10;
                        gb.AddVertexAt(0, new Point2d(10, y), 0, 0, 0);
                        gb.AddVertexAt(1, new Point2d(90, y), 0, 0, 0);

                        ms.AppendEntity(gb);
                        guard.Tr.AddNewlyCreatedDBObject(gb, true);
                        AddHandleToDictionary(guard.Tr, gradebeamDict, gb.Handle.ToString().ToUpperInvariant());
                    }
                    guard.Commit();
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

            PromptStringOptions pso = new PromptStringOptions("\nEnter handle to remove:")
            {
                AllowSpaces = false
            };

            PromptResult resHandle = ed.GetString(pso);
            if (resHandle.Status != PromptStatus.OK) return;

            string handleStr = resHandle.StringResult.ToUpperInvariant();
            if (!IsValidHexHandleString(handleStr)) { ed.WriteMessage("\nInvalid handle string."); return; }

            using (var guard = new TransactionGuard(db))
            {
                try
                {
                    DBDictionary nod = (DBDictionary)guard.Tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                    if (!nod.Contains(ROOT)) { ed.WriteMessage("\nEE_Foundation dictionary does not exist."); return; }

                    DBDictionary root = (DBDictionary)guard.Tr.GetObject(nod.GetAt(ROOT), OpenMode.ForWrite);
                    if (!root.Contains(subDictName)) { ed.WriteMessage($"\nSubdictionary {subDictName} does not exist."); return; }

                    DBDictionary subDict = (DBDictionary)guard.Tr.GetObject(root.GetAt(subDictName), OpenMode.ForWrite);
                    if (!subDict.Contains(handleStr)) { ed.WriteMessage($"\nHandle {handleStr} not found in {subDictName}."); return; }

                    Xrecord xr = (Xrecord)guard.Tr.GetObject(subDict.GetAt(handleStr), OpenMode.ForWrite);
                    xr.Erase();

                    guard.Commit();
                    ed.WriteMessage($"\nHandle {handleStr} removed from {subDictName}.");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nTransaction failed: {ex.Message}");
                }
            }
        }

        // ==========================================================
        //  CLEAN ALL QueryNODData ENTRIES
        // ==========================================================
        [CommandMethod("NODCleaner")]
        public static void NODCleanAll()
        {
            IterateFoundationNod(true);
        }
    }
}
