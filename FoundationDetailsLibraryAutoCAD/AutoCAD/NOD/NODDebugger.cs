using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.NOD
{
    /// <summary>
    /// Tool for displaying and debugging the current drawings NOD structure.
    /// </summary>
    public static class NODDebugger
    {
        /// <summary>
        /// Returns a string showing the full tree structure of the given dictionary,
        /// including all subdictionaries and entities, in a visually indented, sorted tree format.
        /// </summary>
        public static string DumpDictionaryTree(DBDictionary dict, Transaction tr, string dictName = null)
        {
            if (dict == null || tr == null)
                return string.Empty;

            StringBuilder sb = new StringBuilder();
            string rootName = dictName ?? "[Root Dictionary]";
            sb.AppendLine(rootName + " [Dictionary]");

            DumpDictionaryRecursive(dict, tr, sb, "", true);

            return sb.ToString();
        }

        private static void DumpDictionaryRecursive(DBDictionary dict, Transaction tr, StringBuilder sb, string indent, bool isLast)
        {
            if (dict == null) return;

            // Collect entries and sort: dictionaries first, then entities/others; alphabetical by key
            List<DBDictionaryEntry> entries = new List<DBDictionaryEntry>();
            foreach (DBDictionaryEntry entry in dict)
            {
                entries.Add(entry);
            }

            List<DBDictionaryEntry> sortedEntries = entries
                .OrderBy(delegate (DBDictionaryEntry e)
                {
                    DBObject obj = (DBObject)tr.GetObject(e.Value, OpenMode.ForRead);
                    return (obj is DBDictionary) ? 0 : 1; // dictionaries first
                })
                .ThenBy(delegate (DBDictionaryEntry e) { return e.Key; }, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int count = sortedEntries.Count;
            int idx = 0;

            foreach (DBDictionaryEntry entry in sortedEntries)
            {
                idx++;
                bool entryIsLast = (idx == count);

                string branch = entryIsLast ? "└─ " : "├─ ";
                string childIndent = indent + (entryIsLast ? "   " : "│  ");

                DBObject obj = (DBObject)tr.GetObject(entry.Value, OpenMode.ForRead);

                string typeDesc;
                if (obj is DBDictionary)
                {
                    typeDesc = "[Dictionary]";
                }
                else if (obj is Entity)
                {
                    typeDesc = "[Entity] (ID: " + ((Entity)obj).ObjectId.ToString() + ")";
                }
                else
                {
                    typeDesc = "[" + (obj != null ? obj.GetType().Name : "null") + "]";
                }

                sb.AppendLine(indent + branch + entry.Key + " " + typeDesc);

                // Recurse if this entry is a subdictionary
                if (obj is DBDictionary)
                {
                    DumpDictionaryRecursive((DBDictionary)obj, tr, sb, childIndent, true);
                }
            }
        }

        public static void DebugSetImpliedSelection(
Document doc,
IEnumerable<ObjectId> ids,
string tag = null)
        {
            if (doc == null || ids == null)
                return;

            var ed = doc.Editor;

            ed.WriteMessage($"\n[Selection Debug] {tag ?? ""}");

            int index = 0;

            foreach (var id in ids)
            {
                index++;

                try
                {
                    // Basic validity checks
                    if (id.IsNull)
                    {
                        ed.WriteMessage($"\n  #{index}: NULL ObjectId");
                        continue;
                    }

                    if (!id.IsValid)
                    {
                        ed.WriteMessage($"\n  #{index}: INVALID ObjectId ({id})");
                        continue;
                    }

                    if (id.IsErased)
                    {
                        ed.WriteMessage($"\n  #{index}: ERASED ObjectId ({id})");
                        continue;
                    }

                    if (id.Database != doc.Database)
                    {
                        ed.WriteMessage($"\n  #{index}: WRONG DATABASE ({id})");
                        continue;
                    }

                    // Try selecting JUST this ID
                    ed.SetImpliedSelection(new[] { id });

                    ed.WriteMessage(
                        $"\n  #{index}: OK ({id.Handle})");
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    ed.WriteMessage(
                        $"\n  #{index}: FAILED ({id.Handle}) → {ex.ErrorStatus}");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage(
                        $"\n  #{index}: FAILED ({id.Handle}) → {ex.Message}");
                }
            }

            // Clear selection at the end (optional)
            try { ed.SetImpliedSelection(Array.Empty<ObjectId>()); } catch { }
        }

    }
}
