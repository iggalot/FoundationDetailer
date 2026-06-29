using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using FoundationDetailer.AutoCAD;
using FoundationDetailsLibraryAutoCAD.Data;
using System.Windows;
using System.Windows.Controls;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.NOD.UI.TreeViewer
{
    /// <summary>
    /// UI control that displays the full NOD structure as a TreeView.
    /// </summary>
    public partial class NODTreeViewer : UserControl
    {
        public NODTreeViewer()
        {
            InitializeComponent();
        }

        /// <summary>
        /// External trigger to rebuild the tree from current AutoCAD document.
        /// Call this whenever NOD changes.
        /// </summary>
        public void Refresh()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application
                .DocumentManager
                .MdiActiveDocument;

            if (doc == null) return;

            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var root = NODTreeReader.BuildTree(tr, doc.Database);
                Tree.ItemsSource = new[] { root };
                tr.Commit();
            }
        }

        /// <summary>
        /// Handles selection of a node in the tree.
        /// Can later be extended to zoom/highlight AutoCAD entities.
        /// </summary>
        private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var node = e.NewValue as NODTreeNode;

            if (node == null)
                return;

            if (string.IsNullOrWhiteSpace(node.AutoCADHandle))
                return;

            SelectObject(node.AutoCADHandle);

            System.Diagnostics.Debug.WriteLine(
                    $"Selected: {node.Name} ({node.NodeType})");
        }

        /// <summary>
        /// Selects an AutoCAD object by handle.
        /// </summary>
        private void SelectObject(string handleText)
        {
            try
            {
                Document doc =
                    Autodesk.AutoCAD.ApplicationServices.Application
                    .DocumentManager
                    .MdiActiveDocument;

                if (doc == null)
                    return;

                Database db = doc.Database;

                using (Transaction tr =
                    db.TransactionManager.StartTransaction())
                {
                    Handle handle =
                        new Handle(
                            System.Convert.ToInt64(handleText, 16));

                    ObjectId id =
                        db.GetObjectId(false, handle, 0);

                    if (!id.IsNull)
                    {
                        doc.Editor.SetImpliedSelection(
                            new ObjectId[] { id });

                        doc.Editor.UpdateScreen();
                    }

                    tr.Commit();
                }
            }
            catch
            {
                // Ignore invalid handles.
            }
        }
    }
}