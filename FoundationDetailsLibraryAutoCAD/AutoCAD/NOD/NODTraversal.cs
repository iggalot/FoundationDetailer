using Autodesk.AutoCAD.DatabaseServices;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
        internal static string TreeToString(
            IEnumerable<ExtensionDataItem> tree,
            Transaction tr,
            Database db,
            int indentLevel = 0)
        {
            if (tree == null) return string.Empty;

            StringBuilder sb = new StringBuilder();
            string indent = new string(' ', indentLevel * 3); // 3 spaces per level

            foreach (var item in tree)
            {
                string extraInfo = "";

                if (item.Children != null)
                {
                    // --- Width / Depth
                    foreach (var child in item.Children)
                    {
                        if (child.Name.Equals("Width", StringComparison.OrdinalIgnoreCase) && child.Value?.Count > 0)
                            extraInfo += $" Width={child.Value[0]}";
                        if (child.Name.Equals("Depth", StringComparison.OrdinalIgnoreCase) && child.Value?.Count > 0)
                            extraInfo += $" Depth={child.Value[0]}";
                    }

                    // --- Edge counts
                    var edgesNode = item.Children.FirstOrDefault(c => c.Name == NODCore.KEY_EDGES_SUBDICT);
                    if (edgesNode != null && edgesNode.Children != null)
                    {
                        int leftCount = 0, rightCount = 0;
                        foreach (var edgeChild in edgesNode.Children)
                        {
                            if (edgeChild.Name.StartsWith("L_", StringComparison.OrdinalIgnoreCase))
                                leftCount++;
                            else
                                rightCount++;
                        }
                        extraInfo += $" [Edges: L={leftCount}, R={rightCount}]";
                    }
                }

                // --- Handles / Value strings
                string handlesText = "";
                if (item.Value != null && item.Value.Count > 0)
                {
                    foreach (var val in item.Value)
                    {
                        if (val is string handleStr)
                        {
                            if (NODCore.TryGetObjectIdFromHandleString(null, db, handleStr, out var oid))
                            {
                                if (oid.IsNull)
                                    handlesText += $" [Handle {handleStr} : NULL]";
                                else if (oid.IsErased)
                                    handlesText += $" [Handle {handleStr} : Erased]";
                                else
                                    handlesText += $" [Handle {handleStr} : Valid Object]";
                            }
                            else
                            {
                                handlesText += $" [Handle {handleStr} : NotFound]";
                            }
                        }
                    }
                }

                // --- Compose final line
                string line;
                if (item.Value != null && item.Value.Count > 0)
                    line = $"{indent}{item.Name} ({item.Type}): {FormatValue(item.Value)}{extraInfo}{handlesText}";
                else
                    line = $"{indent}{item.Name} ({item.Type}){extraInfo}{handlesText}";

                sb.AppendLine(line);

                // --- Recurse children
                if (item.Children != null && item.Children.Count > 0)
                {
                    sb.Append(TreeToString(item.Children, tr, db, indentLevel + 1));
                }
            }

            return sb.ToString();
        }

        private static string FormatValue(IEnumerable<string> values)
        {
            return values != null ? string.Join(", ", values) : string.Empty;
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
