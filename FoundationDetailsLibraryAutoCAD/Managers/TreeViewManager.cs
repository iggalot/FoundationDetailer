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

        private readonly Dictionary<string, Func<TreeNodeInfo, TreeViewItem>> _controlMap;

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
