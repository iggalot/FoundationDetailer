using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using FoundationDetailer.AutoCAD;
using FoundationDetailer.Managers;
using FoundationDetailer.UI.Windows;
using FoundationDetailsLibraryAutoCAD.AutoCAD;
using FoundationDetailsLibraryAutoCAD.AutoCAD.NOD;
using FoundationDetailsLibraryAutoCAD.AutoCAD.Testing;
using FoundationDetailsLibraryAutoCAD.Data;
using FoundationDetailsLibraryAutoCAD.Managers;
using FoundationDetailsLibraryAutoCAD.Services;
using FoundationDetailsLibraryAutoCAD.UI.Controls.EqualSpacingGBControl;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using static FoundationDetailsLibraryAutoCAD.UI.Controls.PrelimGBControl.PrelimGradeBeamControl;

namespace FoundationDetailsLibraryAutoCAD.UI
{
    public partial class PaletteMain : UserControl
    {

        private readonly PolylineBoundaryManager _boundaryService = new PolylineBoundaryManager();
        private readonly GradeBeamManager _gradeBeamService = new GradeBeamManager();
        private readonly FoundationPersistenceManager _persistenceService = new FoundationPersistenceManager();

        private FoundationContext CurrentContext => FoundationContext.For(Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument);


        private double ParseDoubleOrDefault(string text, double defaultValue)
        {
            if (double.TryParse(text, out double val))
                return val;
            return defaultValue;
        }


        public PaletteMain()
        {
            InitializeComponent();

            PolylineBoundaryManager.BoundaryChanged += OnBoundaryChanged;  // subscribe for the boundary changed event

            // Initialize PierControl
            //PierUI = new PierControl();
            //PierUI.PierAdded += OnPierAdded;
            //PierUI.RequestPierLocationPick += btnPickPierLocation_Click;

            //PierContainer.Children.Clear();
            //PierContainer.Children.Add(PierUI);

            WireEvents();

            // Initialize NOD for current document
            var context = CurrentContext;
            using (var tr = context.Document.Database.TransactionManager.StartTransaction())
            {
                NODCore.InitFoundationNOD(context, tr);
                tr.Commit();
            }

            _boundaryService.Initialize(context);
            _gradeBeamService.Initialize(context);

            // Load saved NOD
            _persistenceService.Load(context);

            // Initial UI update
            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));

            UpdateTreeViewUI();



