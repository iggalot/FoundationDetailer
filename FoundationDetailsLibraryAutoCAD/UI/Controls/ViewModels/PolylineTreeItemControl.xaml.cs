using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FoundationDetailsLibraryAutoCAD.UI.Controls
{
    public partial class PolylineTreeItemControl : UserControl
    {
        public PolylineTreeItemControl()
        {
            InitializeComponent();
            this.MouseLeftButtonUp += PolylineTreeItemControl_MouseLeftButtonUp;
        }

        private void PolylineTreeItemControl_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Only select if the click wasn’t handled by a child control
            if (!e.Handled)
            {
                var treeViewItem = FindAncestor<TreeViewItem>(this);
                if (treeViewItem != null)
                    treeViewItem.IsSelected = true;
            }
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null && !(current is T))
                current = VisualTreeHelper.GetParent(current);
            return current as T;
        }
    }
}
