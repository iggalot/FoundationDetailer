using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using FoundationDetailer.AutoCAD;
using FoundationDetailer.Managers;
using FoundationDetailer.Model;
using FoundationDetailer.UI.Controls;
using FoundationDetailer.UI.Converters;
using FoundationDetailsLibraryAutoCAD.AutoCAD;
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

            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // Create the QueryNOD dictionaries
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                NODManager.InitFoundationNOD(tr);  // initialize the NOD for our application
                tr.Commit();
            }

            PolylineBoundaryManager.BoundaryChanged += OnBoundaryChanged;  // subscribe for the boundary changed event

            // Initialize PierControl
            //PierUI = new PierControl();
            //PierUI.PierAdded += OnPierAdded;
            //PierUI.RequestPierLocationPick += PickPierLocation;

            //PierContainer.Children.Clear();
            //PierContainer.Children.Add(PierUI);

            WireEvents();

            // Load the saved NOD (if available)
            NODManager.ImportFoundationNOD();
        }

        private void WireEvents()
        {
            BtnQuery.Click += (s, e) => QueryNOD();
            BtnSyncNod.Click += (s, e) => SyncNodData();

            BtnSelectBoundary.Click += (s, e) => SelectBoundary(); // for selecting the boundary

            BtnAddGradeBeams.Click += (s, e) => AddPreliminaryGradeBeams(); // for adding a preliminary gradebeam layout
            
            //BtnAddRebar.Click += (s, e) => AddRebarBars();
            //BtnAddStrands.Click += (s, e) => AddStrands();
            //BtnAddPiers.Click += (s, e) => AddPiers();

            //BtnPreview.Click += (s, e) => ShowPreview();
            //BtnClearPreview.Click += (s, e) => ClearPreview();
            //BtnCommit.Click += (s, e) => CommitModel();
            BtnSave.Click += (s, e) => SaveModel();
            BtnLoad.Click += (s, e) => LoadModel();

            BtnShowBoundary.Click += (s, e) => PolylineBoundaryManager.HighlightBoundary();
            BtnZoomBoundary.Click += (s, e) => PolylineBoundaryManager.ZoomToBoundary();

            BtnHighlightGradeBeams.Click += (s, e) =>
            {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                GradeBeamManager.HighlightGradeBeams(doc);
            };
            BtnClearGradeBeams.Click += BtnClearGradeBeams_Click;

        }

        /// <summary>
        /// Queries the NOD for a list of handles in each subdirectory.
        /// </summary>
        private void QueryNOD()
        {
            NODManager.ViewFoundationNOD();
        }

        /// <summary>
        /// Cleans the NOD of any stake handles
        /// </summary>
        private void SyncNodData()
        {
            NODManager.CleanFoundationNOD();
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

            if (PolylineBoundaryManager.TryGetBoundary(out Polyline pl) && pl.Closed)
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
                TxtBoundaryArea.Text = "-";


                BtnZoomBoundary.IsEnabled = false;
                BtnShowBoundary.IsEnabled = false;
            }

            // Update status circle
            StatusCircle.Fill = isValid ? Brushes.Green : Brushes.Red;

            // Show/hide action buttons
            ActionButtonsPanel.Visibility = isValid ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

            // Optionally, change background color of action buttons
            SetActionButtonBackgrounds(ActionButtonsPanel, isValid ? Brushes.LightGreen : Brushes.LightCoral);
        }

        private void SetActionButtonBackgrounds(Panel parent, Brush background)
        {
            foreach (var child in parent.Children)
            {
                if (child is Button btn)
                {
                    btn.Background = background;
                }
                else if (child is Panel panel)
                {
                    // Recursive call for nested panels
                    SetActionButtonBackgrounds(panel, background);
                }
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
            if (!PolylineBoundaryManager.TrySetBoundary(result.ObjectId, out string error))
            {
                ed.WriteMessage($"\nError setting boundary: {error}");
            }
            else
            {
                NODManager.AddBoundaryHandleToNOD(result.ObjectId);  // store the boundary handle in the NOD
                ed.WriteMessage($"\nBoundary selected: {result.ObjectId.Handle}");
            }
        }

        private void BtnClearGradeBeams_Click(object sender, RoutedEventArgs e)
        {
            NODManager.EraseFoundationSubDictionary("FD_GRADEBEAM");
            TxtStatus.Text = "All grade beams cleared.";
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

        private void AddPreliminaryGradeBeams()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            if (!PolylineBoundaryManager.TryGetBoundary(out Polyline boundary))
            {
                doc.Editor.WriteMessage("\nNo boundary selected.");
                return;
            }

            double maxSpacing = 144.0;
            int vertexCount = 5;

            try
            {
                using (doc.LockDocument())
                {
                    // Let GradeBeamManager handle everything internally
                    GradeBeamManager.CreateBothGridlines(boundary, maxSpacing, vertexCount);

                    doc.Editor.WriteMessage("\nGrade beams created successfully.");
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                doc.Editor.WriteMessage($"\nError creating grade beams: {ex.Message}");
            }
        }

        private void AddRebarBars() => MessageBox.Show("Add rebar bars to model.");
        private void AddStrands() => MessageBox.Show("Add strands to model.");


        private void SaveModel()
        {
            NODManager.ExportFoundationNOD();
        }

        private void LoadModel()
        {
            NODManager.ImportFoundationNOD();
        }

        #endregion
    }
}
