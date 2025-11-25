using FoundationDetailer.AutoCAD;
using FoundationDetailer.Model;
using FoundationDetailer.Storage;
using FoundationDetailer.UI.Controls;
using FoundationDetailer.UI.Converters;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace FoundationDetailer.UI
{
    public partial class PaletteMain : UserControl
    {
        private FoundationModel _currentModel = new FoundationModel();

        private PierControl PierUI;

        public FoundationModel CurrentModel
        {
            get => _currentModel;
            set => _currentModel = value;
        }

        public PaletteMain()
        {
            InitializeComponent();

            // Initialize the PierControl
            PierUI = new PierControl();
            PierUI.PierAdded += OnPierAdded;
            PierUI.RequestPierLocationPick += PickPierLocation;

            // Add it to a container in your XAML (e.g., a StackPanel named PierContainer)
            PierContainer.Children.Clear();
            PierContainer.Children.Add(PierUI);

            WireEvents();
        }

        private void WireEvents()
        {
            BtnSelectBoundary.Click += (s, e) => SelectBoundary();
            BtnAddPiers.Click += (s, e) => AddPiers();
            BtnAddGradeBeams.Click += (s, e) => AddGradeBeams();
            BtnAddRebar.Click += (s, e) => AddRebarBars();
            BtnAddStrands.Click += (s, e) => AddStrands();
            BtnPreview.Click += (s, e) => ShowPreview();
            BtnClearPreview.Click += (s, e) => ClearPreview();
            BtnCommit.Click += (s, e) => CommitModel();
            BtnSave.Click += (s, e) => SaveModel();
            BtnLoad.Click += (s, e) => LoadModel();
        }

        #region --- Button Handlers ---

        private void SelectBoundary()
        {
            MessageBox.Show("Select boundary in AutoCAD.");
        }

        private void AddPiers()
        {
            MessageBox.Show("Add piers to model.");
        }

        private void AddGradeBeams()
        {
            MessageBox.Show("Add grade beams to model.");
        }

        private void AddRebarBars()
        {
            MessageBox.Show("Add rebar bars to model.");
        }

        private void AddStrands()
        {
            MessageBox.Show("Add strands to model.");
        }

        private void ShowPreview()
        {
            try
            {
                PreviewManager.ShowPreview(_currentModel);
                TxtStatus.Text = "Preview shown.";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Preview error: {ex.Message}";
            }
        }

        private void ClearPreview()
        {
            PreviewManager.ClearPreview();
            TxtStatus.Text = "Preview cleared.";
        }

        private void CommitModel()
        {
            try
            {
                AutoCADAdapter.CommitModelToDrawing(_currentModel);
                TxtStatus.Text = "Model committed to DWG.";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Commit error: {ex.Message}";
            }
        }

        private void SaveModel()
        {
            string filePath = "FoundationProject.json"; // can use SaveFileDialog
            try
            {
                JsonStorage.Save(filePath, _currentModel);
                TxtStatus.Text = $"Model saved to {filePath}";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Save error: {ex.Message}";
            }
        }

        private void LoadModel()
        {
            string filePath = "FoundationProject.json"; // can use OpenFileDialog
            try
            {
                var model = JsonStorage.Load<FoundationModel>(filePath);
                if (model != null)
                {
                    _currentModel = model;
                    TxtStatus.Text = "Model loaded.";
                }
                else
                {
                    TxtStatus.Text = "No saved model found.";
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Load error: {ex.Message}";
            }
        }

        #endregion

        #region --- PierControl Handlers ---

        private void OnPierAdded(PierData data)
        {
            Pier pier = PierConverter.ToModelPier(data);
            CurrentModel.Piers.Add(pier);
            TxtStatus.Text = $"Pier added at ({pier.Location.X:F2}, {pier.Location.Y:F2})";
        }

        private void PickPierLocation()
        {
            MessageBox.Show("Pick pier location in AutoCAD.");
            // Implement AutoCAD picking logic here
        }

        private void InitPierControl()
        {
            PierControl pierUI = new PierControl();
            pierUI.PierAdded += data =>
            {
                // Convert to model Pier and add
                Pier pier = PierConverter.ToModelPier(data);
                CurrentModel.Piers.Add(pier);
                TxtStatus.Text = $"Pier added at X={pier.Location.X:F2}, Y={pier.Location.Y:F2}";
            };

            // Add pierUI to a container in your PaletteMain if needed
            PierContainer.Children.Clear();
            PierContainer.Children.Add(pierUI);
        }


        #endregion
    }
}
