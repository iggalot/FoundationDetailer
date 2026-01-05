using System.Windows.Controls;

namespace FoundationDetailsLibraryAutoCAD.UI.Controls.GradeBeamSummaryControl
{
    public partial class GradeBeamSummaryControl : UserControl
    {
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
    }
}