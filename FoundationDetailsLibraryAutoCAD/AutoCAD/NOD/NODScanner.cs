using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Collections;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.NOD
{
    internal class NODScanner
    {
        [CommandMethod("FD_INSPECT_NOD")]
        public static void InspectFoundationNOD(FoundationContext context)
        {
            if(context == null) {throw new ArgumentNullException("context");}

            var doc = context.Document;

            if (doc == null) { throw new ArgumentNullException("document"); }

            var db = doc.Database;
            var ed = doc.Editor;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var sb = new System.Text.StringBuilder();

                var nod = tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead) as DBDictionary;

                if (nod == null || !nod.Contains(NODCore.ROOT))
                {
                    ed.WriteMessage("\nEE_Foundation NOD not found.");
                    return;
                }

                var root = tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForRead) as DBDictionary;

                sb.AppendLine("NOD");
                sb.AppendLine($"└─ {NODCore.ROOT}");

                PrintDictionaryDeep(tr, db, root, sb, 1);

                ed.WriteMessage("\n" + sb.ToString());

                tr.Commit();
            }
        }

        private static void PrintDictionaryDeep(
    Transaction tr,
    Database db,
    DBDictionary dict,
    System.Text.StringBuilder sb,
    int level)
        {
            if (dict == null)
                return;

            string indent = new string(' ', level * 3);

            foreach (DictionaryEntry entry in dict)
            {
                if (!(entry.Key is string key))
                    continue;

                if (!(entry.Value is ObjectId id))
                    continue;

                if (!id.IsValid)
                {
                    sb.AppendLine($"{indent}└─ {key} (INVALID)");
                    continue;
                }

                DBObject obj = null;

                try
                {
                    obj = tr.GetObject(id, OpenMode.ForRead, false);
                }
                catch
                {
                    sb.AppendLine($"{indent}└─ {key} (ERROR OPENING)");
                    continue;
                }

                if (obj == null)
                {
                    sb.AppendLine($"{indent}└─ {key} (NULL)");
                    continue;
                }

                if (obj.IsErased)
                {
                    sb.AppendLine($"{indent}└─ {key} (ERASED)");
                    continue;
                }

                // ------------------------------
                // Subdictionary
                // ------------------------------
                if (obj is DBDictionary subDict)
                {
                    sb.AppendLine($"{indent}└─ {key}");
                    PrintDictionaryDeep(tr, db, subDict, sb, level + 1);
                }

                // ------------------------------
                // Xrecord
                // ------------------------------
                else if (obj is Xrecord xr)
                {
                    sb.Append($"{indent}└─ {key} (Xrecord)");

                    if (xr.Data != null)
                    {
                        var values = xr.Data.AsArray();

                        foreach (var tv in values)
                        {
                            sb.Append($" [{tv.TypeCode}:{tv.Value}]");

                            // If value looks like a handle, try resolving
                            if (tv.TypeCode == (int)DxfCode.Text && tv.Value is string handleStr)
                            {
                                try
                                {
                                    long handleVal = Convert.ToInt64(handleStr, 16);
                                    var handle = new Handle(handleVal);

                                    ObjectId entId = db.GetObjectId(false, handle, 0);

                                    if (!entId.IsNull)
                                    {
                                        var ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;
                                        if (ent != null)
                                        {
                                            sb.Append($" -> {ent.GetType().Name}");
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }

                    sb.AppendLine();
                }

                // ------------------------------
                // Entity
                // ------------------------------
                else if (obj is Entity ent)
                {
                    sb.AppendLine($"{indent}└─ {key} (Entity: {ent.GetType().Name}, Handle={ent.Handle})");
                }

                else
                {
                    sb.AppendLine($"{indent}└─ {key} ({obj.GetType().Name})");
                }
            }
        }

    }
}
