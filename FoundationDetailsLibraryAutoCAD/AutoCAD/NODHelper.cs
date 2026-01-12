using Autodesk.AutoCAD.DatabaseServices;
using FoundationDetailsLibraryAutoCAD.Data;
using System.Collections.Generic;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD
{
    internal static class NODHelper
    {
        public static bool TryGetSubdictEntries(
            FoundationContext context,
            string subdictKey,
            Transaction tr,
            out List<string> entryKeys)
        {
            entryKeys = new List<string>();

            var doc = context.Document;
            var db = doc.Database;

            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(NODManager.ROOT))
                return false;

            var root = (DBDictionary)tr.GetObject(nod.GetAt(NODManager.ROOT), OpenMode.ForRead);
            if (!root.Contains(subdictKey))
                return false;

            var subdict = (DBDictionary)tr.GetObject(root.GetAt(subdictKey), OpenMode.ForRead);
            foreach (DBDictionaryEntry entry in subdict)
                entryKeys.Add(entry.Key);

            return entryKeys.Count > 0;
        }
    }

}
