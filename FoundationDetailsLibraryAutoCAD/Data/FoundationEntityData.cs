using Autodesk.AutoCAD.DatabaseServices;

namespace FoundationDetailsLibraryAutoCAD.Data
{
    internal static class FoundationEntityData
    {
        public class FoundationEntityInfo
        {
            public string GroupName { get; set; }
            public int Version { get; set; }
            public string Handle { get; set; }
        }

        private const string ROOT = "EE_FOUNDATION";

        internal static void Write(
            Transaction tr,
            Entity ent,
            string groupName)
        {
            ent.UpgradeOpen();

            if (ent.ExtensionDictionary.IsNull)
                ent.CreateExtensionDictionary();

            var dict = (DBDictionary)tr.GetObject(
                ent.ExtensionDictionary, OpenMode.ForWrite);

            Xrecord xr = new Xrecord
            {
                Data = new ResultBuffer(
                    new TypedValue((int)DxfCode.Text, groupName),
                    new TypedValue((int)DxfCode.Int32, 1) // version
                )
            };

            dict.SetAt(ROOT, xr);
            tr.AddNewlyCreatedDBObject(xr, true);
        }

        internal static bool HasFoundationData(
            Transaction tr,
            Entity ent)
        {
            if (ent.ExtensionDictionary.IsNull)
                return false;

            var dict = (DBDictionary)tr.GetObject(
                ent.ExtensionDictionary, OpenMode.ForRead);

            return dict.Contains(ROOT);
        }

        internal static bool TryRead(Transaction tr, Entity ent, out string groupName)
        {
            groupName = null;
            if (ent.ExtensionDictionary.IsNull) return false;

            var dict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, OpenMode.ForRead);
            if (!dict.Contains(ROOT)) return false;

            var xr = (Xrecord)tr.GetObject(dict.GetAt(ROOT), OpenMode.ForRead);
            if (xr.Data == null) return false;

            foreach (TypedValue tv in xr.Data)
            {
                if (tv.TypeCode == (int)DxfCode.Text)
                {
                    groupName = tv.Value as string;
                    return true;
                }
            }

            return false;
        }

        public static void DisplayExtensionData(Entity ent)
        {
            if (ent.ExtensionDictionary.IsNull)
            {
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage(
                    $"\nEntity {ent.Handle} has no ExtensionDictionary.");
                return;
            }

            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                DBDictionary dict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, OpenMode.ForRead);
                foreach (DBDictionaryEntry entry in dict)
                {
                    var obj = tr.GetObject(entry.Value, OpenMode.ForRead);

                    if (obj is Xrecord xr)
                    {
                        doc.Editor.WriteMessage($"\nXrecord: {entry.Key}");
                        foreach (TypedValue tv in xr.Data)
                        {
                            doc.Editor.WriteMessage($"\n  Type: {tv.TypeCode}, Value: {tv.Value}");
                        }
                    }
                    else if (obj is DBDictionary subDict)
                    {
                        doc.Editor.WriteMessage($"\nSubdictionary: {entry.Key}");
                    }
                    else
                    {
                        doc.Editor.WriteMessage($"\nUnknown object: {entry.Key}, Type: {obj.GetType().Name}");
                    }
                }
                tr.Commit();
            }
        }

    }

}
