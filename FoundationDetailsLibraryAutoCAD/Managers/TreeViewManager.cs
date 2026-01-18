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
            NODObjectWrapper wrapper = null;

            if (info.NODObject != null)
            {
                wrapper = info.NODObject;
            }
            else if (info.ObjectId != ObjectId.Null && _transaction != null)
            {
                try
                {
                    var obj = _transaction.GetObject(info.ObjectId, OpenMode.ForRead, false);
                    if (obj is Polyline pl)
                        wrapper = new NODObjectWrapper(pl); // Wrap Polyline as NODObjectWrapper
                }
                catch
                {
                    // Leave wrapper null if failed
                    wrapper = null;
                }
            }

            return new TreeViewItem
            {
                Header = new PolylineTreeItemControl
                {
                    DataContext = new PolylineTreeItemViewModel(wrapper, _transaction)
                },
                Tag = info
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

        // ==========================================================
        // Core Node Builder
        // ==========================================================
        private TreeViewItem CreateNode(ExtensionDataItem dataItem)
        {
            if (dataItem == null)
                return null;

            // Create TreeNodeInfo including NODObject reference
            var info = new TreeNodeInfo(
                dataItem.Name,
                dataItem.Type == "Subdictionary" || dataItem.Type == "Dictionary",
                dataItem.ObjectId ?? ObjectId.Null,   // <-- use Null if it's null
                dataItem.NODObject  // <-- store NODObjectWrapper instead of Entity
            );

            TreeViewItem node;

            // Use specialized control if registered
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
            if (dataItem.Value != null)
                return $"{dataItem.Name} ({dataItem.Type}): {FormatValue(dataItem.Value)}";

            return $"{dataItem.Name} ({dataItem.Type})";
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
