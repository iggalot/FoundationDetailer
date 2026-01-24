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
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
            BtnGenerateGradeBeamEdges.Click += (s, e) => BtnGenerateGradeBeamEdges_Click(s, e);
            BtnDeleteSingleGradeBeamFromSelect.Click += (s, e) => BtnDeleteSingleFromSelect_Click(s, e);
            BtnDeleteMultipleGradeBeamFromSelect.Click += (s, e) => BtnDeleteMultipleFromSelect_Click(s, e);
            BtnRegenerateAll.Click += (s, e) => BtnRegenerateAll_Click(s, e);




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

        private void BtnDeleteSingleFromSelect_Click(object s, RoutedEventArgs e)
        {
            var context = CurrentContext;
            if (context?.Document == null)
                return;

            var doc = context.Document;
            var db = doc.Database;
            var ed = doc.Editor;

            // --------------------------------------------------
            // 1) Prompt user to select any grade beam object
            // --------------------------------------------------
            var peo = new PromptEntityOptions("\nSelect any object of a grade beam to delete:");
            var pres = ed.GetEntity(peo);

            if (pres.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n[DEBUG] Operation cancelled at selection step.");
                return;
            }

            ObjectId selectedId = pres.ObjectId;
            ed.WriteMessage($"\n[DEBUG] Selected object handle: {selectedId.Handle}");

            string gradeBeamHandle;
            bool isCenterline;
            bool isEdge;

            // --------------------------------------------------
            // 2) Resolve owning grade beam via NOD helper
            // --------------------------------------------------
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (!GradeBeamNOD.TryResolveOwningGradeBeam(
                        context,
                        tr,
                        selectedId,
                        out gradeBeamHandle,
                        out isCenterline,
                        out isEdge))
                {
                    ed.WriteMessage(
                        "\n[DEBUG] Selected object does not belong to any grade beam.");
                    return;
                }

                ed.WriteMessage(
                    $"\n[DEBUG] Object belongs to grade beam '{gradeBeamHandle}' " +
                    $"({(isCenterline ? "centerline" : "edge")}).");

                // --------------------------------------------------
                // 3) Confirm deletion
                // --------------------------------------------------
                var pko = new PromptKeywordOptions(
                    $"\nDelete grade beam '{gradeBeamHandle}' and rebuild remaining edges?")
                {
                    AllowNone = false
                };
                pko.Keywords.Add("Yes");
                pko.Keywords.Add("No");

                var presConfirm = ed.GetKeywords(pko);
                if (presConfirm.Status != PromptStatus.OK ||
                    presConfirm.StringResult != "Yes")
                {
                    ed.WriteMessage("\n[DEBUG] Deletion cancelled by user.");
                    return;
                }

                // --------------------------------------------------
                // 4) Delete selected grade beam (centerline + edges + NOD)
                // --------------------------------------------------
                int deletedCount =
                    _gradeBeamService.DeleteGradeBeamRecursiveByHandle(
                        context,
                        gradeBeamHandle);

                ed.WriteMessage(
                    $"\n[DEBUG] Deleted {deletedCount} entities for grade beam '{gradeBeamHandle}'.");

                // --------------------------------------------------
                // 5) Delete ALL remaining edges (global cleanup)
                // --------------------------------------------------
                int edgesDeleted =
                    GradeBeamManager.DeleteAllGradeBeamEdges(context);

                ed.WriteMessage(
                    $"\n[DEBUG] Deleted {edgesDeleted} remaining grade beam edges.");

                tr.Commit();
            }

            // --------------------------------------------------
            // 6) Rebuild edges for remaining grade beams
            // --------------------------------------------------
            _gradeBeamService.GenerateEdgesForAllGradeBeams(context);

            ed.WriteMessage("\n[DEBUG] Rebuilt grade beam edges.");
        }

        private void BtnDeleteMultipleFromSelect_Click(object s, RoutedEventArgs e)
        {
            var context = CurrentContext;
            if (context?.Document == null)
                return;

            var doc = context.Document;
            var db = doc.Database;
            var ed = doc.Editor;

            // --- Multi-select grade beam objects
            var pso = new PromptSelectionOptions
            {
                MessageForAdding = "\nSelect grade beam objects to delete:"
            };

            var psr = ed.GetSelection(pso);
            if (psr.Status != PromptStatus.OK)
                return;

            var selectedHandles = new HashSet<string>();

            // --- Resolve owning grade beams
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in psr.Value.GetObjectIds())
                {
                    if (GradeBeamNOD.TryResolveOwningGradeBeam(
                        context, tr, id, out string gbHandle, out bool isCenterline, out bool isEdge))
                    {
                        selectedHandles.Add(gbHandle);
                        ed.WriteMessage($"\n[DEBUG] Object {id.Handle} -> beam {gbHandle} (Centerline: {isCenterline}, Edge: {isEdge})");
                    }
                }

                tr.Commit();
            }

            if (selectedHandles.Count == 0)
            {
                ed.WriteMessage("\n[DEBUG] No selected objects belong to grade beams.");
                return;
            }

            // --- Confirm deletion
            var pko = new PromptKeywordOptions($"\nDelete {selectedHandles.Count} grade beam(s) and rebuild all edges?")
            {
                AllowNone = false
            };
            pko.Keywords.Add("Yes");
            pko.Keywords.Add("No");

            var confirm = ed.GetKeywords(pko);
            if (confirm.Status != PromptStatus.OK || confirm.StringResult != "Yes")
                return;

            int totalDeleted = 0;

            List<string> remainingGBKeys = new List<string>();

            // --- Single transaction to delete selected grade beams and gather remaining keys
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // --- Access NOD dictionaries
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                if (!nod.Contains(NODCore.ROOT))
                    return;

                var root = (DBDictionary)tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForRead);
                if (!root.Contains(NODCore.KEY_GRADEBEAM_SUBDICT))
                    return;

                var gbRoot = (DBDictionary)tr.GetObject(root.GetAt(NODCore.KEY_GRADEBEAM_SUBDICT), OpenMode.ForWrite);

                // --- Enumerate all grade beams
                foreach (DBDictionaryEntry entry in gbRoot)
                    remainingGBKeys.Add(entry.Key);

                // --- Delete selected grade beams completely
                foreach (var key in selectedHandles)
                {
                    if (remainingGBKeys.Contains(key))
                    {
                        totalDeleted += _gradeBeamService.DeleteGradeBeamInternal(context, tr, gbRoot, key);
                        remainingGBKeys.Remove(key); // remove from remaining list
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage($"\n[DEBUG] Deleted {totalDeleted} entities from selected grade beams.");

            int edgesDeleted = 0;

            // --- Delete edges only for remaining grade beams
            foreach (var key in remainingGBKeys)
            {
                edgesDeleted += GradeBeamManager.DeleteGradeBeamEdgesOnly(context, key);
            }

            ed.WriteMessage($"\n[DEBUG] Deleted {edgesDeleted} edge entities from remaining grade beams.");

            // --- Rebuild edges for all grade beams
            _gradeBeamService.GenerateEdgesForAllGradeBeams(context);

            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
        }

        private void BtnRegenerateAll_Click(object sender, RoutedEventArgs e)
        {
            var context = CurrentContext;
            if (context?.Document == null)
                return;

            var doc = context.Document;
            var ed = doc.Editor;

            using (doc.LockDocument())
            {
                ed.WriteMessage("\n[DEBUG] Deleting all grade beam edges...");

                // --- Delete edges only for all grade beams
                int totalEdgesDeleted = GradeBeamManager.DeleteAllGradeBeamEdges(context);
                ed.WriteMessage($"\n[DEBUG] Deleted {totalEdgesDeleted} grade beam edges.");

                // --- Rebuild edges for all grade beams
                _gradeBeamService.GenerateEdgesForAllGradeBeams(context);
                ed.WriteMessage("\n[DEBUG] Regenerated all grade beam edges.");
            }

            // --- Refresh boundary display
            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
        }



        private void BtnGenerateGradeBeamEdges_Click(object sender, RoutedEventArgs e)
        {
            var context = CurrentContext;
            if (context?.Document == null) return;

            _gradeBeamService.GenerateEdgesForAllGradeBeams(context);
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

            // Delete all grade beams entirely: edges + centerlines + NOD dictionary
            using (doc.LockDocument())
            {
                _gradeBeamService.DeleteAllGradeBeams(context);
            }

            // Reset preliminary input control
            PrelimGBControl.ViewModel.IsPreliminaryGenerated = false;

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
            if (context == null || context.Document == null)
            {
                TxtStatus.Text = "No active document.";
                return;
            }

            var doc = context.Document;
            var ed = doc.Editor;
            var db = doc.Database;

            try
            {
                var preview = new GradeBeamMultiVertexPreview(ed, db);

                // --- Multi-point selection loop
                while (true)
                {
                    string promptMessage = preview.Points.Count == 0
                        ? "\nSelect first vertex (ENTER to finish, ESC to cancel):"
                        : "\nSelect next vertex (ENTER to finish, ESC to cancel):";

                    var opts = new PromptPointOptions(promptMessage)
                    {
                        AllowNone = true,   // allows ENTER to finish
                        AllowArbitraryInput = false
                    };

                    if (preview.Points.Count > 0)
                    {
                        opts.BasePoint = preview.Points[preview.Points.Count - 1];
                        opts.UseBasePoint = true;
                    }

                    var res = ed.GetPoint(opts);

                    if (res.Status == PromptStatus.OK)
                    {
                        preview.AddPoint(res.Value);
                    }
                    else if (res.Status == PromptStatus.None)
                    {
                        // ENTER pressed
                        break;
                    }
                    else if (res.Status == PromptStatus.Cancel)
                    {
                        TxtStatus.Text = "Grade beam creation canceled.";
                        preview.ErasePreview();
                        return;
                    }
                }

                if (preview.Points.Count < 2)
                {
                    TxtStatus.Text = "At least two points are required to create a grade beam.";
                    preview.ErasePreview();
                    return;
                }

                // --- Convert points to 2D polyline
                var verts = preview.Points.Select(p => new Point2d(p.X, p.Y)).ToList();
                verts = PolylineConversionService.EnsureMinimumVertices(verts, 5);
                var newPl = PolylineConversionService.CreatePolylineFromVertices(verts);

                // --- Register grade beam (centerline only)
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    _gradeBeamService.RegisterGradeBeam(context, newPl, tr, appendToModelSpace: true);
                    tr.Commit();
                }

                // --- Delete all edges for clean rebuild
                int edgesDeleted = GradeBeamManager.DeleteAllGradeBeamEdges(context);
                ed.WriteMessage($"\n[DEBUG] Deleted {edgesDeleted} existing grade beam edges.");

                // --- Rebuild edges
                _gradeBeamService.GenerateEdgesForAllGradeBeams(context);
                ed.WriteMessage("\n[DEBUG] Rebuilt grade beam edges.");

                // --- Update UI
                Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));

                TxtStatus.Text = "Custom grade beam added.";
                PrelimGBControl.ViewModel.IsPreliminaryGenerated = true;
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

                // --- Rebuild the ExtensionDataItem tree using the new handles-only ProcessDictionary ---
                var treeData = NODScanner.ProcessDictionary(context, tr, rootDict, db);

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
                PolylineBoundaryManager.RaiseBoundaryChanged();
            else if (!string.IsNullOrEmpty(error))
                TxtStatus.Text = error;
        }

        private void BtnEraseNODFully_Click()
        {
            NODCleaner.ClearFoundationNOD(CurrentContext);
        }

        private void BtnTest_Click()
        {
            //FoundationTestTools.TestGradeBeamNOD(CurrentContext);

            ////FoundationTestTools.TestDumpGradeBeamNod(CurrentContext);
            //FoundationTestTools.TestGradeBeamJsonRoundTrip(CurrentContext);
        }

        /// <summary>
        /// Queries the NOD for a list of handles in each subdirectory.
        /// </summary>
        private void BtnQueryNOD_Click(object sender, RoutedEventArgs e)
        {
            var context = CurrentContext;
            if (context == null) return;

            var doc = context.Document;
            if (doc == null) return;

            var db = doc.Database;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var rootDict = NODCore.GetFoundationRootDictionary(tr, db);
                if (rootDict == null)
                {
                    ScrollableMessageBox.Show("No EE_Foundation dictionary found.");
                    return;
                }

                // --- Build structured NOD tree using the new handles-only ProcessDictionary ---
                var treeData = NODScanner.ProcessDictionary(context, tr, rootDict, db);

                // --- Convert the tree to a string for debug purposes ---
                string treeString = NODTraversal.TreeToString(treeData);

                // --- Show the debug string ---
                ScrollableMessageBox.Show(treeString, "NOD Tree Structure");

                // --- Optional: populate TreeView UI if you want ---
                var treeMgr = new TreeViewManager(tr);
                treeMgr.PopulateFromData(TreeViewExtensionData, treeData);

                tr.Commit();
            }
        }

        // ---------------------------
        // Button click handler
        // ---------------------------
        private void BtnConvertToPolyline_Click(object sender, RoutedEventArgs e)
        {
            var context = CurrentContext;
            if (context?.Document == null)
                return;

            var doc = context.Document;
            var ed = doc.Editor;
            var db = doc.Database;

            try
            {
                while (true)
                {
                    var peo = new PromptEntityOptions(
                        "\nSelect a Line, Polyline, GradeBeam centerline, or edge <Enter to finish>:")
                    {
                        AllowNone = true // allows Enter to finish
                    };
                    peo.SetRejectMessage("\nOnly Line or Polyline objects are allowed.");
                    peo.AddAllowedClass(typeof(Line), true);
                    peo.AddAllowedClass(typeof(Polyline), true);

                    var per = ed.GetEntity(peo);

                    if (per.Status == PromptStatus.Cancel)
                        return; // user hit ESC

                    if (per.Status != PromptStatus.OK || per.ObjectId.IsNull)
                        break; // user hit ENTER with no selection

                    using (doc.LockDocument())
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        string owningHandle = null;
                        bool isCenterline = false;
                        bool isEdge = false;

                        bool belongsToGB = GradeBeamNOD.TryResolveOwningGradeBeam(
                            context,
                            tr,
                            per.ObjectId,
                            out owningHandle,
                            out isCenterline,
                            out isEdge);

                        if (belongsToGB)
                        {
                            ed.WriteMessage($"\n[DEBUG] Object {per.ObjectId.Handle} already belongs to grade beam '{owningHandle}' " +
                                            (isCenterline ? "(centerline)" : "(edge)"));
                            tr.Commit();
                            continue; // skip conversion
                        }

                        const int minVertexCount = 5;
                        _gradeBeamService.ConvertToGradeBeam(context, per.ObjectId, minVertexCount, tr);

                        tr.Commit();
                        ed.WriteMessage($"\n[DEBUG] Converted object {per.ObjectId.Handle} to grade beam.");
                    }

                    // --- Immediate redraw and edge regeneration
                    int deleted = GradeBeamManager.DeleteAllGradeBeamEdges(context);
                    ed.WriteMessage($"\n[DEBUG] Deleted {deleted} grade beam edges.");
                    _gradeBeamService.GenerateEdgesForAllGradeBeams(context);

                    Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
                }

                TxtStatus.Text = "Grade beams updated and edges regenerated.";
            }
            catch (Exception ex)
            {
                ed.WriteMessage($"\nError converting to GradeBeam: {ex.Message}");
                TxtStatus.Text = $"Error: {ex.Message}";
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
