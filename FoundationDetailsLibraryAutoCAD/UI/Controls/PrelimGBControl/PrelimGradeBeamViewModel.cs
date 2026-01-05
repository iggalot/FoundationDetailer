using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FoundationDetailsLibraryAutoCAD.UI.Controls.PrelimGBControl
{
    public class PrelimGradeBeamViewModel : INotifyPropertyChanged
    {
        private bool _isPreliminaryGenerated;

        public bool IsPreliminaryGenerated
        {
            get => _isPreliminaryGenerated;
            set
            {
                if (_isPreliminaryGenerated != value)
                {
                    _isPreliminaryGenerated = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanEditSpacingAndGenerate));
                }
            }
        }

        public bool CanEditSpacingAndGenerate => !_isPreliminaryGenerated;

        // Spacing inputs
        private int _horzMin = 60;
        public int HorzMin { get => _horzMin; set { _horzMin = value; OnPropertyChanged(); } }

        private int _horzMax = 144;
        public int HorzMax { get => _horzMax; set { _horzMax = value; OnPropertyChanged(); } }

        private int _vertMin = 60;
        public int VertMin { get => _vertMin; set { _vertMin = value; OnPropertyChanged(); } }

        private int _vertMax = 144;
        public int VertMax { get => _vertMax; set { _vertMax = value; OnPropertyChanged(); } }

        // Summary
        public int Quantity { get; set; } = 0;
        public int Width { get; set; } = 12;
        public int Depth { get; set; } = 28;
        public double TotalLength { get; set; } = 0.0;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));

        public void GeneratePreliminaryBeams()
        {
            // TODO: your existing preliminary grade beam generation logic here
            // Update Quantity, TotalLength, etc.
            Quantity = 10; // example
            TotalLength = 120.0; // example
            OnPropertyChanged(nameof(Quantity));
            OnPropertyChanged(nameof(TotalLength));
        }
    }
}
