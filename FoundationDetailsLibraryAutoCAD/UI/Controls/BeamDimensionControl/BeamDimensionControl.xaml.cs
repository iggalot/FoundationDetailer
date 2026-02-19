// Usage of this control:
// INPUT SELECTOR
//using FoundationDetailsLibraryAutoCAD.UI.Controls.BeamDimensionControl;

//var control = new BeamDimensionControl
//{
//    Mode = BeamDimensionMode.Input
//};

//control.Submitted += (s, e) =>
//{
//    int width = e.Width;
//    int depth = e.Depth;
//};

// DISPLAY MODE:
//using FoundationDetailsLibraryAutoCAD.UI.Controls.BeamDimensionControl;

//var control = new BeamDimensionControl
//{
//    Mode = BeamDimensionMode.Display,
//    WidthValue = 16,
//    DepthValue = 30
//};


using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FoundationDetailsLibraryAutoCAD.UI.Controls.BeamDimensionControl
{
    public enum BeamDimensionMode
    {
        Input,
        Display
    }

    public partial class BeamDimensionControl : UserControl
    {
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

        // -----------------------------
        // Dependency Properties
        // -----------------------------

        public static readonly DependencyProperty WidthValueProperty =
            DependencyProperty.Register(nameof(WidthValue),
                typeof(int),
                typeof(BeamDimensionControl),
                new PropertyMetadata(10));

        public static readonly DependencyProperty DepthValueProperty =
            DependencyProperty.Register(nameof(DepthValue),
                typeof(int),
                typeof(BeamDimensionControl),
                new PropertyMetadata(28));

        public static readonly DependencyProperty ModeProperty =
            DependencyProperty.Register(nameof(Mode),
                typeof(BeamDimensionMode),
                typeof(BeamDimensionControl),
                new PropertyMetadata(BeamDimensionMode.Input));

        public int WidthValue
        {
            get => (int)GetValue(WidthValueProperty);
            set => SetValue(WidthValueProperty, value);
        }

        public int DepthValue
        {
            get => (int)GetValue(DepthValueProperty);
            set => SetValue(DepthValueProperty, value);
        }

        public BeamDimensionMode Mode
        {
            get => (BeamDimensionMode)GetValue(ModeProperty);
            set => SetValue(ModeProperty, value);
        }

        // -----------------------------
        // Events
        // -----------------------------

        public event EventHandler<BeamSizeEventArgs> Submitted;
        public event EventHandler Canceled;

        // -----------------------------
        // Constructor
        // -----------------------------

        public BeamDimensionControl()
        {
            InitializeComponent();

            var sizes = Enumerable.Range(4, 57).ToList(); // 4–60 inches

            WidthCombo.ItemsSource = sizes;
            DepthCombo.ItemsSource = sizes;
        }

        // -----------------------------
        // Button Handlers
        // -----------------------------

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Submitted?.Invoke(this,
                new BeamSizeEventArgs(WidthValue, DepthValue));
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Canceled?.Invoke(this, EventArgs.Empty);
        }
    }
}
