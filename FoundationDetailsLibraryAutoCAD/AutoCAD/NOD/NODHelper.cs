using Autodesk.AutoCAD.DatabaseServices;
using FoundationDetailsLibraryAutoCAD.Data;
using System.Collections.Generic;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.NOD
{
    internal static class NODHelper
    {
        internal static bool TryGetSubdictEntries(
            FoundationContext context,
            string subdictKey,
            Transaction tr,
            out List<string> entryKeys)
        {
            entryKeys = new List<string>();

            var doc = context.Document;
            var db = doc.Database;

            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(NODCore.ROOT))
                return false;

            var root = (DBDictionary)tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForRead);
            if (!root.Contains(subdictKey))
                return false;

            var subdict = (DBDictionary)tr.GetObject(root.GetAt(subdictKey), OpenMode.ForRead);

            // Recursively collect all keys
            CollectDictionaryKeys(subdict, entryKeys, tr);

            return entryKeys.Count > 0;
        }

        private static void CollectDictionaryKeys(DBDictionary dict, List<string> keys, Transaction tr)
        {
            foreach (DBDictionaryEntry entry in dict)
            {
                keys.Add(entry.Key);

                ObjectId id = dict.GetAt(entry.Key);
                if (!id.IsValid || id.IsErased)
                    continue;

                DBObject obj;
                try
                {
                    obj = tr.GetObject(id, OpenMode.ForRead);
                }
                catch
                {
                    continue; // skip unreadable objects
                }

                if (obj is DBDictionary subDict)
                {
                    CollectDictionaryKeys(subDict, keys, tr);
                }
            }
        }

    }

}
