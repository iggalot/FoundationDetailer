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
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using static FoundationDetailsLibraryAutoCAD.Data.FoundationEntityData;

namespace FoundationDetailer.UI
{
    public partial class PaletteMain : UserControl
    {
        private FoundationModel _currentModel = new FoundationModel();
        private PierControl PierUI;

        public double HorzGBMinSpacing => ParseDoubleOrDefault(TxtGBHorzMin.Text, 5.0);
        public double HorzGBMaxSpacing => ParseDoubleOrDefault(TxtGBHorzMax.Text, 12.0);
        public double VertGBMinSpacing => ParseDoubleOrDefault(TxtGBVertMin.Text, 5.0);
        public double VertGBMaxSpacing => ParseDoubleOrDefault(TxtGBVertMax.Text, 12.0);

        private readonly Brush _invalidBrush = Brushes.LightCoral;
        private readonly Brush _validBrush = Brushes.White;


        private double ParseDoubleOrDefault(string text, double defaultValue)
        {
            if (double.TryParse(text, out double val))
                return val;
            return defaultValue;
        }



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
            RestoreBoundaryAfterImport();

            UpdateTreeViewHandleUI();
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
            BtnClearGradeBeams.Click += BtnClearAllGradeBeams_Click;

            TxtGBHorzMin.TextChanged += Spacing_TextChanged;
            TxtGBHorzMax.TextChanged += Spacing_TextChanged;
            TxtGBVertMin.TextChanged += Spacing_TextChanged;
            TxtGBVertMax.TextChanged += Spacing_TextChanged;
        }

        private void Spacing_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                if (IsValidSpacing(tb.Text, out double val))
                {
                    tb.Background = _validBrush;
                    // Optionally store the value somewhere if needed
                }
                else
                {
                    tb.Background = _invalidBrush;
                }
                // Optionally, validate input and store updated spacing values
                double hMin = HorzGBMinSpacing;
                double hMax = HorzGBMaxSpacing;
                double vMin = VertGBMinSpacing;
                double vMax = VertGBMaxSpacing;

