using FoundationDetailsLibraryAutoCAD.Managers;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FoundationDetailsLibraryAutoCAD.UI.Controls.PrelimGBControl
{
    public partial class PrelimGradeBeamControl : UserControl
    {
        private readonly Brush _invalidBrush = Brushes.LightCoral;
        private readonly Brush _validBrush = Brushes.White;

        public PrelimGradeBeamViewModel ViewModel { get; }

        public event EventHandler<PrelimGBEventArgs> AddPreliminaryClicked;

        public class PrelimGBEventArgs : EventArgs
        {
            public int HorzMin { get; set; }
            public int HorzMax { get; set; }
            public int VertMin { get; set; }
            public int VertMax { get; set; }
        }

        public PrelimGradeBeamControl()
        {
            InitializeComponent();
            ViewModel = new PrelimGradeBeamViewModel();
            this.DataContext = ViewModel;
        }

        private void BtnAddGradeBeams_Click(object sender, RoutedEventArgs e)
        {

            // Generate preliminary beams
            ViewModel.GeneratePreliminaryBeams();

            // Disable controls via ViewModel
            ViewModel.IsPreliminaryGenerated = true;

            // Raise event for parent palette
            AddPreliminaryClicked?.Invoke(this, new PrelimGBEventArgs
            {
                HorzMin = ViewModel.HorzMin,
                HorzMax = ViewModel.HorzMax,
                VertMin = ViewModel.VertMin,
                VertMax = ViewModel.VertMax
            });
        }

        private void Spacing_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                if (GridlineManager.IsValidSpacing(tb.Text, out double val))
                {
                    tb.Background = _validBrush;
                    // Optionally store the value somewhere if needed
                }
                else
                {
                    tb.Background = _invalidBrush;
                }
            }
        }
    }
}
