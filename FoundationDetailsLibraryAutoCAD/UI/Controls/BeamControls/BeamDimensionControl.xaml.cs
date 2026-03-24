using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FoundationDetailsLibraryAutoCAD.UI.Controls.BeamControls
{
    public partial class BeamDimensionControl : UserControl
    {
        private const int MIN_SIZE = 4;
        private const int MAX_SIZE = 60;

        private bool _isInitialized = false;
        private bool _isUpdating = false;

        public enum BeamControlMode { Input, Display }

        // ============================
        // MODE PROPERTY
        // ============================
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

        // ============================
        // WIDTH / DEPTH PROPERTIES
        // ============================
        public static readonly DependencyProperty WidthValueProperty =
            DependencyProperty.Register(
                nameof(WidthValue),
                typeof(int),
                typeof(BeamDimensionControl),
                new FrameworkPropertyMetadata(
                    10,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnBeamValueChanged,
                    CoerceSize));

        public int WidthValue
        {
            get => (int)GetValue(WidthValueProperty);
            set => SetValue(WidthValueProperty, value);
        }

        public static readonly DependencyProperty DepthValueProperty =
            DependencyProperty.Register(
                nameof(DepthValue),
                typeof(int),
                typeof(BeamDimensionControl),
                new FrameworkPropertyMetadata(
                    28,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnBeamValueChanged,
                    CoerceSize));

        public int DepthValue
        {
            get => (int)GetValue(DepthValueProperty);
            set => SetValue(DepthValueProperty, value);
        }

        private static object CoerceSize(DependencyObject d, object baseValue)
        {
            int value = (int)baseValue;

            if (value < MIN_SIZE || value > MAX_SIZE)
            {
                System.Diagnostics.Debug.WriteLine($"Invalid beam size coerced: {value}");
            }

            return Math.Max(MIN_SIZE, Math.Min(MAX_SIZE, value));
        }

        // ============================
        // SAFE COMBO SYNC
        // ============================
        private void SyncComboBoxes()
        {
            if (_isUpdating)
                return;

            if (WidthCombo?.ItemsSource == null || DepthCombo?.ItemsSource == null)
                return;

            if (!WidthCombo.Items.Contains(WidthValue))
                WidthCombo.SelectedItem = WidthCombo.Items[0];
            else if (!Equals(WidthCombo.SelectedItem, WidthValue))
                WidthCombo.SelectedItem = WidthValue;

            if (!DepthCombo.Items.Contains(DepthValue))
                DepthCombo.SelectedItem = DepthCombo.Items[0];
            else if (!Equals(DepthCombo.SelectedItem, DepthValue))
                DepthCombo.SelectedItem = DepthValue;
        }

        // ============================
        // DISPLAY UPDATE (NEW)
        // ============================
        private void UpdateDisplayText()
        {
            if (WidthText != null)
                WidthText.Text = $"W: {WidthValue}\"";

            if (DepthText != null)
                DepthText.Text = $"D: {DepthValue}\"";
        }

        // ============================
        // EVENTS
        // ============================
        public class BeamSizeEventArgs : EventArgs
        {
            public double Width { get; }
            public double Depth { get; }

            public BeamSizeEventArgs(double width, double depth)
            {
                Width = width;
                Depth = depth;
            }
        }

        public event EventHandler<BeamSizeEventArgs> Submitted;
        public event EventHandler Canceled;

        // ============================
        // CONSTRUCTOR
        // ============================
        public BeamDimensionControl()
        {
            InitializeComponent();

            var sizes = Enumerable.Range(MIN_SIZE, MAX_SIZE - MIN_SIZE + 1).ToList();

            WidthCombo.ItemsSource = sizes;
            DepthCombo.ItemsSource = sizes;

            // IMPORTANT: Delay full init until UI is ready
            Loaded += (s, e) =>
            {
                _isInitialized = true;

                SyncComboBoxes();
                UpdateDisplayText();
            };
        }

        // ============================
        // PROPERTY CHANGE CALLBACK
        // ============================
        private static void OnBeamValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (BeamDimensionControl)d;

            if (!control._isInitialized || control._isUpdating)
                return;

            try
            {
                control._isUpdating = true;

                control.SyncComboBoxes();
                control.UpdateDisplayText();
            }
            finally
            {
                control._isUpdating = false;
            }

            // Fire AFTER UI is stable
            control.Submitted?.Invoke(
                control,
                new BeamSizeEventArgs(control.WidthValue, control.DepthValue));
        }

        // ============================
        // BUTTON HANDLERS
        // ============================
        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Submitted?.Invoke(this, new BeamSizeEventArgs(WidthValue, DepthValue));
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Canceled?.Invoke(this, EventArgs.Empty);
        }
    }
}