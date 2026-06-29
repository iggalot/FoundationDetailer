using Autodesk.AutoCAD.DatabaseServices;
using FoundationDetailsLibraryAutoCAD.Data;
using System.Collections;
using System.Text;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.NOD
{
    /// <summary>
    /// ------------------------------------------------------------
    /// NODInspector
    /// ------------------------------------------------------------
    /// Purpose:
    ///     Read-only inspection + debugging tool for EE_Foundation NOD.
    ///
    /// Responsibilities:
    ///     - Recursively print full NOD tree
    ///     - Decode ObjectId → DBObject relationships
    ///     - Display Xrecord contents
    ///     - Safely show string metadata (future extension)
    ///     - NEVER modify the database
    ///
    /// This is your primary debugging and verification tool.
    /// ------------------------------------------------------------
    /// </summary>
    public static class NODInspector
    {
        // ==========================================================
        // PUBLIC ENTRY POINT
        // ==========================================================
        /// <summary>
        /// Dumps the entire EE_Foundation NOD tree as a string.
        /// </summary>
        public static string Dump(FoundationContext context)
        {
            if (context?.Document == null)
                return "Invalid context.";

            var db = context.Document.Database;
            var sb = new StringBuilder();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var root = NODCore.GetFoundationRootDictionary(tr, db);

                if (root == null)
                    return "EE_Foundation not found.";

                sb.AppendLine("EE_Foundation");

                DumpDictionary(tr, root, sb, 1);

                tr.Commit();
            }

            return sb.ToString();
        }

        // ==========================================================
        // RECURSIVE DICTIONARY DUMP
        // ==========================================================
        /// <summary>
        /// Recursively walks a DBDictionary and prints its contents.
        /// Fully schema-agnostic and future-proof.
        /// </summary>
        private static void DumpDictionary(
            Transaction tr,
            DBDictionary dict,
            StringBuilder sb,
            int depth)
        {
            if (dict == null || dict.IsErased)
                return;

            string indent = new string(' ', depth * 2);

            foreach (DictionaryEntry entry in dict)
            {
                string key = entry.Key?.ToString() ?? "<null>";
                object value = entry.Value;

                // --------------------------------------------------
                // CASE 1: OBJECT ID (AutoCAD DBObject reference)
                // --------------------------------------------------
                if (value is ObjectId id)
                {
                    if (!id.IsValid || id.IsErased)
                    {
                        sb.AppendLine($"{indent}{key} -> <invalid ObjectId>");
                        continue;
                    }

                    DBObject obj = tr.GetObject(id, OpenMode.ForRead, false);

                    switch (obj)
                    {
                        case DBDictionary subDict:
                            sb.AppendLine($"{indent}{key}");
                            DumpDictionary(tr, subDict, sb, depth + 1);
                            break;

                        case Xrecord xr:
                            sb.AppendLine($"{indent}{key} -> Xrecord");
                            DumpXrecord(xr, sb, indent + "  ");
                            break;

                        default:
                            sb.AppendLine($"{indent}{key} -> {obj?.GetType().Name}");
                            break;
                    }
                }

                // --------------------------------------------------
                // CASE 2: STRING METADATA (FUTURE SUPPORT)
                // --------------------------------------------------
                else if (value is string s)
                {
                    sb.AppendLine($"{indent}{key} = \"{s}\"");
                }

                // --------------------------------------------------
                // CASE 3: UNKNOWN TYPES (SAFE FALLBACK)
                // --------------------------------------------------
                else
                {
                    sb.AppendLine($"{indent}{key} -> <unknown: {value?.GetType().Name ?? "null"}>");
                }
            }
        }

        // ==========================================================
        // XRECORD DECODER
        // ==========================================================
        /// <summary>
        /// Prints the raw contents of an Xrecord.
        /// Useful for debugging metadata stored in NOD.
        /// </summary>
        private static void DumpXrecord(
            Xrecord xr,
            StringBuilder sb,
            string indent)
        {
            if (xr?.Data == null)
            {
                sb.AppendLine($"{indent}(empty)");
                return;
            }

            foreach (TypedValue tv in xr.Data)
            {
                sb.AppendLine($"{indent}{tv.TypeCode} = {tv.Value}");
            }
        }
    }
}