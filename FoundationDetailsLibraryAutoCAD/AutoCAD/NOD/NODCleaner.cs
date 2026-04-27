using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Collections.Generic;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.NOD
{
    internal static class NODCleaner
    {
        public static void ClearFoundationNOD(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            Document doc = context.Document;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            // --- Confirm deletion ---
            PromptKeywordOptions pko = new PromptKeywordOptions(
                "\nWARNING: This will completely DELETE ALL EE_Foundation NOD data. Continue?")
            {
                AllowNone = false,
                Message = "\nConfirm deletion (Yes/No): "
            };

            pko.Keywords.Add("Yes");
            pko.Keywords.Add("No");

            var res = ed.GetKeywords(pko);
            if (res.Status != PromptStatus.OK || res.StringResult != "Yes")
            {
                ed.WriteMessage("\nOperation cancelled.");
                return;
            }

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    DBDictionary root = NODCore.GetFoundationRootDictionary(tr, db);

                    if (root == null)
                    {
                        ed.WriteMessage("\nEE_Foundation root dictionary not found.");
                        return;
                    }

                    int erasedEntities = 0;
                    int erasedNodes = 0;

                    // Copy keys first (IMPORTANT: avoid modifying collection while iterating)
                    var keys = new List<DBDictionaryEntry>();
                    foreach (DBDictionaryEntry entry in root)
                        keys.Add(entry);

                    foreach (var entry in keys)
                    {
                        ObjectId childId = entry.Value;
                        string key = entry.Key;

                        if (!childId.IsValid || childId.IsNull)
                            continue;

                        DBObject obj = tr.GetObject(childId, OpenMode.ForWrite, false);

                        if (obj is DBDictionary childDict)
                        {
                            // FULL recursive delete
                            NODCore.EraseDictionaryRecursive(
                                tr,
                                db,
                                childDict,
                                ref erasedEntities,
                                ref erasedNodes,
                                eraseEntities: true);

                            if (!childDict.IsErased)
                                childDict.Erase();
                        }
                        else
                        {
                            // If ever non-dictionary junk appears directly under root
                            obj.Erase();
                            erasedEntities++;
                        }

                        if (!root.IsWriteEnabled)
                            root.UpgradeOpen();

                        if (root.Contains(key))
                            root.Remove(key);

                        erasedNodes++;
                    }

                    ed.WriteMessage(
                        $"\nHard NOD wipe complete. Entities erased: {erasedEntities}, nodes removed: {erasedNodes}");

                    // Rebuild clean structure (optional but recommended)
                    NODCore.InitFoundationNOD(context, tr);

                    tr.Commit();
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nError clearing NOD: {ex.Message}");
                    tr.Abort();
                }
            }
        }
    }
}
