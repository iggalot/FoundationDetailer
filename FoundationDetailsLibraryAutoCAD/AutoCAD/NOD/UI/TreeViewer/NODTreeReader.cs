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

                root.Children.Add(child);

                TraverseObject(tr, entry.Value, child);
            }

            return root;
        }

        /// <summary>
        /// Recursively traverses NOD objects.
        /// NOTE: dictionary nodes are flattened to reduce visual noise.
        /// </summary>
        private static void TraverseObject(Transaction tr, ObjectId id, NODTreeNode parent)
        {
            if (id.IsNull)
                return;

            DBObject obj = tr.GetObject(id, OpenMode.ForRead);
            if (obj == null)
                return;

            // ---------------------------
            // Dictionary (flattened)
            // ---------------------------
            var dict = obj as DBDictionary;
            if (dict != null)
            {
                foreach (DBDictionaryEntry entry in dict)
                {
                    var child = new NODTreeNode
                    {
                        Name = entry.Key,
                        NodeType = "Dictionary"
                    };

                    parent.Children.Add(child);

                    TraverseObject(tr, entry.Value, child);
                }
                return;
            }

            // ---------------------------
            // XRecord
            // ---------------------------
            // ---------------------------
            // XRecord (compressed to single line)
            // ---------------------------
            var xrec = obj as Xrecord;
            if (xrec != null)
            {
                var xrecNode = new NODTreeNode("XRecord");

                // Build one-line representation of all values
                ResultBuffer rb = xrec.Data;

                if (rb == null)
                {
                    xrecNode.Value = "<empty>";
                    parent.Children.Add(xrecNode);
                    return;
                }

                List<string> parts = new List<string>();

                foreach (TypedValue tv in rb)
                {
                    parts.Add(TypedValueToString(tv));
                }

                rb.Dispose();

                // IMPORTANT: single-line compression
                xrecNode.Value = string.Join(" | ", parts);

                parent.Children.Add(xrecNode);
                return;
            }

            // ---------------------------
            // Default object type
            // ---------------------------
            parent.Children.Add(new NODTreeNode
            {
                Name = obj.GetType().Name,
                NodeType = "Object"
            });
        }

        /// <summary>
        /// Converts TypedValue into readable string (C# 7.3 safe).
        /// Uses DxfCode enum for stability and readability.
        /// </summary>
        private static string TypedValueToString(TypedValue tv)
        {
            if (tv == null)
                return "<null>";

            short code = tv.TypeCode;
            string typeName = GetDxfTypeName(code);

            if (code == (short)DxfCode.Text)
                return "String: " + (tv.Value ?? "");

            if (code == (short)DxfCode.Real)
                return "Double: " + tv.Value;

            if (code == (short)DxfCode.Int16)
                return "Int16: " + tv.Value;

            if (code == (short)DxfCode.Int32)
                return "Int32: " + tv.Value;

            // These are individual components of a point, not a full point
            if (IsCoordinatePair(code))
                return typeName + ": " + tv.Value;

            if (code == (short)DxfCode.ExtendedDataRegAppName)
                return "AppName: " + (tv.Value ?? "");

            return typeName + ": " + (tv.Value ?? "").ToString();
        }

        private static bool IsCoordinatePair(short code)
        {
            return code == (short)DxfCode.XCoordinate ||
                   code == (short)DxfCode.YCoordinate ||
                   code == (short)DxfCode.ZCoordinate;
        }

        /// <summary>
        /// Maps DXF type codes to human-readable names.
        /// Includes safe fallback for unknown/unsupported codes.
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

            if (code == (short)DxfCode.XCoordinate ||
                code == (short)DxfCode.YCoordinate ||
                code == (short)DxfCode.ZCoordinate)
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