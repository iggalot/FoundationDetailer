using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FoundationDetailsLibraryAutoCAD.UI.Controls.GradeBeamSummaryControl
{
    public class GradeBeamSummaryViewModel : INotifyPropertyChanged
    {
        private int _quantity;
        private double _totalLength;

        public int Quantity
        {
            get => _quantity;
            set
            {
                if (_quantity != value)
                {
                    _quantity = value;
                    OnPropertyChanged();
                }
            }
        }

        public double TotalLength
        {
            get => _totalLength;
            set
            {
                if (_totalLength != value)
                {
                    _totalLength = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}