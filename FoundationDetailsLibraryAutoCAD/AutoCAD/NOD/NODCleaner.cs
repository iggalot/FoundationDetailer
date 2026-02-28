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
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // Prompt user for confirmation
            PromptKeywordOptions pko = new PromptKeywordOptions(
                "\nWARNING: This will completely clear the EE_Foundation NOD. Are you sure?");
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
                    // Access the Named Objects Dictionary
                    DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

                    // If ROOT exists, erase it
                    if (nod.Contains(NODCore.ROOT))
                    {
                        var rootId = nod.GetAt(NODCore.ROOT);
                        var rootObj = tr.GetObject(rootId, OpenMode.ForWrite);
                        rootObj.Erase();
                    }

                    // --- Immediately recreate an empty ROOT dictionary ---
                    var newRoot = new DBDictionary();
                    nod.SetAt(NODCore.ROOT, newRoot);
                    tr.AddNewlyCreatedDBObject(newRoot, true);

                    // Optional: recreate empty subdictionaries for boundary and grade beam
                    NODCore.GetOrCreateNestedSubDictionary(tr, newRoot, NODCore.KEY_BOUNDARY_SUBDICT);
                    NODCore.GetOrCreateNestedSubDictionary(tr, newRoot, NODCore.KEY_GRADEBEAM_SUBDICT);

                    tr.Commit();
                    ed.WriteMessage("\nEE_Foundation NOD has been cleared and reset.");
                }
                catch (Exception ex)
                {
                    ed.WriteMessage($"\nError clearing NOD: {ex.Message}");
                }
            }
        }
    }
}
