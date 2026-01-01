using Autodesk.AutoCAD.DatabaseServices;
using FoundationDetailsLibraryAutoCAD.AutoCAD;
using System.Collections.Generic;
using System.Windows.Controls;
using static FoundationDetailsLibraryAutoCAD.Data.FoundationEntityData;

namespace FoundationDetailsLibraryAutoCAD.Managers
{
    public static class TreeViewManager
    {
        internal static TreeViewItem CreateTreeViewItem(ExtensionDataItem dataItem)
        {
            string headerText = dataItem.Value != null
                ? $"{dataItem.Name} ({dataItem.Type}): {FormatValue(dataItem.Value)}"
                : $"{dataItem.Name} ({dataItem.Type})";

            var treeItem = new TreeViewItem { Header = headerText };

            foreach (var child in dataItem.Children)
            {
                treeItem.Items.Add(CreateTreeViewItem(child));
            }

            return treeItem;
        }

        private static string FormatValue(object value)
        {
            if (value is IEnumerable<string> list)
                return string.Join(", ", list);

            return value?.ToString() ?? "";
        }

        public static void Populate(TreeView treeView, Database db)
        {
            treeView.Items.Clear();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var root = NODManager.GetFoundationRoot(tr, db);
                if (root == null) return;

                var nodeMap = new Dictionary<string, TreeViewItem>();
                TreeViewItem rootNode = new TreeViewItem
                {
                    Header = NODManager.ROOT,
                    IsExpanded = true
                };

                treeView.Items.Add(rootNode);
                NODManager.BuildTree(root, rootNode, tr, nodeMap);

                tr.Commit();
            }
        }
    }
}
