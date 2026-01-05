using Autodesk.AutoCAD.DatabaseServices;
using FoundationDetailsLibraryAutoCAD.AutoCAD;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
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

        public static void Populate(FoundationContext context, TreeView treeView, Database db)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            treeView.Items.Clear();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var root = NODManager.GetFoundationRoot(context, tr);
                if (root == null) return;

                var nodeMap = new Dictionary<string, TreeViewItem>();
                TreeViewItem rootNode = new TreeViewItem
                {
                    Header = NODManager.ROOT,
                    IsExpanded = true
                };

                treeView.Items.Add(rootNode);
                TreeViewManager.BuildTree(root, rootNode, tr, nodeMap);

                tr.Commit();
            }
        }

        internal static void BuildTree(
            DBDictionary dict,
            TreeViewItem parent,
            Transaction tr,
            Dictionary<string, TreeViewItem> nodeMap)
        {
            foreach (DBDictionaryEntry entry in dict)
            {
                DBObject obj = tr.GetObject(entry.Value, OpenMode.ForRead);

                bool isDict = obj is DBDictionary;

                var node = new TreeViewItem
                {
                    Header = entry.Key,
                    Tag = new TreeNodeInfo(entry.Key, isDict)
                };

                parent.Items.Add(node);

                // 🔑 THIS IS THE MISSING PIECE
                nodeMap[entry.Key] = node;

                if (isDict)
                {
                    BuildTree((DBDictionary)obj, node, tr, nodeMap);
                }
            }
        }


        internal static void AttachEntityToTree(FoundationContext context,
            TreeViewItem rootNode,
            string handleKey,
            Entity ent)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var model = context.Model;

            if (doc == null) return;

            var db = doc.Database;
            var ed = doc.Editor;

            // Find the node with matching header (handle string)
            TreeViewItem node = FindNodeByHeader(rootNode, handleKey);
            if (node == null)
                return;

            node.Tag = ent;
            FoundationEntityData.DisplayExtensionData(context, ent);
        }

        internal static TreeViewItem FindNodeByHeader(
            TreeViewItem parent,
            string header)
        {
            foreach (TreeViewItem child in parent.Items)
            {
                if (child.Header?.ToString() == header)
                    return child;

                TreeViewItem found = FindNodeByHeader(child, header);
                if (found != null)
                    return found;
            }

            return null;
        }

        internal sealed class TreeNodeInfo
        {
            public string Key { get; }
            public bool IsDictionary { get; }
            public Entity Entity { get; set; }

            public TreeNodeInfo(string key, bool isDictionary)
            {
                Key = key;
                IsDictionary = isDictionary;
            }
        }
    }
}
