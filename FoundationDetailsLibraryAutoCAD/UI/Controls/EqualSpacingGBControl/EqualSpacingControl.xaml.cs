using Autodesk.AutoCAD.Geometry;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FoundationDetailsLibraryAutoCAD.UI.Controls.EqualSpacingGBControl
{
    public partial class EqualSpacingControl : UserControl
    {
        // Geometry
        private Point3d _start;
        private Point3d _end;
        private double _spanLength;

        // Spacing constraints
        private double _minSpacing = 60.0;
        private double _maxSpacing = 144.0;

        // Computed limits
        private int _minCount;
        private int _maxCount;
        private int _currentCount;

        private bool _isUpdating;

        private SpacingDirections? SelectedDirection
        {
            get
            {
                if (DirectionCombo.SelectedItem is SpacingDirections dir)
                    return dir;

                return null;
            }
        }

        private bool TryGetDirection(out SpacingDirections direction)
        {
            direction = default;

            if (DirectionCombo.SelectedItem == null)
                return false;

            if (!(DirectionCombo.SelectedItem is SpacingDirections dir))
                return false;

            direction = dir;
            return true;
        }

        private void UpdateDirectionState(bool isValid)
        {
            DirectionCombo.BorderBrush =
                isValid ? SystemColors.ControlDarkBrush : Brushes.Red;
        }




        public event Action<SpacingRequest> DrawRequested;

        public EqualSpacingControl()
        {
            InitializeComponent();

            MinSpacingBox.Text = _minSpacing.ToString(CultureInfo.InvariantCulture);
            MaxSpacingBox.Text = _maxSpacing.ToString(CultureInfo.InvariantCulture);

            MinSpacingBox.TextChanged += ConstraintChanged;
            MaxSpacingBox.TextChanged += ConstraintChanged;
            CountBox.TextChanged += CountChanged;

            DirectionCombo.ItemsSource = Enum.GetValues(typeof(SpacingDirections));
            DirectionCombo.SelectedIndex = 0; // safe default

            ApplyButton.IsEnabled = false;
        }

        // =========================
        // PUBLIC API (Palette calls)
        // =========================

        private bool _pointsSelected = false;

        internal void SetSpan(Point3d start, Point3d end)
        {
            _start = start;
            _end = end;
            _spanLength = start.DistanceTo(end);

            _pointsSelected = true; // mark that we have valid points

            SpanText.Text = _spanLength.ToString("0.###");
            RecalculateCounts();
            UpdateUI();

            ApplyButton.IsEnabled = true;
        }


        // =========================
        // CORE LOGIC
        // =========================

        private void RecalculateCounts()
        {
            if (_spanLength <= 0 || _minSpacing <= 0 || _maxSpacing <= 0)
                return;

            _minCount = (int)Math.Ceiling(_spanLength / _maxSpacing) + 1;
            _maxCount = (int)Math.Floor(_spanLength / _minSpacing) + 1;

            if (_minCount > _maxCount)
            {
                _minCount = _maxCount;
            }

            if (_currentCount == 0)
                _currentCount = _maxCount;

            _currentCount = Math.Max(_minCount, Math.Min(_currentCount, _maxCount));
        }

        private double CurrentSpacing
        {
            get
            {
                if (_currentCount <= 1)
                    return 0;

                return _spanLength / (_currentCount - 1);
            }
        }

        private void UpdateUI()
        {
            _isUpdating = true;

            CountBox.Text = _currentCount.ToString(CultureInfo.InvariantCulture);
            ResultSpacingText.Text = CurrentSpacing.ToString("0.###");

            _isUpdating = false;
        }

        // =========================
        // UI HANDLERS
        // =========================

        private void ConstraintChanged(object sender, TextChangedEventArgs e)
        {
            if (!double.TryParse(MinSpacingBox.Text, out _minSpacing)) return;
            if (!double.TryParse(MaxSpacingBox.Text, out _maxSpacing)) return;

            if (_minSpacing > _maxSpacing)
                return;

            RecalculateCounts();
            UpdateUI();
        }

        private void CountChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating)
                return;

            if (!int.TryParse(CountBox.Text, out var value))
                return;

            _currentCount = Math.Max(_minCount, Math.Min(value, _maxCount));
            UpdateUI();
        }

        private void SliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdating)
                return;

            _currentCount = (int)e.NewValue;
            UpdateUI();
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetDirection(out SpacingDirections direction))
                return;

            if (!TryReadSpacingInputs())
                return;

            if (_currentCount < 1)
                return;

            Debug.WriteLine($"DrawRequested is {(DrawRequested != null ? "set" : "null")}");

            DrawRequested?.Invoke(new SpacingRequest
            {
                MaxSpa = _maxSpacing,
                MinSpa = _minSpacing,
                Direction = direction,
                Count = _currentCount,
            });
        }

        /// <summary>
        /// Reads and validates the min/max spacing and the direction from the UI.
        /// Updates _minSpacing, _maxSpacing, and outputs the selected direction if valid.
        /// </summary>
        /// <param name="direction">Outputs the selected spacing direction.</param>
        /// <returns>True if all inputs are valid; false otherwise.</returns>
        private bool TryReadSpacingInputs()
        {
            // --- Min/Max Spacing ---
            if (!double.TryParse(MinSpacingBox.Text, out double min))
                return false;

            if (!double.TryParse(MaxSpacingBox.Text, out double max))
                return false;

            if (min <= 0 || max <= 0)
                return false;

            if (min > max)
                return false;

            // Store validated spacing
            _minSpacing = min;
            _maxSpacing = max;

            return true;
        }


    }

    // =========================
    // REQUEST DTO
    // =========================
    public enum SpacingDirections
    {
        Perpendicular,
        Horizontal,
        Vertical
    }


    public class SpacingRequest
    {
        public double MaxSpa { get; set; } = 144.0;
        public double MinSpa { get; set; } = 60.0;
        public SpacingDirections Direction { get; set; } = SpacingDirections.Horizontal;
        public int Count { get; set; } = 0;
        public Point3d Start { get; set; }
        public Point3d End { get; set; }
    }
}

