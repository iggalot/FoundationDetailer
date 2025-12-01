using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using FoundationDetailer.AutoCAD;
using FoundationDetailer.Managers;
using FoundationDetailer.Model;
using FoundationDetailer.Storage;
using FoundationDetailer.UI.Controls;
using FoundationDetailer.UI.Converters;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
            FoundationBoundaryManager.BoundaryChanged += OnBoundaryChanged;  // subscribe for the boundary changed event

            // Initialize PierControl
            //PierUI = new PierControl();
            //PierUI.PierAdded += OnPierAdded;
            //PierUI.RequestPierLocationPick += PickPierLocation;

            //PierContainer.Children.Clear();
            //PierContainer.Children.Add(PierUI);

            WireEvents();

            // Initialize boundary display immediately
            LoadBoundaryForActiveDocument();
        }

        private void WireEvents()
        {

            BtnSelectBoundary.Click += (s, e) => SelectBoundary();
            //BtnAddPiers.Click += (s, e) => AddPiers();
            BtnAddGradeBeams.Click += (s, e) => AddGradeBeams();
            BtnAddRebar.Click += (s, e) => AddRebarBars();
            BtnAddStrands.Click += (s, e) => AddStrands();
            //BtnPreview.Click += (s, e) => ShowPreview();
            //BtnClearPreview.Click += (s, e) => ClearPreview();
            //BtnCommit.Click += (s, e) => CommitModel();
            //BtnSave.Click += (s, e) => SaveModel();
            //BtnLoad.Click += (s, e) => LoadModel();

            BtnShowBoundary.Click += (s, e) => FoundationBoundaryManager.HighlightBoundary();
            BtnZoomBoundary.Click += (s, e) => FoundationBoundaryManager.ZoomToBoundary();
        }

        #region --- Boundary Selection and UI Updates ---

        private void LoadBoundaryForActiveDocument()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            try
            {
                //using (doc.LockDocument())
                using (var tr = doc.TransactionManager.StartTransaction())
                {
                    // Immediately update boundary display for the active document
                    UpdateBoundaryDisplay();
                    tr.Commit();
                }
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                    TxtStatus.Text = $"Error loading boundary: {ex.Message}"));
            }
        }

        private void OnBoundaryChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
        }

        private void UpdateBoundaryDisplay()
        {
            bool isValid = false;

            if (FoundationBoundaryManager.TryGetBoundary(out Polyline pl) && pl.Closed)
            {
                isValid = true;
                TxtBoundaryStatus.Text = "Boundary valid - "+ pl.ObjectId.Handle.ToString();
                TxtBoundaryVertices.Text = pl.NumberOfVertices.ToString();

                double perimeter = 0;
                for (int i = 0; i < pl.NumberOfVertices; i++)
                    perimeter += pl.GetPoint2dAt(i).GetDistanceTo(pl.GetPoint2dAt((i + 1) % pl.NumberOfVertices));
                TxtBoundaryPerimeter.Text = perimeter.ToString("F2");

                double area = ComputePolylineArea(pl);
                TxtBoundaryArea.Text = area.ToString("F2");

                BtnZoomBoundary.IsEnabled = true;
                BtnShowBoundary.IsEnabled = true;
            }
            else
            {
                TxtBoundaryStatus.Text = "No boundary selected";
                TxtBoundaryVertices.Text = "-";
                TxtBoundaryPerimeter.Text = "-";

                BtnZoomBoundary.IsEnabled = false;
                BtnShowBoundary.IsEnabled = false;
            }

            // Update status circle
            StatusCircle.Fill = isValid ? Brushes.Green : Brushes.Red;

            // Show/hide action buttons
            ActionButtonsPanel.Visibility = isValid ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

            // Optionally, change background color of action buttons
            foreach (Button btn in ActionButtonsPanel.Children)
            {
                btn.Background = isValid ? Brushes.LightGreen : Brushes.LightCoral;
            }
        }

        public static double ComputePolylineArea(Polyline pl)
        {
            if (pl == null || pl.NumberOfVertices < 3)
                return 0.0;

            double area = 0.0;

            for (int i = 0; i < pl.NumberOfVertices; i++)
            {
                Point2d p1 = pl.GetPoint2dAt(i);
                Point2d p2 = pl.GetPoint2dAt((i + 1) % pl.NumberOfVertices);
                area += (p1.X * p2.Y) - (p2.X * p1.Y);
            }

            return Math.Abs(area / 2.0);
        }

        private void SelectBoundary()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var ed = doc.Editor;

            // Prompt for a closed polyline
            PromptEntityOptions options = new PromptEntityOptions("\nSelect a closed polyline: ");
            options.SetRejectMessage("\nMust be a closed polyline.");
            options.AddAllowedClass(typeof(Polyline), false);

            var result = ed.GetEntity(options);
            if (result.Status != PromptStatus.OK) return;

            // Try set the boundary
            if (!FoundationBoundaryManager.TrySetBoundary(result.ObjectId, out string error))
            {
                ed.WriteMessage($"\nError setting boundary: {error}");
            }
            else
            {
                ed.WriteMessage($"\nBoundary selected: {result.ObjectId.Handle}");
            }
        }

        #endregion

        #region --- Highlight and Zoom ---

        private void ZoomToExtents(Editor ed, Extents3d ext)
        {
            try
            {
                if (ext.MinPoint.DistanceTo(ext.MaxPoint) < 1e-6)
                    return;

                var view = ed.GetCurrentView();

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

private void AddGradeBeams()
{
    // Get stored boundary
    if (!FoundationBoundaryManager.TryGetBoundary(out Polyline boundaryRecord))
    {
        MessageBox.Show("No boundary selected.");
        return;
    }

    var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
    var db = doc.Database;

    // Palette already runs inside AutoCAD's main document context
    using (Transaction tr = db.TransactionManager.StartTransaction())
    {
        // Open boundary for read
        Polyline pl = tr.GetObject(boundaryRecord.ObjectId, OpenMode.ForRead) as Polyline;
        if (pl == null)
        {
            MessageBox.Show("Boundary polyline was not found.");
            return;
        }

        // Clone (safe, optional)
        //Polyline workPl = (Polyline)pl.Clone();

        // Build grid
        double maxSpacing = 10.0;
        GridLineManager grid = new GridLineManager(pl, maxSpacing, 5);

        var verticalLines = grid.GetVerticalPolylines();
        var horizontalLines = grid.GetHorizontalPolylines();

        //BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

        //foreach (var ln in verticalLines)
        //{
        //    btr.AppendEntity(ln);
        //    tr.AddNewlyCreatedDBObject(ln, true);
        //}

        //foreach (var ln in horizontalLines)
        //{
        //    btr.AppendEntity(ln);
        //    tr.AddNewlyCreatedDBObject(ln, true);
        //}

        tr.Commit();
    }
}





        private void AddPiers() => MessageBox.Show("Add piers to model.");

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
