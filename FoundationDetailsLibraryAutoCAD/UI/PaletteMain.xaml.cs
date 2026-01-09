using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using FoundationDetailer.AutoCAD;
using FoundationDetailer.Managers;
using FoundationDetailer.UI.Controls;
using FoundationDetailsLibraryAutoCAD.AutoCAD;
using FoundationDetailsLibraryAutoCAD.Data;
using FoundationDetailsLibraryAutoCAD.Managers;
using FoundationDetailsLibraryAutoCAD.UI.Controls;
using FoundationDetailsLibraryAutoCAD.UI.Controls.EqualSpacingGBControl;
using FoundationDetailsLibraryAutoCAD.UI.Controls.GradeBeamSummaryControl;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

            BtnQuery.Click += (s, e) => BtnQueryNOD_Click();
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
            BtnNEqualSpaces.Click += NEqualSpaces_Click;
            BtnConvertExisting.Click += BtnConvertToPolyline_Click;

        }

        private void NEqualSpaces_Click(object sender, RoutedEventArgs e)
        {
            var doc = CurrentContext.Document;
            var ed = doc.Editor;

            // get the interpolated points.
            (Point3d? start, Point3d? end) = _gradeBeamService.PromptForSpacingPoints(CurrentContext);
            if (start == null || end == null)
            {
                ed.WriteMessage("No points selected.");
                return;
            }
            var spaces = _gradeBeamService.PromptForEqualSpacingCount(CurrentContext);
            if (spaces <= 1)
            {
                ed.WriteMessage("At least 2 spaces are required.");
                return;
            }
            var dir = _gradeBeamService.PromptForSpacingDirection(CurrentContext);
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
                _gradeBeamService.AddExistingPolylineAsGradeBeam(context, selectedId, tr);

                tr.Commit();
            }

            // Refresh the UI asynchronously
            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
        }




        #region --- UI Updates ---
        private void PrelimGBControl_AddPreliminaryClicked(object sender, PrelimGBEventArgs e)
        {
            var context = CurrentContext;
            var doc = context.Document;

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
            var doc = context.Document;
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

            var doc = context.Document;
            if(doc == null) return;

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
                var treeMgr = new TreeViewManager();
                var root = NODManager.GetFoundationRoot(context, tr);
                if (root == null) return;

                var nodeMap = new Dictionary<string, TreeViewItem>();

                // Root node
                var rootNode = new TreeViewItem
                {
                    Header = NODManager.ROOT,
                    IsExpanded = true,
                    Tag = new TreeViewManager.TreeNodeInfo(NODManager.ROOT, true)
                };

                TreeViewExtensionData.Items.Add(rootNode);

                // Recursive build for all subdictionaries & entities
                BuildTreeRecursiveWithEntities(root, rootNode, tr, nodeMap, treeMgr, "");

                // Count immediate children for each dictionary node
                var branchCounts = new Dictionary<TreeViewItem, int>();
                foreach (var kvp in nodeMap)
                {
                    var node = kvp.Value;
                    if (node.Tag is TreeViewManager.TreeNodeInfo info && info.IsDictionary)
                    {
                        int count = 0;
                        foreach (TreeViewItem child in node.Items)
                        {
                            if (child.Tag is TreeViewManager.TreeNodeInfo) count++;
                        }
                        branchCounts[node] = count;
                    }
                }

                // Update headers with counts
                foreach (var kvp in branchCounts)
                {
                    var node = kvp.Key;
                    int count = kvp.Value;

                    var tb = new TextBlock();
                    tb.Inlines.Add(new Run($" ({count}) ") { FontWeight = FontWeights.Bold });
                    tb.Inlines.Add(new Run(node.Header.ToString()));
                    node.Header = tb;
                }

                tr.Commit();
            }
        }

        /// <summary>
        /// Recursively builds the TreeView for a dictionary and its subdictionaries/entities.
        /// </summary>
        private static void BuildTreeRecursiveWithEntities(
            DBDictionary dict,
            TreeViewItem parentNode,
            Transaction tr,
            Dictionary<string, TreeViewItem> nodeMap,
            TreeViewManager treeMgr,
            string pathSoFar)
        {
            foreach (DBDictionaryEntry entry in dict)
            {
                string entryKey = entry.Key;
                string fullPath = string.IsNullOrEmpty(pathSoFar) ? entryKey : $"{pathSoFar}/{entryKey}";

                DBObject obj = tr.GetObject(entry.Value, OpenMode.ForRead);

                var node = new TreeViewItem
                {
                    Header = entryKey,
                    Tag = new TreeViewManager.TreeNodeInfo(entryKey, obj is DBDictionary)
                };

                parentNode.Items.Add(node);
                nodeMap[fullPath] = node;

                if (obj is DBDictionary subDict)
                {
                    // Recurse into subdictionary
                    BuildTreeRecursiveWithEntities(subDict, node, tr, nodeMap, treeMgr, fullPath);
                }
                else
                {
                    // Leaf entity (Entity or Xrecord)
                    if (obj is Entity ent)
                    {
                        node.Tag = new TreeViewManager.TreeNodeInfo(entryKey, false) { Entity = ent };
                        node.Header = $"{entryKey} ({ent.Handle})";
                    }
                    else if (obj is Xrecord xr)
                    {
                        node.Tag = new TreeViewManager.TreeNodeInfo(entryKey, false);
                        node.Header = $"{entryKey} (Xrecord)";
                    }
                }
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

        /// <summary>
        /// Queries the NOD for a list of handles in each subdirectory.
        /// </summary>
        private void BtnQueryNOD_Click()
        {
            var context = CurrentContext;
            NODManager.CleanFoundationNOD(context);
            NODManager.ViewFoundationNOD(context); // optional debug

            var doc = context?.Document;
            if(doc == null) return;

            var db = doc?.Database;
            if(db == null) return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary root = NODManager.GetFoundationRoot(context, tr);
                string tree = NODDebugger.DumpDictionaryTree(root, tr, "EE_Foundation");
                Console.WriteLine(tree);
            }

            // Updates the TreeView for the handles.
            UpdateTreeViewUI();

        }

        // ---------------------------
        // Button click handler
        // ---------------------------
        private void BtnConvertToPolyline_Click(object sender, RoutedEventArgs e)
        {
            Document doc = CurrentContext.Document;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptEntityOptions peo = new PromptEntityOptions("\nSelect a Line or Polyline to convert:");
            peo.SetRejectMessage("\nOnly Line or Polyline are allowed.");
            peo.AddAllowedClass(typeof(Line), true);
            peo.AddAllowedClass(typeof(Polyline), true);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            ObjectId selectedId = per.ObjectId;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity ent = tr.GetObject(selectedId, OpenMode.ForRead) as Entity;
                if (ent == null) return;

                // Collect vertices
                List<Point2d> verts = new List<Point2d>();

                if (ent is Line line)
                {
                    verts.Add(new Point2d(line.StartPoint.X, line.StartPoint.Y));
                    verts.Add(new Point2d(line.EndPoint.X, line.EndPoint.Y));
                }
                else if (ent is Polyline pl)
                {
                    for (int i = 0; i < pl.NumberOfVertices; i++)
                        verts.Add(pl.GetPoint2dAt(i));
                }
                else
                {
                    ed.WriteMessage("\nUnsupported entity type.");
                    return;
                }

                // Ensure minimum vertices
                verts = EnsureMinimumVertices(verts, 5);

                // Create new Polyline
                Polyline newPl = new Polyline();
                for (int i = 0; i < verts.Count; i++)
                    newPl.AddVertexAt(i, verts[i], 0, 0, 0);

                newPl.LayerId = ent.LayerId;
                newPl.Color = ent.Color;
                newPl.LinetypeId = ent.LinetypeId;
                newPl.LineWeight = ent.LineWeight;

                // Append to ModelSpace
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                btr.AppendEntity(newPl);
                tr.AddNewlyCreatedDBObject(newPl, true);

                // Delete original
                ent.UpgradeOpen();
                ent.Erase();

                // Add to GradeBeam NOD
                _gradeBeamService.AddExistingPolylineAsGradeBeam(CurrentContext, newPl.ObjectId, tr);

                tr.Commit();
            }

            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
        }

        // ==================================================
        // Subdivide until minimum vertex count is met
        // ==================================================
        private static List<Point2d> EnsureMinimumVertices(
            List<Point2d> input, int minCount)
        {
            if (input.Count >= minCount)
                return input;

            List<Point2d> result = new List<Point2d>(input);

            while (result.Count < minCount)
            {
                int longestIndex = 0;
                double maxDist = 0.0;

                for (int i = 0; i < result.Count - 1; i++)
                {
                    double d =
                        result[i].GetDistanceTo(result[i + 1]);

                    if (d > maxDist)
                    {
                        maxDist = d;
                        longestIndex = i;
                    }
                }

                Point2d a = result[longestIndex];
                Point2d b = result[longestIndex + 1];

                Point2d mid = new Point2d(
                    (a.X + b.X) * 0.5,
                    (a.Y + b.Y) * 0.5);

                result.Insert(longestIndex + 1, mid);
            }

            return result;
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
