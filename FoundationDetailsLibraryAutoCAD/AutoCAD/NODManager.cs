using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;

[assembly: CommandClass(typeof(FoundationDetailsLibraryAutoCAD.AutoCAD.NODManager))]

namespace FoundationDetailsLibraryAutoCAD.AutoCAD
{
    public class NODManager
    {
        private static readonly string ROOT = "EE_Foundation";
        private static readonly string KEY_BOUNDARY = "FD_BOUNDARY";
        private static readonly string KEY_GRADEBEAM = "FD_GRADEBEAM";

        // ==========================================================
        //  1.  HELPER UTILITIES
        // ==========================================================

        private static bool IsValidHexHandleString(string s)
        {
            if (string.IsNullOrEmpty(s))
                return false;

            foreach (char c in s)
            {
                bool digit = (c >= '0' && c <= '9');
                bool hex = (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');

                if (!digit && !hex)
                    return false;
            }
            return true;
        }

        private static bool TryGetObjectIdFromHandleString(Database db, string handleStr, out ObjectId id)
        {
            id = ObjectId.Null;

            if (!IsValidHexHandleString(handleStr))
                return false;

            try
            {
                long value = Convert.ToInt64(handleStr, 16);
                Handle h = new Handle(value);
                id = db.GetObjectId(false, h, 0);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static DBDictionary EnsureDictionary(Transaction tr, DBDictionary parent, string name)
        {
            if (!parent.Contains(name))
            {
                parent.UpgradeOpen();
                DBDictionary sub = new DBDictionary();
                parent.SetAt(name, sub);
                tr.AddNewlyCreatedDBObject(sub, true);
                return sub;
            }
            return (DBDictionary)tr.GetObject(parent.GetAt(name), OpenMode.ForWrite);
        }

        // Placeholder – users define how entities belong in groups
        private static bool ShouldBelongToKey(Entity ent, string key)
        {
            // TODO: Replace this with your real logic.
            // For now: nothing automatically qualifies.
            return false;
        }


        // ==========================================================
        //  2.  COMMAND: INITIALIZE FOUNDATION STRUCTURE
        // ==========================================================

        [CommandMethod("InitFoundationNOD")]
        public static void InitFoundationNOD()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

                DBDictionary root = EnsureDictionary(tr, nod, ROOT);
                EnsureDictionary(tr, root, KEY_BOUNDARY);
                EnsureDictionary(tr, root, KEY_GRADEBEAM);

                tr.Commit();
            }

            doc.Editor.WriteMessage("\nEE_Foundation NOD structure initialized.");
        }


        // ==========================================================
        //  3.  COMMAND: SYNC FOUNDATION NOD
        // ==========================================================

        [CommandMethod("SyncFoundationNOD")]
        public static void SyncFoundationNOD()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;

            int removed = 0;
            int added = 0;
            int normalized = 0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

                if (!nod.Contains(ROOT))
                {
                    MessageBox.Show("EE_Foundation dictionary does not exist. Run InitFoundationNOD first.");
                    return;
                }

                DBDictionary root = (DBDictionary)tr.GetObject(nod.GetAt(ROOT), OpenMode.ForWrite);

                string[] subkeys = { KEY_BOUNDARY, KEY_GRADEBEAM };

                // Get ModelSpace
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (string key in subkeys)
                {
                    if (!root.Contains(key))
                        continue;

                    DBDictionary sub = (DBDictionary)tr.GetObject(root.GetAt(key), OpenMode.ForWrite);
                    List<string> toRemove = new List<string>();

                    // STEP 1 — Clean existing entries
                    foreach (DBDictionaryEntry entry in sub)
                    {
                        string handleStr = entry.Key;
                        ObjectId id;

                        // Remove malformed handles
                        if (!IsValidHexHandleString(handleStr))
                        {
                            toRemove.Add(handleStr);
                            continue;
                        }

                        // Remove broken resolver
                        if (!TryGetObjectIdFromHandleString(db, handleStr, out id))
                        {
                            toRemove.Add(handleStr);
                            continue;
                        }

                        // Remove missing objects
                        Entity ent = null;
                        try { ent = tr.GetObject(id, OpenMode.ForRead) as Entity; } catch { }

                        if (ent == null)
                        {
                            toRemove.Add(handleStr);
                            continue;
                        }

                        // Normalize to uppercase
                        // Normalize to uppercase
                        string normalizedKey = id.Handle.ToString().ToUpperInvariant();
                        if (normalizedKey != handleStr)
                        {
                            // Get original record
                            Xrecord xr = (Xrecord)tr.GetObject(sub.GetAt(handleStr), OpenMode.ForWrite);

                            // Add under normalized key
                            sub.SetAt(normalizedKey, xr);

                            // Remove old key
                            sub.Remove(handleStr);

                            normalized++; // <- increment your counter variable
                        }

                    }

                    // Remove bad entries
                    foreach (string keyToDelete in toRemove)
                    {
                        Xrecord xr = (Xrecord)tr.GetObject(sub.GetAt(keyToDelete), OpenMode.ForWrite);
                        xr.Erase();
                        removed++;
                    }

                    // STEP 2 — Add missing items (based on user logic)
                    foreach (ObjectId id in ms)
                    {
                        Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        if (!ShouldBelongToKey(ent, key))
                            continue;

                        string handleKey = id.Handle.ToString().ToUpperInvariant();

                        if (!sub.Contains(handleKey))
                        {
                            Xrecord xr = new Xrecord();
                            xr.Data = new ResultBuffer(new TypedValue((int)DxfCode.Handle, handleKey));
                            sub.SetAt(handleKey, xr);
                            tr.AddNewlyCreatedDBObject(xr, true);
                            added++;
                        }
                    }
                }

                tr.Commit();
            }

