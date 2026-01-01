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

        private PierControl PierUI;

        private readonly PolylineBoundaryManager _boundaryService = new PolylineBoundaryManager();
        private readonly GradeBeamManager _gradeBeamService = new GradeBeamManager();
        private readonly FoundationPersistenceManager _persistenceService = new FoundationPersistenceManager();

        private FoundationContext CurrentContext => FoundationContext.For(Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument);



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
            BtnQuery.Click += (s, e) => btnQueryNOD_Click();
            BtnSelectBoundary.Click += (s, e) => btnDefineFoundationBoundary_Click();
            BtnAddGradeBeams.Click += (s, e) => btnAddPreliminaryGradeBeams_Click();
            BtnClearGradeBeams.Click += btnClearAllGradeBeams_Click;
            BtnSave.Click += (s, e) => btnSaveModel_Click();
            BtnLoad.Click += (s, e) => btnLoadModel_Click();

            BtnShowBoundary.Click += (s, e) => PolylineBoundaryManager.HighlightBoundary();
            BtnZoomBoundary.Click += (s, e) => PolylineBoundaryManager.ZoomToBoundary();

            TxtGBHorzMin.TextChanged += Spacing_TextChanged;
            TxtGBHorzMax.TextChanged += Spacing_TextChanged;
            TxtGBVertMin.TextChanged += Spacing_TextChanged;
            TxtGBVertMax.TextChanged += Spacing_TextChanged;
        }

        #region --- UI Updates ---

        private void UpdateBoundaryDisplay()
        {
            var context = CurrentContext;
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

        private void TreeViewExtensionData_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (!(e.NewValue is TreeViewItem tvi && tvi.Tag is Entity ent))
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

        #endregion

        #region --- NOD / TreeView ---


        /// <summary>
        /// Helper function to update the data in the TreeView for the HANDLES from the NOD.
        /// </summary>
        internal void UpdateTreeViewUI()
        {
            var context = CurrentContext;
            var doc = context.Document;
            if (doc == null) return;

            TreeViewExtensionData.Items.Clear();

            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var root = NODManager.GetFoundationRoot(context, tr);
                if (root == null) return;

                var nodeMap = new Dictionary<string, TreeViewItem>();
                var rootNode = new TreeViewItem { Header = NODManager.ROOT, IsExpanded = true };
                TreeViewExtensionData.Items.Add(rootNode);

                NODManager.BuildTree(root, rootNode, tr, nodeMap);

                var dictCounts = new Dictionary<string, int>();
                NODManager.TraverseDictionary(tr, root, doc.Database, (ent, handle) =>
                {
                    if (nodeMap.TryGetValue(handle, out var node))
                    {
                        node.Tag = ent;
                        FoundationEntityData.DisplayExtensionData(ent);

                        if (node.Parent is TreeViewItem parentNode)
                        {
                            string parentName = parentNode.Header.ToString();
                            dictCounts[parentName] = dictCounts.ContainsKey(parentName) ? dictCounts[parentName] + 1 : 1;
                        }
                    }
                });

                foreach (var kvp in dictCounts)
                {
                    var node = NODManager.FindNodeByHeader(rootNode, kvp.Key);
                    if (node != null)
                    {
                        var tb = new TextBlock();
                        tb.Inlines.Add(new Run($" ({kvp.Value})  ") { FontWeight = FontWeights.Bold });
                        tb.Inlines.Add(new Run(kvp.Key));
                        node.Header = tb;
                    }
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

        private void btnAddPreliminaryGradeBeams_Click()
        {
            var context = CurrentContext;
            if (!PolylineBoundaryManager.TryGetBoundary(out Polyline boundary))
            {
                TxtStatus.Text = "No boundary selected.";
                return;
            }

            _gradeBeamService.CreatePreliminary(context, boundary,
                HorzGBMinSpacing, HorzGBMaxSpacing,
                VertGBMinSpacing, VertGBMaxSpacing);

            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
        }

        private void btnClearAllGradeBeams_Click(object sender, RoutedEventArgs e)
        {
            var context = CurrentContext;
            _gradeBeamService.ClearAll(context);
            TxtStatus.Text = "All grade beams cleared.";
            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
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
    }
}
