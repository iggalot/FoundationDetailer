using Autodesk.AutoCAD.DatabaseServices;
using System;
using System.Text;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.NOD.UI.TreeViewer
{
    /// <summary>
    /// Responsible ONLY for reading the AutoCAD NOD structure
    /// and converting it into a NODTreeNode hierarchy.
    /// </summary>
    public static class NODTreeReader
    {
        /// <summary>
        /// Entry point: builds full NOD tree from the foundation root dictionary.
        /// </summary>
        public static NODTreeNode BuildTree(Transaction tr, Database db)
        {
            var rootDict = NODCore.GetFoundationRootDictionary(tr, db);

            if (rootDict == null)
            {
                return new NODTreeNode
                {
                    Name = "NOD_ROOT_MISSING",
                    NodeType = "Missing"
                };
            }

            var rootNode = new NODTreeNode
            {
                Name = "FoundationNOD",
                NodeType = "RootDictionary"
            };

            TraverseDictionary(tr, rootDict, rootNode, 0);

            return rootNode;
        }

        /// <summary>
        /// Recursive traversal of DBDictionary.
        /// This is the backbone of the inspector.
        /// </summary>
        private static void TraverseDictionary(
            Transaction tr,
            DBDictionary dict,
            NODTreeNode parent,
            int depth)
        {
            foreach (DBDictionaryEntry entry in dict)
            {
                var obj = tr.GetObject(entry.Value, OpenMode.ForRead);

                var node = new NODTreeNode
                {
                    Name = entry.Key,
                    Depth = depth,
                    Handle = obj.ObjectId.Handle.ToString()
                };

                if (obj is DBDictionary subDict)
                {
                    node.NodeType = "Dictionary";
                    TraverseDictionary(tr, subDict, node, depth + 1);
                }
                else if (obj is Xrecord xrec)
                {
                    node.NodeType = "XRecord";
                    TraverseXRecord(xrec, node);
                }
                else
                {
                    node.NodeType = obj.GetType().Name;
                    node.Value = SafeObjectToString(obj);
                }

                parent.Children.Add(node);
            }
        }

        /// <summary>
        /// Reads XRecord data safely (handles string, resbuf chains, etc.)
        /// </summary>
        private static void TraverseXRecord(Xrecord xrec, NODTreeNode node)
        {
            var sb = new StringBuilder();

            foreach (var rb in xrec.Data)
            {
                sb.Append(rb.Value?.ToString());
                sb.Append(" | ");
            }

            node.Value = sb.ToString().TrimEnd('|', ' ');
        }

        /// <summary>
        /// Fallback string conversion for unknown DB objects.
        /// </summary>
        private static string SafeObjectToString(DBObject obj)
        {
            try
            {
                return obj.ToString();
            }
            catch
            {
                return "<unreadable>";
            }
        }
    }
}