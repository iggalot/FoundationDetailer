using Autodesk.AutoCAD.DatabaseServices;

namespace FoundationDetailsLibraryAutoCAD.Data
{
    internal static class FoundationEntityData
    {
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
    }

}