                // For debug or status update
                TxtStatus.Text = $"H: {hMin}-{hMax}, V: {vMin}-{vMax}";
            }
        }

        private bool IsValidSpacing(string text, out double value)
        {
            value = 0;

            // Must parse as double
            if (!double.TryParse(text, out value))
                return false;

            // Must be non-negative
            if (value < 0)
                return false;

            // Optionally, you could check for min <= max if you have both values
            return true;
        }


        /// <summary>
        /// Queries the NOD for a list of handles in each subdirectory.
        /// </summary>
        private void QueryNOD()
        {
            NODManager.ViewFoundationNOD(); // optional debug

            // Updates the TreeView for the handles.
            UpdateTreeViewHandleUI();

        }

        /// <summary>
        /// Helper function to update the data in the TreeView for the HANDLES from the NOD.
        /// </summary>
        internal void UpdateTreeViewHandleUI()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var db = doc.Database;

            TreeViewExtensionData.Items.Clear();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var root = NODManager.GetFoundationRoot(tr, db);
                if (root == null)
                    return;

                var nodeMap = new Dictionary<string, TreeViewItem>();

                TreeViewItem rootNode = new TreeViewItem
                {
                    Header = NODManager.ROOT,
                    IsExpanded = true
                };

                TreeViewExtensionData.Items.Add(rootNode);

                // Build the foundation tree.
                NODManager.BuildTree(root, rootNode, tr, nodeMap);


                var dictCounts = new Dictionary<string, int>(); // key = subdictionary name, value = count

                // Traverse the Dictionary to check the extension data.
                NODManager.TraverseDictionary(
                    tr,
                    root,
                    db,
                    (ent, handle) =>
                    {
                        if (nodeMap.TryGetValue(handle, out var node))
                        {
                            node.Tag = ent;
                            FoundationEntityData.DisplayExtensionData(ent);

                            // Update the count for the parent dictionary
                            TreeViewItem parentNode = node.Parent as TreeViewItem;
                            if (parentNode != null)
                            {
                                string parentName = parentNode.Header.ToString();
                                if (dictCounts.ContainsKey(parentName))
                                    dictCounts[parentName]++;
                                else
                                    dictCounts[parentName] = 1;
                            }
                        }
                    });

                foreach (var kvp in dictCounts)
                {
                    TreeViewItem node = NODManager.FindNodeByHeader(TreeViewExtensionData.Items[0] as TreeViewItem, kvp.Key);
                    if (node != null)
                    {
                        TextBlock tb = new TextBlock();

                        // Quantity: bold
                        tb.Inlines.Add(new Run($" ({kvp.Value})  ") { FontWeight = FontWeights.Bold });

                        // Subdictionary name: normal weight
                        tb.Inlines.Add(new Run(kvp.Key));

                        node.Header = tb;
                    }
                }

                tr.Commit();
            }

        }




        /// <summary>
        /// Cleans the NOD of any stake handles
        /// </summary>
        private void SyncNodData()
        {
            NODManager.CleanFoundationNOD();
        }

        #region --- Boundary Selection and UI Updates ---

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
                TxtBoundaryStatus.Text = "Boundary valid - " + pl.ObjectId.Handle.ToString();
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

            // Update the tree Viewer
            UpdateTreeViewHandleUI();
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
            var db = doc.Database;

            // Prompt for a closed polyline
            PromptEntityOptions options = new PromptEntityOptions("\nSelect a closed polyline: ");
            options.SetRejectMessage("\nMust be a closed polyline.");
            options.AddAllowedClass(typeof(Polyline), false);

            var result = ed.GetEntity(options);
            if (result.Status != PromptStatus.OK) return;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    // Open the selected entity
                    Polyline boundary = tr.GetObject(
                        result.ObjectId,
                        OpenMode.ForWrite) as Polyline;

                    if (boundary == null)
                    {
                        ed.WriteMessage("\nSelected object is not a polyline.");
                        return;
                    }

                    // Try set the boundary (no DB writes assumed here)
                    if (!PolylineBoundaryManager.TrySetBoundary(
                            result.ObjectId, out string error))
                    {
                        ed.WriteMessage($"\nError setting boundary: {error}");
                        return;
                    }

                    // Attach entity-side metadata
                    FoundationEntityData.Write(tr, boundary, NODManager.KEY_BOUNDARY);

                    // Register handle in the NOD
                    PolylineBoundaryManager.AddBoundaryHandleToNOD(tr, boundary.ObjectId);

                    tr.Commit();

                    ed.WriteMessage(
                        $"\nBoundary selected: {boundary.Handle}");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nBoundary selection failed: {ex.Message}");
                }
            }
        }

        private void BtnClearAllGradeBeams_Click(object sender, RoutedEventArgs e)
        {
            Database db = Autodesk.AutoCAD.ApplicationServices.Application
                .DocumentManager
                .MdiActiveDocument
                .Database;

            // Delete the AutoCAD entities
            NODManager.DeleteEntitiesFromFoundationSubDictionary(
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Database,
                NODManager.KEY_GRADEBEAM
                );

            // Clear the NOD
            NODManager.ClearFoundationSubDictionary(db, NODManager.KEY_GRADEBEAM);

            TxtStatus.Text = "All grade beams cleared.";

            // Update UI immediately
            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
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
                double horizMin = HorzGBMinSpacing;
                double horizMax = HorzGBMaxSpacing;
                double vertMin = VertGBMinSpacing;
                double vertMax = VertGBMaxSpacing;

                using (doc.LockDocument())
                {
                    // Let GradeBeamManager handle everything internally
                    GradeBeamManager.CreateBothGridlines(boundary, horizMin, horizMax, vertMin, vertMax, vertexCount);

                    doc.Editor.WriteMessage("\nGrade beams created successfully.");
                }

                // Update UI immediately
                Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                doc.Editor.WriteMessage($"\nError creating grade beams: {ex.Message}");
            }

            // Update UI immediately
            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
        }

        private void AddRebarBars()
        {
            MessageBox.Show("Add rebar bars to model.");
            // Update UI immediately
            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));

        }
        private void AddStrands()
        {
            MessageBox.Show("Add strands to model.");
            // Update UI immediately
            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));

        }

        private void SaveModel()
        {
            NODManager.ExportFoundationNOD();
            // Update UI immediately
            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
        }

        private void LoadModel()
        {
            NODManager.ImportFoundationNOD();
            // Update UI immediately
            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
        }

        #endregion

        private void RestoreBoundaryAfterImport()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                if (PolylineBoundaryManager.TryRestoreBoundaryFromNOD(doc.Database, tr, out ObjectId boundaryId))
                {
                    // Set boundary in PolylineBoundaryManager (this triggers BoundaryChanged event)
                    if (!PolylineBoundaryManager.TrySetBoundary(boundaryId, out string error))
                    {
                        doc.Editor.WriteMessage($"\nFailed to set boundary: {error}");
                    }
                    else
                    {
                        doc.Editor.WriteMessage("\nBoundary restored from NOD.");
                    }
                }

                tr.Commit(); // read-only, but commit for consistency
            }

            // Update UI immediately
            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
        }

        private TreeViewItem CreateTreeViewItem(ExtensionDataItem dataItem)
        {
            string headerText = dataItem.Value != null
                ? $"{dataItem.Name} ({dataItem.Type}): {FormatValue(dataItem.Value)}"
                : $"{dataItem.Name} ({dataItem.Type})";

            var treeItem = new TreeViewItem { Header = headerText };

            foreach (var child in dataItem.Children)
            {
                treeItem.Items.Add(CreateTreeViewItem(child));
            }

            return treeItem;
        }

        private string FormatValue(object value)
        {
            if (value is IEnumerable<string> list)
                return string.Join(", ", list);

            return value?.ToString() ?? "";
        }

        private void TreeViewExtensionData_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem tvi && tvi.Tag is Entity ent)
            {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                var ed = doc.Editor;

                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    ed.SetImpliedSelection(new ObjectId[] { ent.ObjectId });
                    ed.UpdateScreen();
                    tr.Commit();
                }
            }
        }



    }
}
