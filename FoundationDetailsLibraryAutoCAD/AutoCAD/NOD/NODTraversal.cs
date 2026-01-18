using Autodesk.AutoCAD.DatabaseServices;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using static FoundationDetailsLibraryAutoCAD.AutoCAD.NOD.HandleHandler;
using static FoundationDetailsLibraryAutoCAD.Data.FoundationEntityData;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.NOD
{
    /// <summary>
    /// Centralized NOD traversal and tree building helper.
    /// Produces both WPF TreeView data and debug strings.
    /// </summary>
    public static class NODTraversal
    {
        /// <summary>
        /// Recursively builds the NOD tree as an ObservableCollection for TreeView binding.
        /// </summary>
        internal static ObservableCollection<ExtensionDataItem> BuildTree(
            FoundationContext context,
            Transaction tr,
            DBDictionary dict,
            Database db)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (dict == null) throw new ArgumentNullException(nameof(dict));
            if (db == null) throw new ArgumentNullException(nameof(db));

            var items = new ObservableCollection<ExtensionDataItem>();

            foreach (var kvp in NODScanner.EnumerateDictionary(dict))
            {
                string key = kvp.Key;
                ObjectId id = kvp.Value;

                DBObject obj = null;
                try
                {
                    obj = tr.GetObject(id, OpenMode.ForRead);
                }
                catch
                {
                    // Unreadable object
                }

                if (obj is DBDictionary subDict)
                {
                    var childItem = new ExtensionDataItem
                    {
                        Name = key,
                        Type = "Subdictionary",
                        Children = BuildTree(context, tr, subDict, db)
                    };
                    items.Add(childItem);
                }
                else if (obj is Entity || obj is Xrecord)
                {
                    var entry = NODCore.ValidateHandleOrId(context, tr, db, "BuildTree", key);
                    var childItem = new ExtensionDataItem
                    {
                        Name = key,
                        Type = obj?.GetType().Name ?? "Unknown",
                        ObjectId = entry.Status == HandleStatus.Valid ? entry.Id : ObjectId.Null
                    };
                    items.Add(childItem);
                }
                else
                {
                    // fallback
                    items.Add(new ExtensionDataItem
                    {
                        Name = key,
                        Type = "Unreadable",
                        ObjectId = ObjectId.Null
                    });
                }
            }

            return items;
        }

        /// <summary>
        /// Converts an ObservableCollection tree into a formatted string for debugging/logging.
        /// </summary>
        internal static string TreeToString(IEnumerable<ExtensionDataItem> tree, int indentLevel = 0)
        {
            if (tree == null) return string.Empty;

            StringBuilder sb = new StringBuilder();
            string indent = new string(' ', indentLevel * 3);

            foreach (var item in tree)
            {
                int subDictCount = 0;
                if (item.Children != null)
                {
                    foreach (var c in item.Children)
                    {
                        if (c.Type == "Subdictionary")
                            subDictCount++;
                    }
                }

                sb.AppendLine($"{indent}{item.Name} ({item.Type})" +
                    (subDictCount > 0 ? $" [{subDictCount} sub-dictionaries]" : "") +
                    (item.ObjectId.HasValue ? $" [Id: {item.ObjectId.Value.Handle}]" : "") +
                    (item.NODObject != null ? $" [{item.NODObject.Type}]" : ""));

                // --- Recurse into children ---
                if (item.Children != null && item.Children.Count > 0)
                {
                    sb.Append(TreeToString(item.Children, indentLevel + 1));
                }
            }

            return sb.ToString();
        }


        /// <summary>
        /// Builds both TreeView data and a debug string at once.
        /// </summary>
        internal static void GetTreeAndDebug(
            FoundationContext context,
            Transaction tr,
            DBDictionary root,
            Database db,
            out ObservableCollection<ExtensionDataItem> treeData,
            out string debugString)
        {
            treeData = BuildTree(context, tr, root, db);
            debugString = TreeToString(treeData);
        }

        internal static NODTraversalResult TraverseFoundation(
    FoundationContext context,
    Transaction tr,
    DBDictionary root,
    Database db)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (db == null) throw new ArgumentNullException(nameof(db));

            var results = new List<TraversalResult>();

            var tree = BuildTreeAndValidate(
                context,
                tr,
                root,
                db,
                results,
                path: NODCore.ROOT);

            return new NODTraversalResult(tree, results);
        }

        private static ObservableCollection<ExtensionDataItem> BuildTreeAndValidate(
    FoundationContext context,
    Transaction tr,
    DBDictionary dict,
    Database db,
    List<TraversalResult> results,
    string path)
        {
            var items = new ObservableCollection<ExtensionDataItem>();

            foreach (var kvp in NODScanner.EnumerateDictionary(dict))
            {
                string key = kvp.Key;
                ObjectId id = kvp.Value;
                string fullPath = $"{path}/{key}";

                DBObject obj = null;
                try
                {
                    obj = tr.GetObject(id, OpenMode.ForRead);
                }
                catch
                {
                    items.Add(new ExtensionDataItem
                    {
                        Name = key,
                        Type = "Unreadable"
                    });
                    continue;
                }

                if (obj is DBDictionary subDict)
                {
                    items.Add(new ExtensionDataItem
                    {
                        Name = key,
                        Type = "Subdictionary",
                        Children = BuildTreeAndValidate(
                            context, tr, subDict, db, results, fullPath)
                    });
                    continue;
                }

                // ---- ENTITY / XRECORD VALIDATION ----
                var handleResult = NODCore.ValidateHandleOrId(
                    context, tr, db, "Traversal", key);

                if (handleResult.Status == HandleStatus.Valid &&
                    handleResult.Entity != null)
                {
                    results.Add(TraversalResult.Success(
                        key, fullPath, handleResult.Handle,
                        handleResult.Id, handleResult.Entity));

                    items.Add(new ExtensionDataItem
                    {
                        Name = key,
                        Type = handleResult.Entity.GetType().Name,
                        ObjectId = handleResult.Id
                    });
                }
                else
                {
                    results.Add(TraversalResult.InvalidHandle(key, fullPath));

                    items.Add(new ExtensionDataItem
                    {
                        Name = key,
                        Type = "Invalid"
                    });
                }
            }

            return items;
        }


    }

    internal sealed class NODTraversalResult
    {
        public ObservableCollection<ExtensionDataItem> Tree { get; }
        public List<TraversalResult> Results { get; }

        public NODTraversalResult(
            ObservableCollection<ExtensionDataItem> tree,
            List<TraversalResult> results)
        {
            Tree = tree;
            Results = results;
        }
    }

    internal enum TraversalStatus
    {
        Success,
        InvalidHandle,
        MissingObjectId,
        ErasedObject,
        NotEntity
    }

    internal sealed class TraversalResult
    {
        public string Key { get; }
        public string Path { get; }          // FULL dictionary path
        public Handle Handle { get; }
        public ObjectId ObjectId { get; }
        public Entity Entity { get; }
        public TraversalStatus Status { get; }

        private TraversalResult(
            string key,
            string path,
            TraversalStatus status,
            Handle handle = default,
            ObjectId objectId = default,
            Entity entity = null)
        {
            Key = key;
            Path = path;
            Status = status;
            Handle = handle;
            ObjectId = objectId;
            Entity = entity;
        }

        // ===============================
        // FACTORIES
        // ===============================

        public static TraversalResult Success(
            string key,
            string path,
            Handle handle,
            ObjectId id,
            Entity ent)
        {
            return new TraversalResult(
                key,
                path,
                TraversalStatus.Success,
                handle,
                id,
                ent);
        }

        public static TraversalResult InvalidHandle(string key, string path)
        {
            return new TraversalResult(
                key,
                path,
                TraversalStatus.InvalidHandle);
        }

        public static TraversalResult MissingObjectId(
            string key,
            string path,
            Handle handle)
        {
            return new TraversalResult(
                key,
                path,
                TraversalStatus.MissingObjectId,
                handle);
        }

        public static TraversalResult Erased(
            string key,
            string path,
            Handle handle,
            ObjectId id)
        {
            return new TraversalResult(
                key,
                path,
                TraversalStatus.ErasedObject,
                handle,
                id);
        }

        public static TraversalResult NotEntity(
            string key,
            string path,
            Handle handle,
            ObjectId id)
        {
            return new TraversalResult(
                key,
                path,
                TraversalStatus.NotEntity,
                handle,
                id);
        }
    }

}
