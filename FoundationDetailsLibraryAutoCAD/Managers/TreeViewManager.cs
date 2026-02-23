using Autodesk.AutoCAD.DatabaseServices;
using FoundationDetailsLibraryAutoCAD.AutoCAD.NOD;
using FoundationDetailsLibraryAutoCAD.Data;
using FoundationDetailsLibraryAutoCAD.UI.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Controls;

namespace FoundationDetailsLibraryAutoCAD.Managers
{
    /// <summary>
    /// UI-only adapter that converts ExtensionDataItem trees
    /// into WPF TreeViewItems. No NOD traversal here.
    /// Supports Entity references for node actions.
    /// </summary>
    public sealed class TreeViewManager
    {
        private readonly Transaction _transaction;

        // ==========================================================
        // Constructor: pass the active transaction for Polyline lookup
        // ==========================================================
        public TreeViewManager(Transaction tr)
        {
            _transaction = tr ?? throw new ArgumentNullException(nameof(tr));

            _controlMap = new Dictionary<string, Func<TreeNodeInfo, TreeViewItem>>
            {
                { NODCore.KEY_BOUNDARY_SUBDICT, info => CreatePolylineNode(info) },
                { NODCore.KEY_GRADEBEAM_SUBDICT, info => CreatePolylineNode(info) }
            };
        }

        private readonly Dictionary<string, Func<TreeNodeInfo, TreeViewItem>> _controlMap;

        // ==========================================================
        // Helper to create a TreeViewItem for a Polyline
        // ==========================================================
        private TreeViewItem CreatePolylineNode(TreeNodeInfo info)
        {
            // Wrap the Entity if not already wrapped
            NODObjectWrapper wrapper = info.NODObject;
            if (wrapper == null && info.ObjectId != ObjectId.Null && _transaction != null)
            {
                try
                {
                    var obj = _transaction.GetObject(info.ObjectId, OpenMode.ForRead, false);
                    if (obj is Entity ent)
                        wrapper = new NODObjectWrapper(ent);
                }
                catch
                {
                    wrapper = null;
                }
            }

            return new TreeViewItem
            {
                Header = new PolylineTreeItemControl
                {
                    DataContext = new PolylineTreeItemViewModel(wrapper, _transaction)
                },
                Tag = new TreeNodeInfo(info.Key, info.IsDictionary, info.ObjectId, wrapper)
            };
        }

        // ==========================================================
        // Public Entry Point
        // ==========================================================
        public void PopulateFromData(TreeView treeView, ObservableCollection<ExtensionDataItem> treeData)
        {
            if (treeView == null)
                throw new ArgumentNullException(nameof(treeView));

            treeView.Items.Clear();

            if (treeData == null)
                return;

            foreach (var item in treeData)
            {
                treeView.Items.Add(CreateNode(item));
            }
        }

        /// <summary>
        /// Populate a TreeView from an ExtensionDataItem collection (from your NOD tree)
        /// </summary>
        public void PopulateTreeViewFromExtensionData(
            TreeView treeView,
            ObservableCollection<ExtensionDataItem> extensionData)
        {
            if (treeView == null)
                throw new ArgumentNullException(nameof(treeView));

            treeView.Items.Clear();

            if (extensionData == null)
                return;

            foreach (var item in extensionData)
            {
                var node = CreateNode(item);
                if (node != null)
                    treeView.Items.Add(node);
            }
        }

        // ==========================================================
        // Core Node Builder
        // ==========================================================
        // In TreeViewManager:
        private TreeViewItem CreateNode(ExtensionDataItem dataItem)
        {
            if (dataItem == null) return null;

            // Create TreeNodeInfo including NODObject reference
            var info = new TreeNodeInfo(
                dataItem.Name,
                dataItem.Type == "Subdictionary" || dataItem.Type == "Dictionary",
                dataItem.ObjectId ?? ObjectId.Null,
                dataItem.NODObject // <-- NODObjectWrapper
            );

            TreeViewItem node;

            // Use specialized control if registered (polyline, gradebeam, etc.)
            if (_controlMap.TryGetValue(dataItem.Name, out var factory))
            {
                node = factory(info);
            }
            else
            {
                node = new TreeViewItem
                {
                    Header = BuildHeader(dataItem),
                    Tag = info
                };
            }

            // Recurse children
            if (dataItem.Children != null)
            {
                foreach (var child in dataItem.Children)
                {
                    var childNode = CreateNode(child);
                    if (childNode != null)
                        node.Items.Add(childNode);
                }
            }

            return node;
        }

        // ==========================================================
        // Header Formatting
        // ==========================================================
        private static string BuildHeader(ExtensionDataItem dataItem)
        {
            if (dataItem == null) return string.Empty;

            string extraInfo = "";

            if (dataItem.Children != null)
            {
                // Loop over children to gather width, depth, and edge info
                foreach (var child in dataItem.Children)
                {
                    // --- Width / Depth
                    if (child.Name.Equals("Width", StringComparison.OrdinalIgnoreCase) && child.Value?.Count > 0)
                        extraInfo += $" Width={child.Value[0]}";
                    if (child.Name.Equals("Depth", StringComparison.OrdinalIgnoreCase) && child.Value?.Count > 0)
                        extraInfo += $" Depth={child.Value[0]}";

                    // --- Edges (assumes L_ prefix or 'Left' in value)
                    if (child.Name.Equals(NODCore.KEY_EDGES_SUBDICT, StringComparison.OrdinalIgnoreCase) && child.Children != null)
                    {
                        int leftCount = 0, rightCount = 0;
                        foreach (var edgeChild in child.Children)
                        {
                            if (edgeChild.Name.StartsWith("L_", StringComparison.OrdinalIgnoreCase) ||
                                (edgeChild.Value?.Count > 0 && edgeChild.Value[0].ToString().Contains("Left") == true))
                                leftCount++;
                            else
                                rightCount++;
                        }

                        extraInfo += $" [Edges: L={leftCount}, R={rightCount}]";
                    }
                }
            }

            // --- Display node's value if present (e.g., XRecord info)
            if (dataItem.Value != null && dataItem.Value.Count > 0)
                return $"{dataItem.Name} ({dataItem.Type}): {FormatValue(dataItem.Value)}{extraInfo}";

            return $"{dataItem.Name} ({dataItem.Type}){extraInfo}";
        }

        private static string FormatValue(object value)
        {
            if (value is IEnumerable<string> list)
                return string.Join(", ", list);

            return value?.ToString() ?? string.Empty;
        }

        // ==========================================================
        // Tree Node Metadata (UI-safe)
        // ==========================================================
        public sealed class TreeNodeInfo
        {
            public string Key { get; }
            public bool IsDictionary { get; }
            public ObjectId ObjectId { get; }
            public NODObjectWrapper NODObject { get; }  // <-- wrapper instead of Entity

            public TreeNodeInfo(
                string key,
                bool isDictionary,
                ObjectId objectId,
                NODObjectWrapper nodObject = null)
            {
                Key = key;
                IsDictionary = isDictionary;
                ObjectId = objectId;
                NODObject = nodObject;
            }
        }

    }
}
