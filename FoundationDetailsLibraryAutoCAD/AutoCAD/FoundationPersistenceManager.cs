namespace FoundationDetailsLibraryAutoCAD.AutoCAD
{
    internal class FoundationPersistenceManager
    {
        public void Save()
        {
            NODManager.ExportFoundationNOD();
        }

        public void Load()
        {
            NODManager.ImportFoundationNOD();
        }

        public void Query()
        {
            NODManager.CleanFoundationNOD();
            NODManager.ViewFoundationNOD();
        }
    }
}
