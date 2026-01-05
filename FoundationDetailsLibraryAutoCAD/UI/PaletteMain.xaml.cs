using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using FoundationDetailer.AutoCAD;
using FoundationDetailer.Managers;
using FoundationDetailer.UI.Controls;
using FoundationDetailsLibraryAutoCAD.AutoCAD;
using FoundationDetailsLibraryAutoCAD.Data;
using FoundationDetailsLibraryAutoCAD.Managers;
using FoundationDetailsLibraryAutoCAD.UI.Controls;
using FoundationDetailsLibraryAutoCAD.UI.Controls.GradeBeamSummaryControl;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using static FoundationDetailsLibraryAutoCAD.AutoCAD.NODManager;
using static FoundationDetailsLibraryAutoCAD.Managers.TreeViewManager;
using static FoundationDetailsLibraryAutoCAD.UI.Controls.PrelimGBControl.PrelimGradeBeamControl;

namespace FoundationDetailsLibraryAutoCAD.UI
{
    public partial class PaletteMain : UserControl
    {

        private PierControl PierUI;

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
                NODManager.InitFoundationNOD(context, tr);
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

            BtnQuery.Click += (s, e) => btnQueryNOD_Click();
            BtnSelectBoundary.Click += (s, e) => btnDefineFoundationBoundary_Click();
            BtnSave.Click += (s, e) => btnSaveModel_Click();
            BtnLoad.Click += (s, e) => btnLoadModel_Click();

            BtnShowBoundary.Click += (s, e) => _boundaryService.HighlightBoundary(context);
            BtnZoomBoundary.Click += (s, e) => _boundaryService.ZoomToBoundary(context);

            PrelimGBControl.AddPreliminaryClicked += PrelimGBControl_AddPreliminaryClicked;
            GradeBeamSummary.ClearAllClicked += GradeBeam_ClearAllClicked;
            GradeBeamSummary.HighlightGradeBeamslClicked += GradeBeam_HighlightGradeBeamsClicked;
            GradeBeamSummary.AddSingleGradeBeamClicked += GradeBeamSummary_AddSingleGradeBeamClicked;

        }

        #region --- UI Updates ---
        private void PrelimGBControl_AddPreliminaryClicked(object sender, PrelimGBEventArgs e)
        {
            var context = CurrentContext;

            if (!_boundaryService.TryGetBoundary(context, out Polyline boundary))
            {
                TxtStatus.Text = "No boundary selected.";
                return;
            }

            _gradeBeamService.CreatePreliminary(
                context,
                boundary,
                e.HorzMin,
                e.HorzMax,
                e.VertMin,
                e.VertMax
            );
            PrelimGBControl.ViewModel.IsPreliminaryGenerated = true;  // reset the the preliminary input control

            // Hide the gradebeam control completely
            PrelimGBControl.Visibility = System.Windows.Visibility.Collapsed;

            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
        }

        private void GradeBeam_ClearAllClicked(object sender, EventArgs e)
        {
            var context = CurrentContext;
            _gradeBeamService.ClearAll(context);
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

            try
            {
                // Prompt user for first point
                var firstPointRes = context.Document.Editor.GetPoint("\nSelect first point for grade beam:");
                if (firstPointRes.Status != Autodesk.AutoCAD.EditorInput.PromptStatus.OK)
                {
                    TxtStatus.Text = "First point selection canceled.";
                    return;
                }

                // Prompt user for second point using an autocad jig for preview
                var jig = new GradeBeamPolylineJig(firstPointRes.Value);
                var res = context.Document.Editor.Drag(jig);


                var pt1 = firstPointRes.Value;
                var pt2 = jig.Polyline.GetPoint3dAt(1);

                // Validation: points not the same
                if (pt1.IsEqualTo(pt2))
                {
                    TxtStatus.Text = "Points cannot be the same.";
                    context.Document.Editor.GetPoint("\nPoints cannot be the same.");
                    return;
                }

                // Ask the manager to create the polyline
                _gradeBeamService.AddInterpolatedGradeBeam(context, pt1, pt2, 5);

                // Refresh UI
                Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));

                TxtStatus.Text = "Custom grade beam added.";
                context.Document.Editor.GetPoint("Custom grade beam added.");

                PrelimGBControl.ViewModel.IsPreliminaryGenerated = true;  // reset the the preliminary input control

