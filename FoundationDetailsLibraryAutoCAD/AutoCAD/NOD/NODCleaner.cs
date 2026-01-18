using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using FoundationDetailsLibraryAutoCAD.Data;
using System;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.NOD
{
    internal static class NODCleaner
    {
        /// <summary>
        /// Completely deletes the EE_Foundation NOD structure (ROOT and all subdictionaries) after a warning prompt.
        /// </summary>
        public static void ClearFoundationNOD(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            Document doc = context.Document;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // Prompt user for confirmation
            PromptKeywordOptions pko = new PromptKeywordOptions(
                "\nWARNING: This will completely delete the EE_Foundation NOD. Are you sure?");
            pko.Keywords.Add("Yes");
            pko.Keywords.Add("No");
            pko.AllowNone = false;
            pko.Message = "\nConfirm deletion (Yes/No): ";

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
                    // Access Named Objects Dictionary
                    DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

                    if (nod.Contains(NODCore.ROOT))
                    {
                        ObjectId rootId = nod.GetAt(NODCore.ROOT);
                        DBObject rootObj = tr.GetObject(rootId, OpenMode.ForWrite);

                        // Erase the ROOT dictionary (this removes all subdictionaries and entities under it)
                        rootObj.Erase();

                        ed.WriteMessage("\nEE_Foundation NOD has been cleared.");
                    }
                    else
                    {
                        ed.WriteMessage("\nEE_Foundation NOD does not exist.");
                    }

                    tr.Commit();
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nError clearing NOD: {ex.Message}");
                }
            }
        }
    }
}
