using Autodesk.AutoCAD.ApplicationServices;
using FoundationDetailer.Model;
using FoundationDetailsLibraryAutoCAD.Data;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD
{
    internal class FoundationPersistenceManager
    {
        public void Save(FoundationContext context)
        {
            NODManager.ExportFoundationNOD(context);
        }

        public void Load(FoundationContext context)
        {
            NODManager.ImportFoundationNOD(context);
        }

        public void Query(FoundationContext context)
        {
            NODManager.CleanFoundationNOD(context);
            NODManager.ViewFoundationNOD(context);
        }
    }
}
