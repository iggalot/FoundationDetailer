using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FoundationDetailsLibraryAutoCAD.UI.Controls.BeamDimensionControl
{
    public partial class BeamDimensionControl : UserControl
    {
        public enum BeamControlMode { Input, Display }

        // Dependency property for mode
        public static readonly DependencyProperty ModeProperty =
            DependencyProperty.Register(
                nameof(Mode),
                typeof(BeamControlMode),
                typeof(BeamDimensionControl),
                new PropertyMetadata(BeamControlMode.Input));

        public BeamControlMode Mode
        {
            get => (BeamControlMode)GetValue(ModeProperty);
            set => SetValue(ModeProperty, value);
        }

        // Event args for submitted beam size
        public class BeamSizeEventArgs : EventArgs
        {
            public int Width { get; }
            public int Depth { get; }

            public BeamSizeEventArgs(int width, int depth)
            {
                Width = width;
                Depth = depth;
            }
        }

        public event EventHandler<BeamSizeEventArgs> Submitted;
        public event EventHandler Canceled;

        public BeamDimensionControl()
        {
            InitializeComponent();

            var sizes = Enumerable.Range(4, 57).ToList(); // 4–60
            WidthCombo.ItemsSource = sizes;
            DepthCombo.ItemsSource = sizes;

            WidthCombo.SelectedItem = 10;
            DepthCombo.SelectedItem = 28;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (WidthCombo.SelectedItem == null || DepthCombo.SelectedItem == null)
                return;

            int width = (int)WidthCombo.SelectedItem;
            int depth = (int)DepthCombo.SelectedItem;

            Submitted?.Invoke(this, new BeamSizeEventArgs(width, depth));
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Canceled?.Invoke(this, EventArgs.Empty);
        }

        // Set displayed width/depth in Display mode
        public void SetDisplayValues(int width, int depth)
        {
            WidthText.Text = $"W: {width}\"";
            DepthText.Text = $"D: {depth}\"";
        }
    }
}