                // Hide the gradebeam control completely
                PrelimGBControl.Visibility = System.Windows.Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Error adding grade beam: {ex.Message}";
                context.Document.Editor.GetPoint("Custom grade beam added.");
            }
        }

        private void UpdateBoundaryDisplay()
        {
            var context = CurrentContext;
            if ( context == null )
            {
                throw new Exception("Un UpdateBoundaryDCurrent context is null.");
            }

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

            // TreeNodeInfo holds the Entity
            if (!(tvi.Tag is TreeViewManager.TreeNodeInfo nodeInfo))
                return;

            Entity ent = nodeInfo.Entity;
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

            TreeViewExtensionData.Items.Clear();

            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var _tree_view_mgr = new TreeViewManager();

                var root = NODManager.GetFoundationRoot(context, tr);
                if (root == null) return;

                var nodeMap = new Dictionary<string, TreeViewItem>();

                // Root node
                var rootNode = new TreeViewItem
                {
                    Header = NODManager.ROOT,
                    IsExpanded = true,
                    Tag = new TreeNodeInfo(NODManager.ROOT, isDictionary: true)
                };

                TreeViewExtensionData.Items.Add(rootNode);

                // PASS 1: Build the tree
                _tree_view_mgr.BuildTree(root, rootNode, tr, nodeMap);

                // PASS 2: Attach entities + branch counts
                var branchCounts = new Dictionary<string, int>();

                NODManager.TraverseDictionary(context, tr, root, doc.Database, result =>
                {
                    if (result.Status != TraversalStatus.Success)
                        return;

                    if (!nodeMap.TryGetValue(result.Key, out var leafNode))
                        return;

                    if (!(leafNode.Tag is TreeNodeInfo leafInfo))
                        return;

                    // Attach entity
                    leafInfo.Entity = result.Entity;
                    FoundationEntityData.DisplayExtensionData(context, result.Entity);

                    // Determine parent branch key
                    string branchKey = (leafNode.Parent as TreeViewItem)?.Tag is TreeNodeInfo parentInfo
                        ? parentInfo.Key
                        : null;
                    Debug.WriteLine($"Leaf: {leafInfo.Key}, Parent Key: {branchKey}");

                    // --------------------------
                    // Use the _controlMap field to create custom header if available
                    // --------------------------
                    if (branchKey != null && _tree_view_mgr._controlMap.TryGetValue(branchKey, out var factory))
                    {
                        // Call the factory to create a new TreeViewItem
                        var newLeafNode = factory(leafInfo);

                        // Assign a NEW instance of the control, not the old one
                        if (newLeafNode.Header is PolylineTreeItemControl control)
                        {
                            leafNode.Header = new PolylineTreeItemControl
                            {
                                DataContext = control.DataContext
                            };
                        }
                        else
                        {
                            // Fallback: assign whatever the factory returned
                            leafNode.Header = newLeafNode.Header;
                        }
                    }
                    else
                    {
                        // Fallback: just show key text
                        leafNode.Header = leafInfo.Key;
                    }

                    // Count leaf under immediate dictionary parent
                    if (leafNode.Parent is TreeViewItem parentNode &&
                        parentNode.Tag is TreeNodeInfo parentInfo2 &&
                        parentInfo2.IsDictionary)
                    {
                        branchCounts[parentInfo2.Key] =
                            branchCounts.TryGetValue(parentInfo2.Key, out int count)
                                ? count + 1
                                : 1;
                    }
                });

                // PASS 3: Update branch headers with counts
                foreach (var kvp in branchCounts)
                {
                    if (!nodeMap.TryGetValue(kvp.Key, out var branchNode))
                        continue;

                    var tb = new TextBlock();
                    tb.Inlines.Add(new Run($" ({kvp.Value}) ")
                    {
                        FontWeight = FontWeights.Bold
                    });
                    tb.Inlines.Add(new Run(kvp.Key));

                    branchNode.Header = tb;
                }

                tr.Commit();
            }
        }

        #endregion


        #region --- UI Button Click Handlers ---
        private void btnDefineFoundationBoundary_Click()
        {
            var context = CurrentContext;
            if (_boundaryService.SelectBoundary(context, out string error))
                Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
            else if (!string.IsNullOrEmpty(error))
                TxtStatus.Text = error;
        }

        /// <summary>
        /// Queries the NOD for a list of handles in each subdirectory.
        /// </summary>
        private void btnQueryNOD_Click()
        {
            var context = CurrentContext;
            NODManager.CleanFoundationNOD(context);
            NODManager.ViewFoundationNOD(context); // optional debug

            // Updates the TreeView for the handles.
            UpdateTreeViewUI();

        }



        private void btnSaveModel_Click()
        {
            var context = CurrentContext;
            _persistenceService.Save(context);
        }
        private void btnLoadModel_Click()
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

        #endregion

        private void RefreshGradeBeamSummary()
        {
            var context = CurrentContext;

            int quantity;
            double total_length;
            (quantity, total_length) = _gradeBeamService.GetGradeBeamSummary(context);

            GradeBeamSummary.UpdateSummary(quantity, total_length);
        }
    }
}
