using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using FoundationDetailsLibraryAutoCAD.Data;
using System;

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

            // --- Confirm deletion with user ---
            PromptKeywordOptions pko = new PromptKeywordOptions(
                "\nWARNING: This will completely clear the EE_Foundation NOD. Are you sure?")
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
                    // --- Get the EE_Foundation root dictionary
                    DBDictionary root = NODCore.GetFoundationRootDictionary(tr, db);

                    if (root != null)
                    {
                        int edgesDeleted = 0;
                        int beamsDeleted = 0;

                        foreach (string subKey in NODCore.KNOWN_ROOT_SUBDIRS)
                        {
                            DBDictionary subDict;
                            if (NODCore.TryGetNestedSubDictionary(tr, root, out subDict, subKey))
                            {
                                // Recursively erase contents
                                NODCore.EraseDictionaryRecursive(tr, db, subDict, ref edgesDeleted, ref beamsDeleted, eraseEntities: true);

                                // Upgrade subDict before erasing
                                if (!subDict.IsErased)
                                {
                                    if (!subDict.IsWriteEnabled)
                                        subDict.UpgradeOpen();

                                    subDict.Erase();
                                }

                                // Upgrade root before removing key
                                if (!root.IsWriteEnabled)
                                    root.UpgradeOpen();

                                if (root.Contains(subKey))
                                    root.Remove(subKey);
                            }
                        }

                        ed.WriteMessage($"\nCleared {edgesDeleted} edge entities and {beamsDeleted} beam nodes.");
                    }

                    // --- Recreate empty ROOT structure and known subdicts ---
                    NODCore.InitFoundationNOD(context, tr);

                    tr.Commit();
                    ed.WriteMessage("\nEE_Foundation NOD has been cleared and reset.");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nError clearing NOD: {ex.Message}");
                }
            }
        }
    }
}
