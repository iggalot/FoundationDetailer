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

        private static void PrintTreeWithHandles(
            StringBuilder sb,
            IEnumerable<ExtensionDataItem> items,
            Transaction tr,
            Database db,
            int indentLevel)
        {
            string indent = new string(' ', indentLevel * 3); // 3 spaces per level

            foreach (var item in items)
            {
                string handleStatus = "";

                // Check if any of the stored values are handles and resolve them
                if (item.Value != null)
                {
                    foreach (var val in item.Value)
                    {
                        if (val is string handleStr)
                        {
                            if (NODCore.TryGetObjectIdFromHandleString(null, db, handleStr, out var oid))
                            {
                                if (oid.IsNull)
                                    handleStatus += $" [Handle {handleStr} : NULL]";
                                else if (oid.IsErased)
                                    handleStatus += $" [Handle {handleStr} : Erased]";
                                else
                                    handleStatus += $" [Handle {handleStr} : Valid Object]";
                            }
                            else
                            {
                                handleStatus += $" [Handle {handleStr} : NotFound]";
                            }
                        }
                    }
                }

                string valueText = !string.IsNullOrEmpty(handleStatus) ? handleStatus : "";

                sb.AppendLine($"{indent}{item.Name} : {item.Type}{valueText}");

                if (item.Children != null && item.Children.Count > 0)
                {
                    PrintTreeWithHandles(sb, item.Children, tr, db, indentLevel + 1);
                }
            }
        }


    }
}
