using Autodesk.AutoCAD.DatabaseServices;
using FoundationDetailer.UI.Windows;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Collections.Generic;
using System.Text;
using static FoundationDetailsLibraryAutoCAD.Data.FoundationEntityData;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.NOD
{
    public class NODViewer
    {
        // ==========================================================
        // VIEW NOD CONTENT - fully recursive using existing ProcessDictionary
        // ==========================================================
        public static void ViewFoundationNOD(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var db = doc.Database;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var rootDict = NODCore.GetFoundationRootDictionary(tr, db);
                if (rootDict == null)
                {
                    ScrollableMessageBox.Show("No EE_Foundation dictionary found.");
                    return;
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("=== EE_Foundation Contents ===");

                // Process each top-level subdictionary (BOUNDARY, GRADEBEAM, etc.)
                foreach (var kvp in NODScanner.EnumerateDictionary(rootDict))
                {
                    string rootName = kvp.Key;
                    sb.AppendLine();
                    sb.AppendLine($"[{rootName}]");

                    var subDict = tr.GetObject(kvp.Value, OpenMode.ForRead) as DBDictionary;
                    if (subDict == null || subDict.Count == 0)
                    {
                        sb.AppendLine("   No Objects");
                        continue;
                    }

                    // Use your existing ProcessDictionary to get the tree
                    var treeItems = NODScanner.ProcessDictionary(context, tr, subDict, db);

                    // Recursively print the tree
                    PrintTree(sb, treeItems, indentLevel: 1);
                }

                ScrollableMessageBox.Show(sb.ToString());
                tr.Commit();
            }
        }

        // ==========================================================
        // Helper to recursively print ExtensionDataItem tree
        // ==========================================================
        private static void PrintTree(StringBuilder sb, IEnumerable<ExtensionDataItem> items, int indentLevel)
        {
            string indent = new string(' ', indentLevel * 3); // 3 spaces per level

            foreach (var item in items)
            {
                if (item.Children != null && item.Children.Count > 0)
                {
                    sb.AppendLine($"{indent}{item.Name} : {item.Type}");
                    // Cast ObservableCollection to IEnumerable
                    PrintTree(sb, (IEnumerable<ExtensionDataItem>)item.Children, indentLevel + 1);
                }
                else
                {
                    sb.AppendLine($"{indent}{item.Name} : {item.Type}");
                }
            }
        }


    }
}
