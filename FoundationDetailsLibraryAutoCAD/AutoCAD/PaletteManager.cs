using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Windows;

namespace FoundationDetailer
{
    public static class PaletteManager
    {
        private static PaletteSet _paletteSet;

        // Call this once at startup
        public static void Initialize()
        {
            // Attach to existing documents
            foreach (Document doc in Application.DocumentManager)
                AttachDocumentEvents(doc);

            // Listen for new documents
            Application.DocumentManager.DocumentCreated -= DocManager_DocumentCreated;
            Application.DocumentManager.DocumentCreated += DocManager_DocumentCreated;

            // Create palette set
            CreatePalette();
        }

        private static void DocManager_DocumentCreated(object sender, DocumentCollectionEventArgs e)
        {
            AttachDocumentEvents(e.Document);
        }

        private static void AttachDocumentEvents(Document doc)
        {
            // Attach to database events
            Database db = doc.Database;

            db.ObjectAppended -= Database_BoundaryChanged;
            db.ObjectAppended += Database_BoundaryChanged;

            db.ObjectErased -= Database_BoundaryErased;
            db.ObjectErased += Database_BoundaryErased;

            db.ObjectModified -= Database_BoundaryChanged;
            db.ObjectModified += Database_BoundaryChanged;
        }

        private static void Database_BoundaryChanged(object sender, ObjectEventArgs e)
        {
            // This will fire when objects are added, erased, or modified
            // You can check e.DBObject or filter for Polylines etc.
        }

        private static void Database_BoundaryErased(object sender, ObjectErasedEventArgs e)
        {
            // e.DBObject is the erased object
            // e.Erased tells you if it's being erased or unerased
        }

        private static void CreatePalette()
        {
            if (_paletteSet != null)
                return;

            _paletteSet = new PaletteSet("Boundary Tools");
            _paletteSet.Style = PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowCloseButton;

            // Add a user control to the palette (example)
            System.Windows.Forms.UserControl uc = new System.Windows.Forms.UserControl();
            _paletteSet.Add("Boundary Control", uc);

            _paletteSet.Visible = true;
        }

        public static void ShowPalette()
        {
            if (_paletteSet != null)
                _paletteSet.Visible = true;
        }
    }
}
