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
            // XRecord (robust key → grouped values)
            // ---------------------------
            var xrec = obj as Xrecord;
            if (xrec != null)
            {
                ResultBuffer rb = xrec.Data;

                if (rb == null)
                {
                    parent.Children.Add(new NODTreeNode
                    {
                        Name = "XRecord",
                        NodeType = "XRecord",
                        Value = "<empty>"
                    });
                    return;
                }

                using (rb)
                {
                    TypedValue[] values = rb.AsArray();

                    string currentKey = null;
                    List<string> buffer = new List<string>();

                    for (int i = 0; i < values.Length; i++)
                    {
                        TypedValue tv = values[i];
                        string text = TypedValueToString(tv);

                        bool isKey =
                            tv.TypeCode == (short)DxfCode.ExtendedDataAsciiString ||
                            tv.TypeCode == (short)DxfCode.Text;

                        // ---------------------------
                        // NEW KEY FOUND → flush previous
                        // ---------------------------
                        if (isKey)
                        {
                            if (currentKey != null)
                            {
                                parent.Children.Add(new NODTreeNode
                                {
                                    Name = currentKey + " → " + string.Join(" | ", buffer),
                                    NodeType = "XRecord"
                                });

                                buffer.Clear();
                            }

                            currentKey = text;
                            continue;
                        }

                        // ---------------------------
                        // VALUE (ANY TYPE)
                        // ---------------------------
                        if (currentKey != null)
                        {
                            buffer.Add(text);
                        }
                    }

                    // flush last group
                    if (currentKey != null)
                    {
                        parent.Children.Add(new NODTreeNode
                        {
                            Name = currentKey + " → " + string.Join(" | ", buffer),
                            NodeType = "XRecord"
                        });
                    }
                }

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
        /// Uses DXF codes for stability across AutoCAD versions.
        /// </summary>
        private static string TypedValueToString(TypedValue tv)
        {
            if (tv == null)
                return "<null>";

            short code = tv.TypeCode;
            string typeName = GetDxfTypeName(code);

            if (code == (short)DxfCode.Text)
                return (tv.Value ?? "").ToString();

            if (code == (short)DxfCode.ExtendedDataAsciiString)
                return (tv.Value ?? "").ToString();

            if (code == (short)DxfCode.Real)
                return (tv.Value ?? "").ToString();

            if (code == (short)DxfCode.Int16)
                return (tv.Value ?? "").ToString();

            if (code == (short)DxfCode.Int32)
                return (tv.Value ?? "").ToString();

            if (code == (short)DxfCode.XCoordinate ||
                code == (short)DxfCode.YCoordinate ||
                code == (short)DxfCode.ZCoordinate)
                return (tv.Value ?? "").ToString();

            return typeName + ": " + (tv.Value ?? "").ToString();
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

            if (code == (short)DxfCode.XCoordinate ||
                code == (short)DxfCode.YCoordinate ||
                code == (short)DxfCode.ZCoordinate)
                return "Coordinate";

            if (code == (short)DxfCode.ExtendedDataAsciiString)
                return "String";

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