            MessageBox.Show(
                $"Sync Complete\nRemoved: {removed}\nAdded: {added}\nNormalized: {normalized}",
                "EE_Foundation Sync"
            );
        }


        // ==========================================================
        //  4.  VIEWER COMMAND
        // ==========================================================

        [CommandMethod("ViewFoundationNOD")]
        public static void ViewFoundationNOD()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

                if (!nod.Contains(ROOT))
                {
                    MessageBox.Show("EE_Foundation dictionary does not exist.");
                    return;
                }

                DBDictionary root = (DBDictionary)tr.GetObject(nod.GetAt(ROOT), OpenMode.ForRead);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("=== EE_Foundation Contents ===");

                foreach (DBDictionaryEntry group in root)
                {
                    sb.AppendLine($"\n[{group.Key}]");
                    DBDictionary sub = (DBDictionary)tr.GetObject(group.Value, OpenMode.ForRead);

                    foreach (DBDictionaryEntry entry in sub)
                    {
                        string handleStr = entry.Key;
                        sb.Append($"  {handleStr} : ");

                        ObjectId id;
                        if (!TryGetObjectIdFromHandleString(db, handleStr, out id))
                        {
                            sb.AppendLine("INVALID HANDLE");
                            continue;
                        }

                        try
                        {
                            Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                            if (ent == null)
                                sb.AppendLine("Missing object");
                            else
                                sb.AppendLine(ent.GetType().Name);
                        }
                        catch
                        {
                            sb.AppendLine("Error reading object");
                        }
                    }
                }

