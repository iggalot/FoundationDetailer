using System;
using System.Collections.Generic;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.NOD.UI.TreeViewer
{
    /// <summary>
    /// Represents a single node in the NOD tree visualization.
    /// This is a pure UI/inspection model and does NOT modify AutoCAD NOD data.
    /// </summary>
    public class NODTreeNode
    {
        /// <summary>
        /// Display name of the node (dictionary name, xrecord key, etc.)
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Type of node (Dictionary, XRecord, StringValue, etc.)
        /// </summary>
        public string NodeType { get; set; }

        /// <summary>
        /// Optional value (used for XRecords or string entries)
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// Child nodes (recursive structure)
        /// </summary>
        public List<NODTreeNode> Children { get; set; } = new List<NODTreeNode>();

        /// <summary>
        /// Optional AutoCAD ObjectId handle string for selection/highlight
        /// </summary>
        public string Handle { get; set; }

        /// <summary>
        /// Depth in tree (used for indentation / debugging)
        /// </summary>
        public int Depth { get; set; }

        public override string ToString()
        {
            if (!string.IsNullOrEmpty(Value))
                return $"{Name} : {Value}";

            return $"{Name} ({NodeType})";
        }
    }
}