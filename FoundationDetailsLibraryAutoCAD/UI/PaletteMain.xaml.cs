using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
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

        private int _boundaryPollAttempts = 0;
        private const int MaxPollAttempts = 25;

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
            // Reset attempts counter
            _boundaryPollAttempts = 0;

            // Fire AutoCAD selection command
            Autodesk.AutoCAD.ApplicationServices.Application
                .DocumentManager.MdiActiveDocument
                .SendStringToExecute("FD_SELECTBOUNDARY ", true, false, false);

            // Start polling with DispatcherTimer
            DispatcherTimer timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };

            timer.Tick += new EventHandler(BoundaryPoll_Tick);
            timer.Start();
        }

        private void BoundaryPoll_Tick(object sender, EventArgs e)
        {
            _boundaryPollAttempts++;
            var timer = (DispatcherTimer)sender;

            try
            {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;

                using (doc.LockDocument())
                {
                    if (PolylineBoundaryManager.TryGetBoundary(out Polyline pl))
                    {
                        int vertexCount = pl.NumberOfVertices;
                        double perimeter = 0;
                        for (int i = 0; i < vertexCount; i++)
                            perimeter += pl.GetPoint2dAt(i).GetDistanceTo(pl.GetPoint2dAt((i + 1) % vertexCount));

                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            TxtBoundaryStatus.Text = "Boundary selected.";
                            TxtBoundaryVertices.Text = vertexCount.ToString();
                            TxtBoundaryPerimeter.Text = perimeter.ToString("F2");
                        }));

                        HighlightAndZoom(pl);

                        timer.Stop();
                        return;
                    }

                    if (_boundaryPollAttempts > MaxPollAttempts)
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                            TxtBoundaryStatus.Text = "No boundary selected."));
                        timer.Stop();
                    }
                }
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                    TxtBoundaryStatus.Text = $"Error: {ex.Message}"));
                timer.Stop();
            }
        }

        private void HighlightAndZoom(Polyline pl)
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            try
            {
                using (doc.LockDocument())
                {
                    ed.SetImpliedSelection(new ObjectId[] { pl.ObjectId });

                    Extents3d ext = pl.GeometricExtents;
                    ZoomToExtents(ed, ext);
                }
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                    TxtBoundaryStatus.Text = $"Zoom/Highlight error: {ex.Message}"));
            }
        }

        private void ZoomToExtents(Editor ed, Extents3d ext)
        {
            try
            {
                if (ext.MinPoint.DistanceTo(ext.MaxPoint) < 1e-6)
                    return;

                var view = ed.GetCurrentView();

                // Convert center to Point2d
                Point2d center = new Point2d(
                    (ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                    (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0
                );

                double width = ext.MaxPoint.X - ext.MinPoint.X;
                double height = ext.MaxPoint.Y - ext.MinPoint.Y;

                if (width < 1e-6) width = 1.0;
                if (height < 1e-6) height = 1.0;

                view.CenterPoint = center;
                view.Width = width * 1.1;
                view.Height = height * 1.1;

                ed.SetCurrentView(view);
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                    TxtBoundaryStatus.Text = $"Zoom error: {ex.Message}"));
            }
        }

        private void RefreshBoundaryInfo(Polyline pl)
        {
            if (pl == null)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    TxtBoundaryStatus.Text = "No boundary selected.";
                    TxtBoundaryVertices.Text = "-";
                    TxtBoundaryPerimeter.Text = "-";
                }));
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                TxtBoundaryStatus.Text = pl.Closed ? "Boundary valid" : "Boundary not closed";
                TxtBoundaryVertices.Text = pl.NumberOfVertices.ToString();

                double perimeter = 0;
                for (int i = 0; i < pl.NumberOfVertices; i++)
                    perimeter += pl.GetPoint2dAt(i).GetDistanceTo(pl.GetPoint2dAt((i + 1) % pl.NumberOfVertices));

                TxtBoundaryPerimeter.Text = perimeter.ToString("F2");
            }));
        }

        private void ZoomToBoundary()
        {
            try
            {
                if (!PolylineBoundaryManager.TryGetBoundary(out Polyline pl))
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                        TxtBoundaryStatus.Text = "No boundary to zoom."));
                    return;
                }

                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                var ed = doc.Editor;

                using (doc.LockDocument())
                using (var tr = doc.TransactionManager.StartTransaction())
                {
                    Extents3d ext = pl.GeometricExtents;
                    ZoomToExtents(ed, ext);
                    tr.Commit();
                }
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                    TxtBoundaryStatus.Text = $"Zoom error: {ex.Message}"));
            }
        }

        #endregion

        #region --- PierControl Handlers ---

        private void OnPierAdded(PierData data)
        {
            Pier pier = PierConverter.ToModelPier(data);
            CurrentModel.Piers.Add(pier);
            Dispatcher.BeginInvoke(new Action(() =>
                TxtStatus.Text = $"Pier added at ({pier.Location.X:F2}, {pier.Location.Y:F2})"));
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
                Dispatcher.BeginInvoke(new Action(() => TxtStatus.Text = "Preview shown."));
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(new Action(() => TxtStatus.Text = $"Preview error: {ex.Message}"));
            }
        }

        private void ClearPreview()
        {
            PreviewManager.ClearPreview();
            Dispatcher.BeginInvoke(new Action(() => TxtStatus.Text = "Preview cleared."));
        }

        private void CommitModel()
        {
            try
            {
                AutoCADAdapter.CommitModelToDrawing(_currentModel);
                Dispatcher.BeginInvoke(new Action(() => TxtStatus.Text = "Model committed to DWG."));
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(new Action(() => TxtStatus.Text = $"Commit error: {ex.Message}"));
            }
        }

        private void SaveModel()
        {
            string filePath = "FoundationProject.json";
            try
            {
                JsonStorage.Save(filePath, _currentModel);
                Dispatcher.BeginInvoke(new Action(() => TxtStatus.Text = $"Model saved to {filePath}"));
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(new Action(() => TxtStatus.Text = $"Save error: {ex.Message}"));
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
                    Dispatcher.BeginInvoke(new Action(() => TxtStatus.Text = "Model loaded."));
                }
                else
                {
                    Dispatcher.BeginInvoke(new Action(() => TxtStatus.Text = "No saved model found."));
                }
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(new Action(() => TxtStatus.Text = $"Load error: {ex.Message}"));
            }
        }

        #endregion
    }
}
