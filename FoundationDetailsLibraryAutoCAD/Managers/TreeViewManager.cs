using Autodesk.AutoCAD.DatabaseServices;
using FoundationDetailsLibraryAutoCAD.AutoCAD.NOD;
using FoundationDetailsLibraryAutoCAD.Data;
using FoundationDetailsLibraryAutoCAD.UI.Controls;
using System;
using System.Collections.Generic;
using System.Windows.Controls;
using static FoundationDetailsLibraryAutoCAD.Data.FoundationEntityData;

namespace FoundationDetailsLibraryAutoCAD.Managers
{
    public class TreeViewManager
    {
        internal readonly Dictionary<string, Func<TreeNodeInfo, TreeViewItem>> _controlMap =
            new Dictionary<string, Func<TreeNodeInfo, TreeViewItem>>
            {
                { NODCore.KEY_BOUNDARY_SUBDICT, leafInfo =>
                    new TreeViewItem
                    {
                        Header = new PolylineTreeItemControl { DataContext = new PolylineTreeItemViewModel((Polyline)leafInfo.Entity) },
                        Tag = leafInfo
                    }
                },
                { NODCore.KEY_GRADEBEAM_SUBDICT, leafInfo =>
                    new TreeViewItem
                    {
                        Header = new PolylineTreeItemControl { DataContext = new PolylineTreeItemViewModel((Polyline)leafInfo.Entity) },
                        Tag = leafInfo
                    }
                }
            };

        internal TreeViewItem CreateTreeViewItem(ExtensionDataItem dataItem)
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

        private string FormatValue(object value)
        {
            if (value is IEnumerable<string> list)
                return string.Join(", ", list);

            return value?.ToString() ?? "";
        }

        public void Populate(FoundationContext context, TreeView treeView, Database db)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            treeView.Items.Clear();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var root = NODCore.GetFoundationRoot(tr, db);
                if (root == null) return;

                var nodeMap = new Dictionary<string, TreeViewItem>();
                TreeViewItem rootNode = new TreeViewItem
                {
                    Header = NODCore.ROOT,
                    IsExpanded = true
                };

                treeView.Items.Add(rootNode);
                BuildTree(root, rootNode, tr, nodeMap);

                tr.Commit();
            }
        }

        internal void BuildTree(
            DBDictionary dict,
            TreeViewItem parent,
            Transaction tr,
            Dictionary<string, TreeViewItem> nodeMap,
            string parentPath = "")
        {
            foreach (DBDictionaryEntry entry in dict)
            {
                DBObject obj = tr.GetObject(entry.Value, OpenMode.ForRead);

                bool isDict = obj is DBDictionary;

                // Full path key
                string fullKey = string.IsNullOrEmpty(parentPath) ? entry.Key : $"{parentPath}/{entry.Key}";

                var node = new TreeViewItem
                {
                    Header = entry.Key,
                    Tag = new TreeViewManager.TreeNodeInfo(fullKey, isDict)
                };

                parent.Items.Add(node);

                // Add to map
                nodeMap[fullKey] = node;

                if (isDict)
                {
                    BuildTree((DBDictionary)obj, node, tr, nodeMap, fullKey);
                }
            }
        }


        /// <summary>
        /// Recursively build TreeView items from DBDictionaries and Entities, including nested subdictionaries
        /// </summary>
        internal static void BuildTreeRecursive(
            DBDictionary dict,
            TreeViewItem parentNode,
            Transaction tr,
            Dictionary<string, TreeViewItem> nodeMap,
            TreeViewManager treeMgr,
            string pathSoFar = "")
        {
            foreach (DBDictionaryEntry entry in dict)
            {
                string entryKey = entry.Key;
                string fullPath = string.IsNullOrEmpty(pathSoFar) ? entryKey : $"{pathSoFar}/{entryKey}";

                DBObject obj = tr.GetObject(entry.Value, OpenMode.ForRead);

                // Node for this key
                var node = new TreeViewItem
                {
                    Header = entryKey,
                    Tag = new TreeViewManager.TreeNodeInfo(entryKey, obj is DBDictionary)
                };

                parentNode.Items.Add(node);
                nodeMap[fullPath] = node;

                if (obj is DBDictionary subDict)
                {
                    // Recurse into subdictionary
                    BuildTreeRecursive(subDict, node, tr, nodeMap, treeMgr, fullPath);
                }
                else
                {
                    // Leaf entity (Xrecord or Entity)
                    if (obj is Entity ent)
                    {
                        node.Tag = new TreeViewManager.TreeNodeInfo(entryKey, false) { Entity = ent };
                        node.Header = $"{entryKey} ({ent.Handle})";
                    }
                    else if (obj is Xrecord xr)
                    {
                        node.Tag = new TreeViewManager.TreeNodeInfo(entryKey, false);
                        node.Header = $"{entryKey} (Xrecord)";
                    }
                    else
                    {
                        node.Tag = new TreeViewManager.TreeNodeInfo(entryKey, false);
                        node.Header = entryKey;
                    }
                }
            }
        }


        /// <summary>
        /// Recursively builds the TreeView for a dictionary and its subdictionaries/entities.
        /// </summary>
        private static void BuildTreeRecursiveWithEntities(
            DBDictionary dict,
            TreeViewItem parentNode,
            Transaction tr,
            Dictionary<string, TreeViewItem> nodeMap,
            TreeViewManager treeMgr,
            string pathSoFar)
        {
            foreach (DBDictionaryEntry entry in dict)
            {
                string entryKey = entry.Key;
                string fullPath = string.IsNullOrEmpty(pathSoFar) ? entryKey : $"{pathSoFar}/{entryKey}";

                DBObject obj = tr.GetObject(entry.Value, OpenMode.ForRead);

                var node = new TreeViewItem
                {
                    Header = entryKey,
                    Tag = new TreeViewManager.TreeNodeInfo(entryKey, obj is DBDictionary)
                };

                parentNode.Items.Add(node);
                nodeMap[fullPath] = node;

                if (obj is DBDictionary subDict)
                {
                    // Recurse into subdictionary
                    BuildTreeRecursiveWithEntities(subDict, node, tr, nodeMap, treeMgr, fullPath);
                }
                else
                {
                    // Leaf entity (Entity or Xrecord)
                    if (obj is Entity ent)
                    {
                        node.Tag = new TreeViewManager.TreeNodeInfo(entryKey, false) { Entity = ent };
                        node.Header = $"{entryKey} ({ent.Handle})";
                    }
                    else if (obj is Xrecord xr)
                    {
                        node.Tag = new TreeViewManager.TreeNodeInfo(entryKey, false);
                        node.Header = $"{entryKey} (Xrecord)";
                    }
                }
            }
        }


        private static string CombinePath(string parent, string child)
        {
            return string.IsNullOrEmpty(parent) ? child : parent + "/" + child;
        }




        internal void AttachEntityToTree(FoundationContext context,
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
