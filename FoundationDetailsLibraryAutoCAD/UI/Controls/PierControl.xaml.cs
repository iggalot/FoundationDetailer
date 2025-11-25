using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using System;
using System.Windows;
using System.Windows.Controls;

namespace FoundationDetailer.UI.Controls
{
    public partial class PierControl : UserControl
    {
        public event Action<PierData> PierAdded;

        // Event requesting AutoCAD to let user pick location
        public event Action RequestPierLocationPick;

        public PierControl()
        {
            InitializeComponent();
            WireEvents();
        }

        private PierData _currentData = new PierData();

        private void WireEvents()
        {
            BtnAddPier.Click += BtnAddPier_Click;
            BtnPickLocation.Click += BtnPickLocation_Click;
        }

        private void BtnPickLocation_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Pick a point in AutoCAD
                Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                Editor ed = doc.Editor;

                PromptPointOptions ppo = new PromptPointOptions("\nSelect pier location:");
                PromptPointResult ppr = ed.GetPoint(ppo);

                if (ppr.Status == PromptStatus.OK)
                {
                    _currentData.X = ppr.Value.X;
                    _currentData.Y = ppr.Value.Y;
                    MessageBox.Show($"Pier location picked: X={_currentData.X:F2}, Y={_currentData.Y:F2}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error picking location: " + ex.Message);
            }
        }

        private void BtnAddPier_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Read all other pier inputs
                PierData data = ReadPierData();

                // Merge with previously picked location
                data.X = _currentData.X;
                data.Y = _currentData.Y;

                // Fire event for parent to convert to model Pier
                PierAdded?.Invoke(data);

                // Reset current data for next pier
                _currentData = new PierData();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Invalid pier input:\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private PierData ReadPierData()
        {
            double diameter = Parse(TxtDiameter.Text, "Diameter");
            double depth = Parse(TxtDepth.Text, "Depth");
            double fc = Parse(TxtConcreteStrength.Text, "Concrete Strength");
            int rebarQty = (int)Parse(TxtRebarQty.Text, "Rebar Qty");
            int rebarSize = (int)Parse(TxtRebarSize.Text, "Rebar Size");

            return new PierData
            {
                Diameter = diameter,
                Depth = depth,
                ConcreteStrength = fc,
                RebarQty = rebarQty,
                RebarSize = rebarSize,
                X = _currentData.X,
                Y = _currentData.Y
            };
        }

        private double Parse(string text, string field)
        {
            if (!double.TryParse(text, out double val))
                throw new Exception($"{field} is not a valid number.");
            return val;
        }
    }

    public class PierData
    {
        public double Diameter { get; set; }
        public double Depth { get; set; }
        public double ConcreteStrength { get; set; }
        public int RebarQty { get; set; }
        public int RebarSize { get; set; }

        public double X { get; set; }
        public double Y { get; set; }
    }
}
