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
using FoundationDetailsLibraryAutoCAD.Managers;
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

        private readonly PolylineBoundaryManager _boundaryService = new PolylineBoundaryManager();
        private readonly GradeBeamManager _gradeBeamService = new GradeBeamManager();
        private readonly FoundationPersistenceManager _persistenceService = new FoundationPersistenceManager();


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

            // Create the btnQueryNOD_Click dictionaries
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                NODManager.InitFoundationNOD(tr);  // initialize the NOD for our application
                tr.Commit();
            }

            PolylineBoundaryManager.BoundaryChanged += OnBoundaryChanged;  // subscribe for the boundary changed event

            // Initialize PierControl
            //PierUI = new PierControl();
            //PierUI.PierAdded += OnPierAdded;
            //PierUI.RequestPierLocationPick += btnPickPierLocation_Click;

            //PierContainer.Children.Clear();
            //PierContainer.Children.Add(PierUI);

            WireEvents();

            // Load the saved NOD (if available)
            NODManager.ImportFoundationNOD();
            PolylineBoundaryManager.RestoreBoundaryAfterImport();

            // Update UI immediately
            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));

            UpdateTreeViewUI();
        }

        private void WireEvents()
        {
            BtnQuery.Click += (s, e) => btnQueryNOD_Click();

            BtnSelectBoundary.Click += (s, e) => btnDefineFoundationBoundary_Click(); // for selecting the boundary

            BtnAddGradeBeams.Click += (s, e) => btnAddPreliminaryGradeBeams_Click(); // for adding a preliminary gradebeam layout

            //BtnAddRebar.Click += (s, e) => btnAddRebarBars_Click();
            //BtnAddStrands.Click += (s, e) => btnAddStrands_Click();
            //BtnAddPiers.Click += (s, e) => btnAddPiers_Click();

            //BtnPreview.Click += (s, e) => ShowPreview();
            //BtnClearPreview.Click += (s, e) => ClearPreview();
            //BtnCommit.Click += (s, e) => CommitModel();
            BtnSave.Click += (s, e) => btnSaveModel_Click();
            BtnLoad.Click += (s, e) => btnLoadModel_Click();

            BtnShowBoundary.Click += (s, e) => PolylineBoundaryManager.HighlightBoundary();
            BtnZoomBoundary.Click += (s, e) => PolylineBoundaryManager.ZoomToBoundary();

            BtnHighlightGradeBeams.Click += (s, e) =>
            {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                GradeBeamManager.HighlightGradeBeams(doc);
            };
            BtnClearGradeBeams.Click += btnClearAllGradeBeams_Click;

            TxtGBHorzMin.TextChanged += Spacing_TextChanged;
            TxtGBHorzMax.TextChanged += Spacing_TextChanged;
            TxtGBVertMin.TextChanged += Spacing_TextChanged;
            TxtGBVertMax.TextChanged += Spacing_TextChanged;
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


        private void Spacing_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                if (GridlineManager.IsValidSpacing(tb.Text, out double val))
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




        /// <summary>
        /// Helper function to update the data in the TreeView for the HANDLES from the NOD.
        /// </summary>
        internal void UpdateTreeViewUI()
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


        #region --- UI Button Click Handlers ---
        private void btnDefineFoundationBoundary_Click()
        {
            if (_boundaryService.SelectBoundary(out string error))
                Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
            else if (!string.IsNullOrEmpty(error))
                TxtStatus.Text = error;
        }

        /// <summary>
        /// Queries the NOD for a list of handles in each subdirectory.
        /// </summary>
        private void btnQueryNOD_Click()
        {
            NODManager.CleanFoundationNOD();
            NODManager.ViewFoundationNOD(); // optional debug

            // Updates the TreeView for the handles.
            UpdateTreeViewUI();

        }

        private void btnAddPreliminaryGradeBeams_Click()
        {
            if (!PolylineBoundaryManager.TryGetBoundary(out Polyline boundary))
            {
                TxtStatus.Text = "No boundary selected.";
                return;
            }

            _gradeBeamService.CreatePreliminary(
                boundary,
                HorzGBMinSpacing, HorzGBMaxSpacing,
                VertGBMinSpacing, VertGBMaxSpacing);

            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
        }
        private void btnClearAllGradeBeams_Click(object sender, RoutedEventArgs e)
        {
            _gradeBeamService.ClearAll();   
            TxtStatus.Text = "All grade beams cleared.";

            // Update UI immediately
            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
        }
        private void btnAddPiers_Click() => MessageBox.Show("Add piers to model.");
        private void btnAddRebarBars_Click()
        {
            MessageBox.Show("Add rebar bars to model.");
            // Update UI immediately
            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));

        }
        private void btnAddStrands_Click()
        {
            MessageBox.Show("Add strands to model.");
            // Update UI immediately
            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));

        }
        private void btnSaveModel_Click()
        {
            _persistenceService.Save();
        }
        private void btnLoadModel_Click()
        {
            _persistenceService.Load();
        }
        private void btnPickPierLocation_Click()
        {
            MessageBox.Show("Pick pier location in AutoCAD.");
        }
        #endregion


        #region --- UI Events Handlers ---

        private void OnPierAdded(PierData data)
        {
            Pier pier = PierConverter.ToModelPier(data);
            CurrentModel.Piers.Add(pier);
            Dispatcher.BeginInvoke(new Action(() =>
                TxtStatus.Text = $"Pier added at ({pier.Location.X:F2}, {pier.Location.Y:F2})"));
        }

        private void OnBoundaryChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
        }



        
        #endregion

    }
}
