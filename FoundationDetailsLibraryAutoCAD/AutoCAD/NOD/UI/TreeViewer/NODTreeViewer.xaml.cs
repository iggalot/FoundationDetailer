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
            if (e.NewValue is NODTreeNode node)
            {
                // Placeholder for future integration:
                // - zoom to handle
                // - highlight entity
                // - inspect xrecord details

                System.Diagnostics.Debug.WriteLine(
                    $"Selected: {node.Name} ({node.NodeType})");
            }
        }
    }
}