                MessageBox.Show(sb.ToString(), "EE_Foundation Viewer");
            }
        }


        // ==========================================================
        //  5.  CLEANUP COMMAND (remove entire structure)
        // ==========================================================

        [CommandMethod("EraseFoundationNOD")]
        public static void EraseFoundationNOD()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

                if (nod.Contains(ROOT))
                {
                    DBDictionary root = (DBDictionary)tr.GetObject(nod.GetAt(ROOT), OpenMode.ForWrite);
                    root.Erase();
                }

                tr.Commit();
            }

            MessageBox.Show("EE_Foundation dictionary erased.");
        }


        // ==========================================================
        //  6.  JSON EXPORT
        // ==========================================================

        [CommandMethod("ExportFoundationNOD")]
        public static void ExportFoundationNOD()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

                if (!nod.Contains(ROOT))
                {
                    MessageBox.Show("EE_Foundation dictionary does not exist.");
                    return;
                }

                DBDictionary root = (DBDictionary)tr.GetObject(nod.GetAt(ROOT), OpenMode.ForRead);

                Dictionary<string, List<string>> data =
                    new Dictionary<string, List<string>>();

                foreach (DBDictionaryEntry group in root)
                {
                    DBDictionary sub = (DBDictionary)tr.GetObject(group.Value, OpenMode.ForRead);
                    List<string> handles = new List<string>();
                    foreach (DBDictionaryEntry entry in sub)
                    {
                        handles.Add(entry.Key);
                    }

                    data[group.Key] = handles;
                }

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(
                    data, Newtonsoft.Json.Formatting.Indented);

                File.WriteAllText("EE_Foundation.json", json);

                MessageBox.Show("Exported to EE_Foundation.json");
            }
        }


        // ==========================================================
        //  7.  JSON IMPORT
        // ==========================================================

        [CommandMethod("ImportFoundationNOD")]
        public static void ImportFoundationNOD()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;

            if (!File.Exists("EE_Foundation.json"))
            {
                MessageBox.Show("EE_Foundation.json not found.");
                return;
            }

            string json = File.ReadAllText("EE_Foundation.json");

            var data = Newtonsoft.Json.JsonConvert.DeserializeObject<
                Dictionary<string, List<string>>>(json);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary nod =
                    (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

                DBDictionary root = EnsureDictionary(tr, nod, ROOT);

                foreach (var kvp in data)
                {
                    DBDictionary sub = EnsureDictionary(tr, root, kvp.Key);

                    foreach (string handle in kvp.Value)
                    {
                        if (!IsValidHexHandleString(handle))
                            continue;

                        if (!sub.Contains(handle))
                        {
                            Xrecord xr = new Xrecord();
                            xr.Data = new ResultBuffer(new TypedValue((int)DxfCode.Handle, handle));
                            sub.SetAt(handle, xr);
                            tr.AddNewlyCreatedDBObject(xr, true);
                        }
                    }
                }

                tr.Commit();
            }

            MessageBox.Show("EE_Foundation.json imported.");
        }

        [CommandMethod("CreateSampleFoundationForNOD")]
        public void CreateSampleFoundationForNOD()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Get the Named Objects Dictionary
                DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                DBDictionary root = NODManager.EnsureDictionary(tr, nod, ROOT);

                // Ensure FD_BOUNDARY and FD_GRADEBEAM dictionaries
                DBDictionary boundaryDict = NODManager.EnsureDictionary(tr, root, KEY_BOUNDARY);
                DBDictionary gradebeamDict = NODManager.EnsureDictionary(tr, root, KEY_GRADEBEAM);

                // Get ModelSpace
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // ----- 1. Create FD_BOUNDARY polyline -----
                Polyline boundary = new Polyline();
                boundary.AddVertexAt(0, new Autodesk.AutoCAD.Geometry.Point2d(0, 0), 0, 0, 0);
                boundary.AddVertexAt(1, new Autodesk.AutoCAD.Geometry.Point2d(100, 0), 0, 0, 0);
                boundary.AddVertexAt(2, new Autodesk.AutoCAD.Geometry.Point2d(100, 50), 0, 0, 0);
                boundary.AddVertexAt(3, new Autodesk.AutoCAD.Geometry.Point2d(0, 50), 0, 0, 0);
                boundary.Closed = true;

                ms.AppendEntity(boundary);
                tr.AddNewlyCreatedDBObject(boundary, true);

                // Register in FD_BOUNDARY dictionary
                string boundaryKey = boundary.Handle.ToString().ToUpperInvariant();
                if (!boundaryDict.Contains(boundaryKey))
                {
                    Xrecord xr = new Xrecord();
                    xr.Data = new ResultBuffer(new TypedValue((int)DxfCode.Handle, boundaryKey));
                    boundaryDict.SetAt(boundaryKey, xr);
                    tr.AddNewlyCreatedDBObject(xr, true);
                }

                // ----- 2. Create 3-4 FD_GRADEBEAM polylines -----
                for (int i = 0; i < 4; i++)
                {
                    Polyline gradebeam = new Polyline();
                    int y = 10 + i * 10;

                    gradebeam.AddVertexAt(0, new Autodesk.AutoCAD.Geometry.Point2d(10, y), 0, 0, 0);
                    gradebeam.AddVertexAt(1, new Autodesk.AutoCAD.Geometry.Point2d(90, y), 0, 0, 0);

                    ms.AppendEntity(gradebeam);
                    tr.AddNewlyCreatedDBObject(gradebeam, true);

                    // Register in FD_GRADEBEAM dictionary
                    string gbKey = gradebeam.Handle.ToString().ToUpperInvariant();
                    if (!gradebeamDict.Contains(gbKey))
                    {
                        Xrecord xr = new Xrecord();
                        xr.Data = new ResultBuffer(new TypedValue((int)DxfCode.Handle, gbKey));
                        gradebeamDict.SetAt(gbKey, xr);
                        tr.AddNewlyCreatedDBObject(xr, true);
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage("\nSample polylines created for FD_BOUNDARY and FD_GRADEBEAM.");
        }

        [CommandMethod("RemoveNODRecord")]
        public void RemoveNODRecord()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // Prompt for subdictionary
            PromptStringOptions pso_sub = new PromptStringOptions(
                "\nEnter subdictionary to remove from [FD_BOUNDARY/FD_GRADEBEAM]:");
            pso_sub.AllowSpaces = false;

            PromptResult resSub = ed.GetString(pso_sub);
            if (resSub.Status != PromptStatus.OK) return;

            string subDictName = resSub.StringResult.Trim().ToUpperInvariant();

            // Validate input
            if (subDictName != KEY_BOUNDARY && subDictName != KEY_GRADEBEAM)
            {
                ed.WriteMessage("\nInvalid subdictionary. Must be FD_BOUNDARY or FD_GRADEBEAM.");
                return;
            }

            // Prompt for handle
            PromptStringOptions pso = new PromptStringOptions($"\nEnter handle of the object to remove from {subDictName}:");
            pso.AllowSpaces = false;
            PromptResult resHandle = ed.GetString(pso);
            if (resHandle.Status != PromptStatus.OK) return;

            string handleStr = resHandle.StringResult.ToUpperInvariant();

            if (!IsValidHexHandleString(handleStr))
            {
                ed.WriteMessage("\nInvalid handle string.");
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

                if (!nod.Contains(ROOT))
                {
                    ed.WriteMessage("\nEE_Foundation dictionary does not exist.");
                    return;
                }

                DBDictionary root = (DBDictionary)tr.GetObject(nod.GetAt(ROOT), OpenMode.ForWrite);

                if (!root.Contains(subDictName))
                {
                    ed.WriteMessage($"\nSubdictionary {subDictName} does not exist.");
                    return;
                }

                DBDictionary subDict = (DBDictionary)tr.GetObject(root.GetAt(subDictName), OpenMode.ForWrite);

                if (!subDict.Contains(handleStr))
                {
                    ed.WriteMessage($"\nHandle {handleStr} not found in {subDictName}.");
                    return;
                }

                // Erase the Xrecord
                Xrecord xr = (Xrecord)tr.GetObject(subDict.GetAt(handleStr), OpenMode.ForWrite);
                xr.Erase();

                ed.WriteMessage($"\nHandle {handleStr} removed from {subDictName}.");

                tr.Commit();
            }
        }


    }
}
