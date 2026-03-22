using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FoundationDetailsLibraryAutoCAD.UI.Controls.GradeBeamSummaryControl
{
    public class GradeBeamSummaryViewModel : INotifyPropertyChanged
    {
        private int _quantity;
        public int Quantity { get => _quantity; set { _quantity = value; OnPropertyChanged(); } }

        private double _totalLength;
        public double TotalLength { get => _totalLength; set { _totalLength = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}