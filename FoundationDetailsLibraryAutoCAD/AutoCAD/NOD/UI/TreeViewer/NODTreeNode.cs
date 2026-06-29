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
        // =========================================================
        // CORE PROPERTIES (DO NOT CHANGE - USED ELSEWHERE)
        // =========================================================

        /// <summary>
        /// Optional AutoCAD entity handle associated with this node.
        /// Clicking the node will attempt to select this object.
        /// </summary>
        public string AutoCADHandle { get; set; }


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

        // =========================================================
        // CONSTRUCTORS (NEW - FIXES YOUR ERRORS)
        // =========================================================

        /// <summary>
        /// Default constructor (required for XAML binding / serializers).
        /// </summary>
        public NODTreeNode()
        {
            Children = new List<NODTreeNode>();
            NodeType = "Unknown";
        }

        /// <summary>
        /// Basic constructor for most nodes.
        /// </summary>
        public NODTreeNode(string name)
        {
            Name = name;
            NodeType = "Unknown";
            Children = new List<NODTreeNode>();
        }

        /// <summary>
        /// Full constructor with type and optional value.
        /// </summary>
        public NODTreeNode(string name, string nodeType, string value = null)
        {
            Name = name;
            NodeType = nodeType;
            Value = value;
            Children = new List<NODTreeNode>();
        }

        /// <summary>
        /// Constructor with handle support (useful for AutoCAD entities).
        /// </summary>
        public NODTreeNode(string name, string nodeType, string value, string handle)
        {
            Name = name;
            NodeType = nodeType;
            Value = value;
            Handle = handle;
            Children = new List<NODTreeNode>();
        }

        // =========================================================
        // DISPLAY / DEBUG
        // =========================================================

        public override string ToString()
        {
            if (!string.IsNullOrEmpty(Value))
                return $"{Name} : {Value}";

            return $"{Name} ({NodeType})";
        }

        // =========================================================
        // OPTIONAL HELPERS (SAFE ADDITIONS)
        // =========================================================

        /// <summary>
        /// Adds a child node safely and returns it for chaining.
        /// </summary>
        public NODTreeNode AddChild(NODTreeNode child)
        {
            if (child == null)
                return null;

            child.Depth = this.Depth + 1;
            Children.Add(child);
            return child;
        }

        /// <summary>
        /// Convenience helper to create and attach a child node.
        /// </summary>
        public NODTreeNode AddChild(string name, string nodeType, string value = null)
        {
            var child = new NODTreeNode(name, nodeType, value)
            {
                Depth = this.Depth + 1
            };

            Children.Add(child);
            return child;
        }
    }
}