            // Optional: auto-refresh on document switch
            Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.DocumentActivated += OnDocumentActivated;

        }

        private void WireEvents()
        {
            var context = CurrentContext;

            BtnQuery.Click += BtnQueryNOD_Click;
            BtnTest.Click += (s, e) => BtnTest_Click();
            BtnEraseNODFully.Click += (s, e) => BtnEraseNODFully_Click();


            BtnSelectBoundary.Click += (s, e) => BtnDefineFoundationBoundary_Click();
            BtnSave.Click += (s, e) => BtnSaveModel_Click();
            BtnLoad.Click += (s, e) => BtnLoadModel_Click();

            BtnShowBoundary.Click += (s, e) => _boundaryService.HighlightBoundary(context);
            BtnZoomBoundary.Click += (s, e) => _boundaryService.ZoomToBoundary(context);

            PrelimGBControl.AddPreliminaryClicked += PrelimGBControl_AddPreliminaryClicked;
            GradeBeamSummary.ClearAllClicked += GradeBeam_ClearAllClicked;
            GradeBeamSummary.HighlightGradeBeamslClicked += GradeBeam_HighlightGradeBeamsClicked;
            GradeBeamSummary.AddSingleGradeBeamClicked += GradeBeamSummary_AddSingleGradeBeamClicked;

            //EqualSpacingGBControl.DrawRequested += OnDrawNewRequested;
            BtnNEqualSpaces.Click += BtnNEqualSpaces_Click;
            BtnConvertExisting.Click += BtnConvertToPolyline_Click;

        }



        private void OnDrawNewRequested(FoundationContext context, SpacingRequest request)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (request == null) throw new ArgumentNullException(nameof(request));

            // Get boundary polyline
            if (!_boundaryService.TryGetBoundary(context, out Polyline boundary))
                return;

            // Get bounding box
            var ext = boundary.GeometricExtents;
            double minX = ext.MinPoint.X;
            double maxX = ext.MaxPoint.X;
            double minY = ext.MinPoint.Y;
            double maxY = ext.MaxPoint.Y;

            Document doc = context.Document;
            Database db = doc.Database;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr =
                    (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                Vector3d span = request.End - request.Start;
                Vector3d dir;

                // Determine direction based on the enum
                switch (request.Direction)
                {
                    case SpacingDirections.Perpendicular:
                        dir = span.GetPerpendicularVector().GetNormal();
                        break;

                    case SpacingDirections.Horizontal:
                        dir = Vector3d.XAxis;
                        break;

                    case SpacingDirections.Vertical:
                        dir = Vector3d.YAxis;
                        break;

                    default:
                        dir = span.GetPerpendicularVector().GetNormal();
                        break;
                }

                // Compute evenly spaced points along the span
                // skip first/last since usually coincide with vertices
                for (int i = 1; i < request.Count; i++)
                {
                    double t = request.Count == 1 ? 0 : (double)i / request.Count;
                    Point3d basePt = request.Start + span * t;

                    // Clip the line to the bounding box
                    if (!MathHelperManager.TryClipLineToBoundingBoxExtents(basePt, dir, ext, out Point3d s, out Point3d e))
                        continue;
                    var start = s;
                    var end = e;

                    //// Clip the line to the bounding box
                    //if (!MathHelperManager.TryClipLineToPolyline(basePt, dir, boundary, out Point3d s1, out Point3d e1))
                    //    continue;
                    //start = s1;
                    //end = e1;


                    // Add the grade beam
                    _gradeBeamService.AddInterpolatedGradeBeam(context, start, end, 5);
                }

                tr.Commit();
            }

            // Refresh the UI asynchronously
            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
        }

        private void OnDrawExistingRequested(FoundationContext context, Polyline existing_pl)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (existing_pl == null) throw new ArgumentNullException(nameof(existing_pl));

            // Get boundary polyline
            if (!_boundaryService.TryGetBoundary(context, out Polyline boundary))
                return;

            // Get bounding box
            var ext = boundary.GeometricExtents;
            double minX = ext.MinPoint.X;
            double maxX = ext.MaxPoint.X;
            double minY = ext.MinPoint.Y;
            double maxY = ext.MaxPoint.Y;

            Document doc = context.Document;
            Database db = doc.Database;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // This is the real ObjectId in the drawing database
                ObjectId selectedId = existing_pl.ObjectId;
                //Editor ed = context.Document.Editor;
                //ed.WriteMessage($"\nObjectId of existing_pl: {existing_pl.ObjectId}");
                //ed.WriteMessage($"\nType: {existing_pl.GetType().Name}");
                //MessageBox.Show($"\nObjectId of existing_pl: {existing_pl.ObjectId}");
                //MessageBox.Show($"\nType: {existing_pl.GetType().Name}");
                // Add the grade beam
                _gradeBeamService.AddExistingAsGradeBeam(context, selectedId, tr);

                tr.Commit();
            }

            // Refresh the UI asynchronously
            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
        }

        #region --- UI Updates ---
        private void PrelimGBControl_AddPreliminaryClicked(object sender, PrelimGBEventArgs e)
        {
            var context = CurrentContext;
            if (context == null) return;

            if (!_boundaryService.TryGetBoundary(context, out Polyline boundary))
            {
                TxtStatus.Text = "No boundary selected.";
                return;
            }

            try
            {
                var beams = _gradeBeamService.CreatePreliminaryGradeBeamLayout(
                    context,
                    boundary,
                    e.HorzMin,
                    e.HorzMax,
                    e.VertMin,
                    e.VertMax,
                    vertexCount: 5
                );

                TxtStatus.Text = $"Created {beams.Count} preliminary grade beams.";
                PrelimGBControl.ViewModel.IsPreliminaryGenerated = true;
                PrelimGBControl.Visibility = System.Windows.Visibility.Collapsed;

                Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Error creating grade beams: {ex.Message}";
            }
        }

        private void GradeBeam_ClearAllClicked(object sender, EventArgs e)
        {
            var context = CurrentContext;
            var doc = context.Document;
            _gradeBeamService.ClearAllGradeBeams(context);
            PrelimGBControl.ViewModel.IsPreliminaryGenerated = false;  // reset the the preliminary input control

            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
        }

        private void GradeBeam_HighlightGradeBeamsClicked(object sender, EventArgs e)
        {
            var context = CurrentContext;
            _gradeBeamService.HighlightGradeBeams(context);
        }

        private void GradeBeamSummary_AddSingleGradeBeamClicked(object sender, EventArgs e)
        {
            var context = CurrentContext;
            if (context == null)
            {
                TxtStatus.Text = "No active document.";
                return;
            }

            var doc = context.Document;
            if (doc == null) return;

            try
            {
                var ed = doc.Editor;

                // --- Prompt user for first point ---
                var firstPointRes = ed.GetPoint("\nSelect first point for grade beam:");
                if (firstPointRes.Status != Autodesk.AutoCAD.EditorInput.PromptStatus.OK)
                {
                    TxtStatus.Text = "First point selection canceled.";
                    return;
                }

                Point3d pt1 = firstPointRes.Value;

                // --- Use jig to get second point (preview) ---
                var jig = new GradeBeamPolylineJig(pt1);
                var res = ed.Drag(jig);
                if (res.Status != Autodesk.AutoCAD.EditorInput.PromptStatus.OK)
                {
                    TxtStatus.Text = "Second point selection canceled.";
                    return;
                }

                Point3d pt2 = jig.Polyline.GetPoint3dAt(1);

                // --- Validate points ---
                if (pt1.IsEqualTo(pt2))
                {
                    TxtStatus.Text = "Points cannot be the same.";
                    return;
                }

                // --- Create Polyline vertices and Polyline ---
                List<Point2d> verts = new List<Point2d>
        {
                    new Point2d(pt1.X, pt1.Y),
                    new Point2d(pt2.X, pt2.Y)
        };

                verts = PolylineConversionService.EnsureMinimumVertices(verts, 5);

                Polyline newPl = PolylineConversionService.CreatePolylineFromVertices(verts);

                // --- Append to ModelSpace and register as GradeBeam ---
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    _gradeBeamService.RegisterGradeBeam(context, newPl, tr, appendToModelSpace: true);
                    tr.Commit();
                }

                // --- Refresh UI ---
                Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));

                TxtStatus.Text = "Custom grade beam added.";
                PrelimGBControl.ViewModel.IsPreliminaryGenerated = true;

                // Hide the gradebeam control
                PrelimGBControl.Visibility = System.Windows.Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Error adding grade beam: {ex.Message}";
            }
        }

        private void UpdateBoundaryDisplay()
        {
            var context = CurrentContext ?? throw new Exception("Un UpdateBoundaryDCurrent context is null.");

            bool isValid = false;

            if (_boundaryService.TryGetBoundary(context, out Polyline pl) && pl.Closed)
            {
                isValid = true;
                TxtBoundaryStatus.Text = "Boundary valid - " + pl.ObjectId.Handle.ToString();
                TxtBoundaryVertices.Text = pl.NumberOfVertices.ToString();

                double perimeter = 0;
                for (int i = 0; i < pl.NumberOfVertices; i++)
                    perimeter += pl.GetPoint2dAt(i).GetDistanceTo(pl.GetPoint2dAt((i + 1) % pl.NumberOfVertices));
                TxtBoundaryPerimeter.Text = perimeter.ToString("F2");

                TxtBoundaryArea.Text = MathHelperManager.ComputePolylineArea(pl).ToString("F2");

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

            // Update the tree Viewer
            UpdateTreeViewUI();

            // Update the gradebeam summary
            RefreshGradeBeamSummary();

            // Hide / show the preliminary button if there are no grade beams
            PrelimGBControl.Visibility = _gradeBeamService.HasAnyGradeBeams(CurrentContext)
                                         ? System.Windows.Visibility.Collapsed
                                         : System.Windows.Visibility.Visible;
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

        private void TreeViewExtensionData_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (!(e.NewValue is TreeViewItem tvi))
                return;

            // TreeNodeInfo holds the NODObjectWrapper
            if (!(tvi.Tag is TreeViewManager.TreeNodeInfo nodeInfo))
                return;

            Entity ent = nodeInfo.NODObject?.Entity;
            if (ent == null)
                return;

            var context = CurrentContext;
            var doc = context.Document;
            var ed = doc.Editor;

            using (doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                try
                {
                    // Select the entity in the AutoCAD drawing
                    ed.SetImpliedSelection(new ObjectId[] { ent.ObjectId });
                    ed.UpdateScreen();

                    tr.Commit();
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    ed.WriteMessage($"\nError selecting entity: {ex.Message}");
                }
            }
        }



        #endregion

        #region --- NOD / TreeView ---


        /// <summary>
        /// Helper function to update the data in the TreeView for the HANDLES from the NOD.
        /// </summary>
        internal void UpdateTreeViewUI()
        {
            var context = CurrentContext;
            var doc = context?.Document;
            if (doc == null) return;

            var db = doc.Database;

            TreeViewExtensionData.Items.Clear();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var rootDict = NODCore.GetFoundationRoot(tr, db);
                if (rootDict == null)
                    return;

                // --- Rebuild the ExtensionDataItem tree ---
                var treeData = NODTraversal.BuildTree(context, tr, rootDict, db);

                // --- Pass the transaction into TreeViewManager ---
                var treeMgr = new TreeViewManager(tr);

                // --- Populate TreeView ---
                treeMgr.PopulateFromData(TreeViewExtensionData, treeData);

                tr.Commit();
            }
        }






        #endregion


        #region --- UI Button Click Handlers ---
        private void BtnDefineFoundationBoundary_Click()
        {
            var context = CurrentContext;
            if (_boundaryService.SelectBoundary(context, out string error))
                Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
            else if (!string.IsNullOrEmpty(error))
                TxtStatus.Text = error;
        }

        private void BtnEraseNODFully_Click()
        {
            NODCleaner.ClearFoundationNOD(CurrentContext);
        }

        private void BtnTest_Click()
        {
            FoundationTestTools.TestGradeBeamNOD(CurrentContext);

            //FoundationTestTools.TestDumpGradeBeamNod(CurrentContext);
            FoundationTestTools.TestGradeBeamJsonRoundTrip(CurrentContext);
        }

        /// <summary>
        /// Queries the NOD for a list of handles in each subdirectory.
        /// </summary>
        private void BtnQueryNOD_Click(object sender, RoutedEventArgs e)
        {
            var context = CurrentContext;
            if (context == null)
                return;

            var doc = context.Document;
            if (doc == null)
                return;

            var db = doc.Database;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var rootDict = NODCore.GetFoundationRoot(tr, db);
                if (rootDict == null)
                {
                    ScrollableMessageBox.Show("No EE_Foundation dictionary found.");
                    return;
                }

                // --- Build structured NOD tree ---
                var treeData = NODTraversal.BuildTree(context, tr, rootDict, db);

                // --- Pass the transaction into the TreeViewManager ---
                var treeMgr = new TreeViewManager(tr);

                // --- Update TreeView UI ---
                treeMgr.PopulateFromData(TreeViewExtensionData, treeData);

                // --- Convert the tree to a string ---
                string treeString = NODTraversal.TreeToString(treeData);

                // --- Show in a MessageBox ---
                ScrollableMessageBox.Show(treeString, "NOD Tree Structure");

                tr.Commit();
            }
        }




        // ---------------------------
        // Button click handler
        // ---------------------------
        private void BtnConvertToPolyline_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var doc = CurrentContext?.Document;
            if (doc == null) return;

            var ed = doc.Editor;
            var db = doc.Database;

            // --------------------------------------------
            // Prompt for Line or Polyline
            // --------------------------------------------
            var peo = new PromptEntityOptions("\nSelect a Line or Polyline to convert:");
            peo.SetRejectMessage("\nOnly Line or Polyline objects are allowed.");
            peo.AddAllowedClass(typeof(Line), true);
            peo.AddAllowedClass(typeof(Polyline), true);

            var per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
                return;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    string handle = per.ObjectId.Handle.ToString();

                    // --------------------------------------------
                    // CHECK: already a GradeBeam?
                    // --------------------------------------------
                    // Open the NamedObjectsDictionary as a DBDictionary
                    var nod = tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead) as DBDictionary;

                    // Check if the handle exists anywhere in the NOD
                    bool alreadyInTree = nod != null && NODScanner.ContainsHandle(
                        CurrentContext,
                        tr,
                        nod,
                        handle);

                    if (alreadyInTree)
                    {
                        ed.WriteMessage(
                            "\nSelected object is already registered as a GradeBeam.");
                        TxtStatus.Text = "Object is already a GradeBeam.";
                        return;
                    }

                    // --------------------------------------------
                    // Convert to GradeBeam Polyline
                    // --------------------------------------------
                    const int minVertexCount = 5;
                    _gradeBeamService.ConvertToGradeBeam(
                        CurrentContext,
                        per.ObjectId,
                        minVertexCount,
                        tr);

                    tr.Commit();

                    // --------------------------------------------
                    // UI refresh
                    // --------------------------------------------
                    Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
                    TxtStatus.Text = "Selected object converted to GradeBeam Polyline.";
                }
                catch (Exception ex)
                {
                    ed.WriteMessage($"\nError converting to GradeBeam: {ex.Message}");
                    TxtStatus.Text = $"Error: {ex.Message}";
                }
            }
        }

        private void BtnNEqualSpaces_Click(object sender, RoutedEventArgs e)
        {
            var doc = CurrentContext.Document;
            var ed = doc.Editor;

            // get the interpolated points.
            (Point3d? start, Point3d? end) = AutoCADEditorPromptService.PromptForSpacingPoints(CurrentContext);
            if (start == null || end == null)
            {
                ed.WriteMessage("No points selected.");
                return;
            }
            var spaces = AutoCADEditorPromptService.PromptForEqualSpacingCount(CurrentContext);
            if (spaces <= 1)
            {
                ed.WriteMessage("At least 2 spaces are required.");
                return;
            }
            var dir = AutoCADEditorPromptService.PromptForSpacingDirection(CurrentContext);
            if (dir == null)
            {
                ed.WriteMessage("No direction selected.");
                return;
            }

            Vector3d vec = end.Value - start.Value;
            var length = vec.Length;


            OnDrawNewRequested(CurrentContext, new SpacingRequest()
            {
                Count = spaces.Value,
                Direction = dir.Value,
                MaxSpa = (Math.Abs(length) / (spaces.Value - 1)),
                MinSpa = (Math.Abs(length) / (spaces.Value - 1)),
                Start = start.Value,
                End = end.Value

            });
        }

        private void BtnSaveModel_Click()
        {
            var context = CurrentContext;
            _persistenceService.Save(context);
        }
        private void BtnLoadModel_Click()
        {
            var context = CurrentContext;
            _persistenceService.Load(context);
            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
        }

        #endregion


        #region --- UI Events Handlers ---

        private void OnBoundaryChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
        }

        private void OnDocumentActivated(object sender, DocumentCollectionEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateBoundaryDisplay();
                UpdateTreeViewUI();
                TxtStatus.Text = $"Active document: {e.Document.Name}";
            }));
        }

        private void RefreshGradeBeamSummary()
        {
            var context = CurrentContext;

            int quantity;
            double total_length;
            (quantity, total_length) = _gradeBeamService.GetGradeBeamSummary(context);

            GradeBeamSummary.UpdateSummary(quantity, total_length);
        }

        #endregion
    }
}
