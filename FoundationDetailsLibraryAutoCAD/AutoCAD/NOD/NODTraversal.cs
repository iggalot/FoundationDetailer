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
        /// Converts an ObservableCollection tree into a formatted string for debugging/logging.
        /// </summary>
        internal static string TreeToString(IEnumerable<ExtensionDataItem> tree, int indentLevel = 0)
        {
            if (tree == null) return string.Empty;

            StringBuilder sb = new StringBuilder();
            string indent = new string(' ', indentLevel * 3); // 3 spaces per level

            foreach (var item in tree)
            {
                // Count subdictionaries in children
                int subDictCount = 0;
                if (item.Children != null)
                {
                    foreach (var c in item.Children)
                    {
                        if (c.Type == "Subdictionary")
                            subDictCount++;
                    }
                }

                // Format handles if present
                string handlesText = string.Empty;
                if (item.Value is List<string> handleList && handleList.Count > 0)
                {
                    handlesText = $" [Handles: {string.Join(", ", handleList)}]";
                }

                sb.AppendLine($"{indent}{item.Name} ({item.Type})" +
                    (subDictCount > 0 ? $" [{subDictCount} sub-dictionaries]" : "") +
                    handlesText);

                // Recurse into children
                if (item.Children != null && item.Children.Count > 0)
                {
                    sb.Append(TreeToString(item.Children, indentLevel + 1));
                }
            }

            return sb.ToString();
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
