using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.NOD.UI.TreeViewer
{
    /// <summary>
    /// Reads the AutoCAD Named Objects Dictionary (NOD)
    /// and converts it into a hierarchical NODTreeNode structure
    /// for visualization in the TreeViewer.
    /// </summary>
    public static class NODTreeReader
    {
        /// <summary>
        /// Entry point: builds full NOD tree.
        /// </summary>
        public static NODTreeNode BuildTree(Transaction tr, Database db)
        {
            var root = new NODTreeNode("NOD Root");

            // IMPORTANT: use foundation root if available
            var nod = NODCore.GetFoundationRootDictionary(tr, db)
                      ?? tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead) as DBDictionary;

            if (nod == null)
                return root;

            foreach (DBDictionaryEntry entry in nod)
            {
                var child = new NODTreeNode
                {
                    Name = entry.Key,
                    NodeType = "Dictionary"
                };

                if (TraverseObject(tr, entry.Value, child))
                {
                    root.Children.Add(child);
                }
            }

            return root;
        }

        /// <summary>
        /// Recursively traverses NOD objects.
        /// Returns TRUE if node contains visible content.
        /// Used for pruning empty dictionary branches.
        /// </summary>
        private static bool TraverseObject(Transaction tr, ObjectId id, NODTreeNode parent)
        {
            if (id.IsNull)
                return false;

            DBObject obj = tr.GetObject(id, OpenMode.ForRead);
            if (obj == null)
                return false;

            // ---------------------------
            // Dictionary (flatten + count children)
            // ---------------------------
            var dict = obj as DBDictionary;
            if (dict != null)
            {
                int visibleCount = 0;

                var tempChildren = new List<NODTreeNode>();

                foreach (DBDictionaryEntry entry in dict)
                {
                    var child = new NODTreeNode
                    {
                        Name = entry.Key,
                        NodeType = "Dictionary"
                    };

                    if (TraverseObject(tr, entry.Value, child))
                    {
                        tempChildren.Add(child);
                        visibleCount++;
                    }
                }

                if (visibleCount == 0)
                    return false;

                // apply children
                parent.Children.AddRange(tempChildren);

                // append count to label
                parent.Name = parent.Name + " (" + visibleCount + ")";

                return true;
            }

            // ---------------------------
            // XRecord (LEFT_0 / LEFT_1 preserved + value included)
            // ---------------------------
            var xrec = obj as Xrecord;
            if (xrec != null)
            {
                ResultBuffer rb = xrec.Data;
                if (rb == null)
                    return false;

                foreach (TypedValue tv in rb)
                {
                    string text = TypedValueToString(tv);

                    // Expect format like: "LEFT_0 : value"
                    string label;
                    string value;

                    SplitLabelValue(text, out label, out value);

                    var node = new NODTreeNode
                    {
                        Name = label,
                        NodeType = "XRecord",
                        Value = value
                    };

                    parent.Children.Add(node);
                }

                rb.Dispose();
                return parent.Children.Count > 0;
            }

            // ---------------------------
            // Default object type
            // ---------------------------
            parent.Children.Add(new NODTreeNode
            {
                Name = obj.GetType().Name,
                NodeType = "Object"
            });

            return true;
        }

        /// <summary>
        /// Splits "LEFT_0 : 3F2A9" into label/value parts.
        /// </summary>
        private static void SplitLabelValue(string input, out string label, out string value)
        {
            label = input;
            value = "";

            if (string.IsNullOrEmpty(input))
                return;

            int idx = input.IndexOf(':');
            if (idx > 0)
            {
                label = input.Substring(0, idx).Trim();
                value = input.Substring(idx + 1).Trim();
            }
        }

        /// <summary>
        /// Converts TypedValue into readable string (C# 7.3 safe).
        /// </summary>
        private static string TypedValueToString(TypedValue tv)
        {
            if (tv == null)
                return "<null>";

            short code = tv.TypeCode;
            string typeName = GetDxfTypeName(code);

            if (code == (short)DxfCode.Text)
                return typeName + ": " + (tv.Value ?? "");

            if (code == (short)DxfCode.Real)
                return typeName + ": " + tv.Value;

            if (code == (short)DxfCode.Int16)
                return typeName + ": " + tv.Value;

            if (code == (short)DxfCode.Int32)
                return typeName + ": " + tv.Value;

            if (IsCoordinate(code))
                return typeName + ": " + tv.Value;

            return typeName + ": " + (tv.Value ?? "").ToString();
        }

        private static bool IsCoordinate(short code)
        {
            return code == (short)DxfCode.XCoordinate ||
                   code == (short)DxfCode.YCoordinate ||
                   code == (short)DxfCode.ZCoordinate;
        }

        /// <summary>
        /// Maps DXF type codes to human-readable names.
        /// </summary>
        private static string GetDxfTypeName(short code)
        {
            if (code == (short)DxfCode.Text)
                return "String";

            if (code == (short)DxfCode.Real)
                return "Double";

            if (code == (short)DxfCode.Int16)
                return "Int16";

            if (code == (short)DxfCode.Int32)
                return "Int32";

            if (IsCoordinate(code))
                return "Coordinate";

            if (code == (short)DxfCode.ExtendedDataRegAppName)
                return "AppName";

            return "Unknown";
        }

        private static string FormatPoint(object value)
        {
            try
            {
                var pt = (Point3d)value;
                return string.Format("({0}, {1}, {2})", pt.X, pt.Y, pt.Z);
            }
            catch
            {
                return value != null ? value.ToString() : "null";
            }
        }
    }
}