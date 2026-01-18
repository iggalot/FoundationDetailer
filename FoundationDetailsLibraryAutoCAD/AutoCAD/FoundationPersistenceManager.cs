using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using FoundationDetailer.Model;
using FoundationDetailsLibraryAutoCAD.AutoCAD.NOD;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD
{
    internal class FoundationPersistenceManager
    {
        public void Save(FoundationContext context)
        {
            ExportFoundationNOD(context);
        }

        public void Load(FoundationContext context)
        {
            ImportFoundationNOD(context);
        }

        public void Query(FoundationContext context)
        {
            NODCore.CleanFoundationNOD(context);
            NODViewer.ViewFoundationNOD(context);
        }

            // ==========================================================
        //  EXPORT / IMPORT JSON
        // ==========================================================
        public static void ExportFoundationNOD(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            Document doc = context.Document;
            Database db = doc.Database;

            string drawingFolder = Path.GetDirectoryName(doc.Name);
            string drawingName = Path.GetFileNameWithoutExtension(doc.Name);
            string jsonFile = Path.Combine(drawingFolder, $"{drawingName}_FDN_DATA.json");

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                    if (!nod.Contains(NODCore.ROOT))
                    {
                        MessageBox.Show("EE_Foundation dictionary does not exist.");
                        return;
                    }

                    DBDictionary root = (DBDictionary)tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForRead);

                    // Recursively export dictionary
                    var exportData = NODCore.ToDictionaryRepresentation(root, tr);

                    // Serialize to JSON
                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(exportData, Newtonsoft.Json.Formatting.Indented);
                    File.WriteAllText(jsonFile, json);

                    MessageBox.Show($"Export complete:\n{jsonFile}");
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    doc.Editor.WriteMessage($"\nExport failed: {ex.Message}");
                }
            }
        }
        /// <summary>
        /// A function that loads the associated JSON file with the NOD data.
        /// </summary>
        public static void ImportFoundationNOD(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var db = doc.Database;

            // Build JSON file path
            string drawingFolder = Path.GetDirectoryName(doc.Name);
            string drawingName = Path.GetFileNameWithoutExtension(doc.Name);
            string jsonFile = Path.Combine(drawingFolder, $"{drawingName}_FDN_DATA.json");

            if (!File.Exists(jsonFile))
            {
                MessageBox.Show($"{jsonFile} not found.");
                return;
            }

            // Read JSON safely
            string json = File.ReadAllText(jsonFile);
            Dictionary<string, object> importData = null;
            try
            {
                importData = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            }
            catch
            {
                MessageBox.Show("JSON is corrupted or invalid. Partial import may be possible.");
                importData = new Dictionary<string, object>();
            }

            if (importData == null)
                importData = new Dictionary<string, object>();

            using (doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // Ensure root NOD structure exists
                    NODCore.InitFoundationNOD(context, tr);
                    tr.Commit();
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                        DBDictionary root = (DBDictionary)tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForWrite);

                        // Recursively restore dictionary
                        foreach (var kvp in importData)
                        {
                            string key = kvp.Key;
                            object value = kvp.Value;

                            if (value is Newtonsoft.Json.Linq.JObject obj)
                            {
                                DBDictionary subDict = NODCore.GetOrCreateNestedSubDictionary(tr, root, key);
                                RestoreDictionaryFromJson(context, tr, subDict, obj, db);
                            }
                            else
                            {
                                // Skip invalid entries safely
                                doc.Editor.WriteMessage($"\nSkipping invalid top-level entry: {key}");
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
            }
        }

        /// <summary>
        /// Recursively restores subdictionaries, entities, and Xrecords from a JSON JObject.
        /// </summary>
        private static void RestoreDictionaryFromJson(
            FoundationContext context,
            Transaction tr,
            DBDictionary dict,
            Newtonsoft.Json.Linq.JObject jsonObj,
            Database db)
        {
            foreach (var kvp in jsonObj)
            {
                string key = kvp.Key;
                var value = kvp.Value as Newtonsoft.Json.Linq.JObject;
                if (value == null)
                    continue;

                string type = value.Value<string>("Type");

                try
                {
                    if (type == "Dictionary")
                    {
                        // Create or get subdictionary
                        DBDictionary subDict = NODCore.GetOrCreateNestedSubDictionary(tr, dict, key);
                        var children = value["Children"] as Newtonsoft.Json.Linq.JObject;
                        if (children != null)
                            RestoreDictionaryFromJson(context, tr, subDict, children, db);
                    }
                    else if (type == "XRecord")
                    {
                        Xrecord xr;
                        if (dict.Contains(key))
                            xr = tr.GetObject(dict.GetAt(key), OpenMode.ForWrite) as Xrecord;
                        else
                        {
                            xr = new Xrecord();
                            dict.SetAt(key, xr);
                            tr.AddNewlyCreatedDBObject(xr, true);
                        }

                        var dataArray = value["Data"] as Newtonsoft.Json.Linq.JArray;
                        if (dataArray != null)
                        {
                            var rb = new ResultBuffer();
                            foreach (var v in dataArray)
                                rb.Add(new TypedValue((int)DxfCode.Text, v.ToString()));
                            xr.Data = rb;
                        }
                    }
                    else if (type == "Entity")
                    {
                        string handleStr = value.Value<string>("Handle");
                        if (!string.IsNullOrEmpty(handleStr) && db.TryGetObjectId(new Handle(Convert.ToInt64(handleStr, 16)), out ObjectId id))
                        {
                            if (id.IsValid && !id.IsErased)
                            {
                                Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                                if (ent != null)
                                    dict.SetAt(key, ent); // attach entity
                            }
                        }
                    }
                }
                catch
                {
                    context.Document.Editor.WriteMessage($"\nSkipping invalid NOD item: {key}");
                }
            }
        }



    }
}