using System;
using System.Windows;
using System.Windows.Controls;

namespace FoundationDetailsLibraryAutoCAD.UI.Controls.GradeBeamSummaryControl
{
    public partial class GradeBeamSummaryControl : UserControl
    {
        public event EventHandler ClearAllClicked;
        public GradeBeamSummaryViewModel ViewModel { get; }

        public GradeBeamSummaryControl()
        {
            InitializeComponent();
            ViewModel = new GradeBeamSummaryViewModel();
            this.DataContext = ViewModel;
        }

        /// <summary>
        /// Update the displayed summary
        /// </summary>
        public void UpdateSummary(int quantity, double totalLength)
        {
            ViewModel.Quantity = quantity;
            ViewModel.TotalLength = totalLength;
        }

        private void BtnClearAll_Click(object sender, RoutedEventArgs e)
        {
            ClearAllClicked?.Invoke(this, EventArgs.Empty);
        }
    }
}