using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using FoundationDetailer.AutoCAD;
using FoundationDetailer.Model;
using FoundationDetailer.Storage;
using FoundationDetailer.UI.Controls;
using FoundationDetailer.UI.Converters;
using FoundationDetailer.Utilities;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

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

            // Initialize PierControl
            PierUI = new PierControl();
            PierUI.PierAdded += OnPierAdded;
            PierUI.RequestPierLocationPick += PickPierLocation;

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

            BtnShowBoundary.Click += (s, e) => PolylineBoundaryManager.HighlightBoundary();
            BtnZoomBoundary.Click += (s, e) => ZoomToBoundary();
        }

        #region --- Boundary Selection ---

        private void SelectBoundary()
        {
            Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.SendStringToExecute(
                "FD_SELECTBOUNDARY ", true, false, false);

            // Poll every 200ms for up to 5 seconds
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(200);
            int attempts = 0;
            timer.Tick += (s, e) =>
            {
                attempts++;
                if (PolylineBoundaryManager.TryGetBoundary(out _))
                {
                    RefreshBoundaryInfo();
                    timer.Stop();
                }
                else if (attempts > 25) // timeout ~5 seconds
                {
                    timer.Stop();
                    TxtBoundaryStatus.Text = "No boundary selected.";
                }
            };
            timer.Start();
        }

        private void RefreshBoundaryInfo()
        {
            if (!PolylineBoundaryManager.TryGetBoundary(out Polyline pl))
            {
                TxtBoundaryStatus.Text = "No valid boundary.";
                TxtBoundaryVertices.Text = "-";
                TxtBoundaryPerimeter.Text = "-";
                return;
            }

            TxtBoundaryStatus.Text = "Boundary loaded.";
            TxtBoundaryVertices.Text = pl.NumberOfVertices.ToString();

            double perimeter = 0;
            for (int i = 0; i < pl.NumberOfVertices; i++)
            {
                Point2d a = pl.GetPoint2dAt(i);
                Point2d b = pl.GetPoint2dAt((i + 1) % pl.NumberOfVertices);
                perimeter += a.GetDistanceTo(b);
            }
            TxtBoundaryPerimeter.Text = $"{perimeter:F2}";
        }

        private void ZoomToBoundary()
        {
            if (!PolylineBoundaryManager.TryGetBoundary(out Polyline pl)) return;

            ZoomToExtents(pl);
        }

        /// <summary>
        /// Zooms the AutoCAD editor to the extents of the given polyline.
        /// </summary>
        private void ZoomToExtents(Polyline pl)
        {
            if (pl == null || pl.IsErased || pl.NumberOfVertices == 0)
                return;

            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            try
            {
                Extents3d ext = pl.GeometricExtents;

                // Compute center in 2D (X, Y)
                Point2d center2d = new Point2d(
                    (ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                    (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0);

                // Get current view
                var view = ed.GetCurrentView();

                // Set center and size
                view.CenterPoint = center2d;
                double margin = 1.1;
                view.Height = (ext.MaxPoint.Y - ext.MinPoint.Y) * margin;
                view.Width = (ext.MaxPoint.X - ext.MinPoint.X) * margin;

                ed.SetCurrentView(view);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                TxtStatus.Text = $"Zoom error: {ex.Message}";
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
        }

        #endregion

        #region --- Model Operations ---

        private void AddPiers() => MessageBox.Show("Add piers to model.");
        private void AddGradeBeams() => MessageBox.Show("Add grade beams to model.");
        private void AddRebarBars() => MessageBox.Show("Add rebar bars to model.");
        private void AddStrands() => MessageBox.Show("Add strands to model.");

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
            string filePath = "FoundationProject.json";
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
            string filePath = "FoundationProject.json";
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
    }
}
