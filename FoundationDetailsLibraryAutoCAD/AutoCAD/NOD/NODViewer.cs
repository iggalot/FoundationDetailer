using Autodesk.AutoCAD.DatabaseServices;
using FoundationDetailer.UI.Windows;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.NOD
{
    public class NODViewer
    {

        // ==========================================================
        // NODViewer: Display the tree recursively
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

                    var treeItems = NODScanner.ProcessDictionary(context, tr, subDict, db);
                    PrintTree(sb, treeItems, indentLevel: 1);
                }

                ScrollableMessageBox.Show(sb.ToString());
                tr.Commit();
            }
        }

        // ==========================================================
        // Recursively print ExtensionDataItem tree with indentation
        // ==========================================================
        private static void PrintTree(StringBuilder sb, IEnumerable<ExtensionDataItem> items, int indentLevel)
        {
            string indent = new string(' ', indentLevel * 3); // 3 spaces per level

            foreach (var item in items)
            {
                string valueText = item.Value != null && item.Value.Count > 0
                    ? " [" + string.Join(", ", item.Value) + "]"
                    : string.Empty;

                sb.AppendLine($"{indent}{item.Name} : {item.Type}{valueText}");

                if (item.Children != null && item.Children.Count > 0)
                {
                    PrintTree(sb, item.Children, indentLevel + 1);
                }
            }
        }


    }
}
