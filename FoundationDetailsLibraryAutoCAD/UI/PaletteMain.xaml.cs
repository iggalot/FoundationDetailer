using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using FoundationDetailer.AutoCAD;
using FoundationDetailer.Managers;
using FoundationDetailsLibraryAutoCAD.AutoCAD;
using FoundationDetailsLibraryAutoCAD.AutoCAD.NOD;
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

            UpdateAll();
            //Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));

            UpdateTreeViewUI();



            // Optional: auto-refresh on document switch
            Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.DocumentActivated += OnDocumentActivated;

        }

        private void WireEvents()
        {
            var context = CurrentContext;

            BtnQuery.Click += BtnQueryNOD_Click;
            BtnEraseNODFully.Click += (s, e) => BtnEraseNODFully_Click();
            BtnDeleteMultipleGradeBeamFromSelect.Click += (s, e) => BtnDeleteMultipleGradeBeamsFromSelect_Click(s, e);
            BtnRegenerateAll.Click += (s, e) => BtnRegenerateAll_Click(s, e);
            BtnClearAllEdges.Click += (s, e) => BtnEraseAllGradeBeamEdges(s, e);

            BtnSelectBoundary.Click += (s, e) => BtnDefineFoundationBoundary_Click();
            BtnDeleteBoundary.Click += (s, e) => BtnDeleteBoundary_Click();

            BtnDrawGradeBeamTable.Click += (s, e) => BtnDrawGradeBeamTable_Click();

            BtnShowBoundary.Click += (s, e) => _boundaryService.HighlightBoundary(context);
            BtnZoomBoundary.Click += (s, e) => _boundaryService.ZoomToBoundary(context);

            PrelimGBControl.AddPreliminaryClicked += PrelimGBControl_AddPreliminaryClicked;
            GradeBeamSummary.ClearAllClicked += GradeBeam_DeleteAllClicked;
            GradeBeamSummary.HighlightGradeBeamslClicked += GradeBeam_HighlightGradeBeamsClicked;
            GradeBeamSummary.AddSingleGradeBeamClicked += GradeBeamSummary_AddSingleGradeBeamClicked;

            BtnNEqualSpaces.Click += BtnNEqualSpaces_Click;
            BtnConvertExisting.Click += BtnConvertToPolyline_Click;

        }

        /// <summary>
        /// Queries the NOD for a list of handles in each subdirectory.
        /// </summary>
        private void BtnQueryNOD_Click(object sender, RoutedEventArgs e)
        {
            NODScanner.InspectFoundationNOD(CurrentContext);

            TxtStatus.Text = "Displaying NOD structure.";
        }

        private void BtnDeleteBoundary_Click()
        {
            var context = CurrentContext;
            if (context?.Document == null)
                return;

            var doc = context.Document;
            var ed = doc.Editor;

            try
            {
                // --- Confirm deletion
                var pko = new PromptKeywordOptions(
                    "\nDelete selected boundary beam from NOD? This cannot be undone.")
                {
                    AllowNone = false
                };
                pko.Keywords.Add("Yes");
                pko.Keywords.Add("No");

                var confirm = ed.GetKeywords(pko);
                if (confirm.Status != PromptStatus.OK || confirm.StringResult != "Yes")
                {
                    ed.WriteMessage("\nOperation canceled.");
                    return;
                }

                // --- Delete only the NOD node
                int deleted = _boundaryService.DeleteBoundaryBeam(context);
                ed.WriteMessage("\nDeleted boundary beam node in NOD.");

                // --- Rebuild grade beam edges
                _gradeBeamService.DeleteEdgesForAllGradeBeams(context);
                _gradeBeamService.GenerateEdgesForAllGradeBeams(context);

                UpdateAll();

                TxtStatus.Text = "Boundary deleted.";
            }
            catch (Exception ex)
            {
                ed.WriteMessage("\nError deleting boundary beam from NOD structure: " + ex.Message);
                TxtStatus.Text = "Error deleting boundary beam from NOD structure: " + ex.Message;
            }
        }

        private void BtnEraseNODFully_Click()
        {
            NODCleaner.ClearFoundationNOD(CurrentContext);
            UpdateAll();

            TxtStatus.Text = "NOD fully erased.";
        }

        private void BtnDefineFoundationBoundary_Click()
        {
            var context = CurrentContext;

            if (context?.Document == null)
            {
                TxtStatus.Text = "No active document.";
                return;
            }

            var doc = context.Document;
            var ed = doc.Editor;
            var db = doc.Database;

            // Prompt user for a polyline
            var options = new PromptEntityOptions("\nSelect a polyline (will be closed if not already): ");
            options.SetRejectMessage("\nMust select a polyline.");
            options.AddAllowedClass(typeof(Polyline), false);

            var result = ed.GetEntity(options);
            if (result.Status != PromptStatus.OK)
            {
                TxtStatus.Text = "Boundary selection canceled.";
                return;
            }

            using (var lockDoc = doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pl = tr.GetObject(result.ObjectId, OpenMode.ForWrite) as Polyline;
                if (pl == null)
                {
                    TxtStatus.Text = "Selected object is not a polyline.";
                    return;
                }

                // --- Ensure polyline is closed
                if (!pl.Closed)
                {
                    pl.Closed = true;
                    ed.WriteMessage("\nPolyline was automatically closed.");
                }

                // ------------------------------------------------
                // Delete existing boundary (edges + centerline)
                // ------------------------------------------------
                int deleted = _boundaryService.DeleteBoundaryBeam(context);
                ed.WriteMessage($"\n[DEBUG] Deleted {deleted} entities from previous boundary.");

                // ------------------------------------------------
                // Create new boundary node in NOD
                // ------------------------------------------------
                string handle = pl.Handle.ToString();
                var node = NODCore.GetOrCreateBoundaryGradeBeamNode(tr, db, handle);

                tr.Commit();
            }

            // ------------------------------------------------
            // Rebuild grade beam edges because boundary changed
            // ------------------------------------------------
            int edgesDeleted = _gradeBeamService.DeleteEdgesForAllGradeBeams(context);
            ed.WriteMessage($"\n[DEBUG] Deleted {edgesDeleted} existing grade beam edges.");

            _gradeBeamService.GenerateEdgesForAllGradeBeams(context);
            ed.WriteMessage("\n[DEBUG] Rebuilt all grade beam edges.");

            UpdateAll();

            TxtStatus.Text = "Boundary defined.";

            // Enable grade beam UI
            PrelimGBControl.ViewModel.IsPreliminaryGenerated = false;
            PrelimGBControl.Visibility = System.Windows.Visibility.Visible;
        }

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
                    vertexCount: GradeBeamManager.DEFAULT_VERTEX_QTY
                );

                UpdateAll();

                TxtStatus.Text = $"Created {beams.Count} preliminary grade beams.";
            }
            catch (System.Exception ex)
            {
                TxtStatus.Text = $"Error creating grade beams: {ex.Message}";
            }
        }




















        private void BtnDrawGradeBeamTable_Click()
        {
            UpdateTables();
        }

        private void BtnDeleteMultipleGradeBeamsFromSelect_Click(object s, RoutedEventArgs e)
        {
            var context = CurrentContext;
            if (context?.Document == null)
                return;

            var doc = context.Document;
            var ed = doc.Editor;
            var db = doc.Database;

            try
            {
                // --- Prompt user to select multiple grade beam objects
                var pso = new PromptSelectionOptions
                {
                    MessageForAdding = "\nSelect grade beam objects to delete:"
                };
                var psr = ed.GetSelection(pso);
                if (psr.Status != PromptStatus.OK || psr.Value.Count == 0)
                {
                    ed.WriteMessage("\nNo objects selected.");
                    return;
                }

                var selectedIds = psr.Value.GetObjectIds();
                var uniqueHandles = _gradeBeamService.ResolveGradeBeamHandles(context, selectedIds);
                if (!uniqueHandles.Any())
                {
                    ed.WriteMessage("\nSelected objects do not belong to any grade beams.");
                    return;
                }

                // --- Confirm deletion
                var pko = new PromptKeywordOptions(
                    $"\nDelete {uniqueHandles.Count()} selected grade beam(s)? This cannot be undone.")
                {
                    AllowNone = false
                };
                pko.Keywords.Add("Yes");
                pko.Keywords.Add("No");

                var confirm = ed.GetKeywords(pko);
                if (confirm.Status != PromptStatus.OK || confirm.StringResult != "Yes")
                {
                    ed.WriteMessage("\nOperation canceled.");
                    return;
                }

                int totalBeamsDeleted = 0;

                ed.WriteMessage("\n[DEBUG] Deleting all grade beam edges...");

                // --- Delete selected beam
                foreach (var handle in uniqueHandles)
                {
                    totalBeamsDeleted += _gradeBeamService.DeleteSingleGradeBeam(context, handle);

                }
                ed.WriteMessage($"\n[DEBUG] Deleted {totalBeamsDeleted} grade beam edges.");

                // --- Delete remaining beam edges and rebuild edges for all beams
                try
                {
                    ed.WriteMessage("\n[DEBUG] Deleting all grade beam edges...");

                    // --- Delete all edges
                    int totalEdgesDeleted = _gradeBeamService.DeleteEdgesForAllGradeBeams(context);
                    ed.WriteMessage($"\n[DEBUG] Deleted {totalEdgesDeleted} grade beam edges.");

                    // --- Rebuild edges for all beams
                    _gradeBeamService.GenerateEdgesForAllGradeBeams(context);
                    ed.WriteMessage("\n[DEBUG] Regenerated all grade beam edges.");

                    Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
                }
                catch (Exception ex)
                {
                    ed.WriteMessage("\nError regenerating grade beam edges: " + ex.Message);
                }

                UpdateAll();
                //Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
            }
            catch (Exception ex)
            {
                ed.WriteMessage("\nError deleting selected grade beams: " + ex.Message);
            }
        }



        private void BtnEraseAllGradeBeamEdges(object s, RoutedEventArgs e)
        {
            var context = CurrentContext;
            if (context?.Document == null) return;

            var ed = context.Document.Editor;

            try
            {
                var pko = new PromptKeywordOptions(
                    "\nDelete all edges from ALL grade beams? This cannot be undone.")
                {
                    AllowNone = false
                };
                pko.Keywords.Add("Yes");
                pko.Keywords.Add("No");

                var confirm = ed.GetKeywords(pko);
                if (confirm.Status != PromptStatus.OK || confirm.StringResult != "Yes")
                {
                    ed.WriteMessage("\nOperation canceled.");
                    return;
                }

                // --- Call manager to delete all edges
                int totalErased = _gradeBeamService.DeleteEdgesForAllGradeBeams(context);

                ed.WriteMessage($"\nErased {totalErased} edge entities across all grade beams.");
                UpdateAll();
                //Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
            }
            catch (Exception ex)
            {
                ed.WriteMessage("\nError erasing grade beam edges: " + ex.Message);
            }
        }

        private void BtnRegenerateAll_Click(object sender, RoutedEventArgs e)
        {
            var context = CurrentContext;
            if (context?.Document == null) return;

            var doc = context.Document;
            var ed = doc.Editor;

            try
            {
                ed.WriteMessage("\n[DEBUG] Deleting all grade beam edges...");

                // --- Delete all edges
                int totalEdgesDeleted = _gradeBeamService.DeleteEdgesForAllGradeBeams(context);
                ed.WriteMessage($"\n[DEBUG] Deleted {totalEdgesDeleted} grade beam edges.");

                // --- Rebuild edges for all beams
                _gradeBeamService.GenerateEdgesForAllGradeBeams(context);
                ed.WriteMessage("\n[DEBUG] Regenerated all grade beam edges.");

                UpdateAll();
                //Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
            }
            catch (Exception ex)
            {
                ed.WriteMessage("\nError regenerating grade beam edges: " + ex.Message);
            }
        }


        private void DrawNEqualSpaces(FoundationContext context, SpacingRequest request)
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
                    Point3d start, end;
                    
                    //// Clip the line to the bounding box
                    //if (!MathHelperManager.TryClipLineToBoundingBoxExtents(basePt, dir, ext, out Point3d s, out Point3d e))
                    //    continue;
                    //var start = s;
                    //var end = e;

                    // Clip the line to the boundary polyline
                    if (!MathHelperManager.TryClipLineToPolyline(basePt, dir, boundary, out Point3d s1, out Point3d e1))
                        continue;
                    start = s1;
                    end = e1;


                    // Add the grade beam
                    _gradeBeamService.AddInterpolatedGradeBeam(context, start, end);
                }

                tr.Commit();
            }

            // Refresh the UI asynchronously
            UpdateAll();
            //Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
        }

        #region --- UI Updates ---



        private void GradeBeam_DeleteAllClicked(object sender, EventArgs e)
        {
            var context = CurrentContext;
            if (context?.Document == null)
                return;

            var doc = context.Document;
            var ed = doc.Editor;

            try
            {
                // --- Confirm deletion
                var pko = new PromptKeywordOptions(
                    $"\nDelete ALL grade beam(s)? This cannot be undone.")
                {
                    AllowNone = false
                };
                pko.Keywords.Add("Yes");
                pko.Keywords.Add("No");

                var confirm = ed.GetKeywords(pko);
                if (confirm.Status != PromptStatus.OK || confirm.StringResult != "Yes")
                {
                    ed.WriteMessage("\nOperation canceled.");
                    return;
                }

                int totalBeamsDeleted = 0;

                ed.WriteMessage("\n[DEBUG] Deleting all grade beams...");

                totalBeamsDeleted += _gradeBeamService.DeleteAllGradeBeams(context);

                ed.WriteMessage($"\n[DEBUG] Deleted {totalBeamsDeleted} grade beam edges.");

                // --- Rebuild edges
                _gradeBeamService.GenerateEdgesForAllGradeBeams(context);
                ed.WriteMessage("\n[DEBUG] Regenerated all grade beam edges.");

                // --- Refresh UI
                Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
            }
            catch (Exception ex)
            {
                ed.WriteMessage("\nError deleting all grade beams: " + ex.Message);
            }

            PrelimGBControl.ViewModel.IsPreliminaryGenerated = false;

            UpdateAll();
            //Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
        }


        private void GradeBeam_HighlightGradeBeamsClicked(object sender, EventArgs e)
        {
            var context = CurrentContext;
            _gradeBeamService.HighlightGradeBeams(context);
        }
        private void GradeBeamSummary_AddSingleGradeBeamClicked(object sender, EventArgs e)
        {
            var context = CurrentContext;
            if (context?.Document == null)
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
                        AllowNone = true,
                        AllowArbitraryInput = false
                    };

                    if (preview.Points.Count > 0)
                    {
                        opts.BasePoint = preview.Points.Last();
                        opts.UseBasePoint = true;
                    }

                    var res = ed.GetPoint(opts);

                    if (res.Status == PromptStatus.OK)
                    {
                        preview.AddPoint(res.Value);
                    }
                    else if (res.Status == PromptStatus.None) // ENTER pressed
                    {
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

                // --- Register the new grade beam
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    _gradeBeamService.RegisterGradeBeam(context, newPl, tr, appendToModelSpace: true);
                    tr.Commit();
                }

                // --- Delete all existing edges (clean slate)
                int edgesDeleted = _gradeBeamService.DeleteEdgesForAllGradeBeams(context);
                ed.WriteMessage($"\n[DEBUG] Deleted {edgesDeleted} existing grade beam edges.");

                // --- Rebuild edges for all grade beams
                _gradeBeamService.GenerateEdgesForAllGradeBeams(context);
                ed.WriteMessage("\n[DEBUG] Rebuilt all grade beam edges.");

                UpdateAll();
                //Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));

                TxtStatus.Text = "Custom grade beam added.";
                PrelimGBControl.ViewModel.IsPreliminaryGenerated = true;
                PrelimGBControl.Visibility = System.Windows.Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Error adding grade beam: {ex.Message}";
            }
        }







        private void SetBoundaryUIState(bool isValid)
        {
            StatusCircle.Fill = isValid ? Brushes.Green : Brushes.Red;

            ActionButtonsPanel.Visibility =
                isValid
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

            SetActionButtonBackgrounds(
                ActionButtonsPanel,
                isValid ? Brushes.LightGreen : Brushes.LightCoral);

            BtnZoomBoundary.IsEnabled = isValid;
            BtnShowBoundary.IsEnabled = isValid;
        }

        private void ClearBoundaryText()
        {
            TxtBoundaryStatus.Text = "No boundary selected";
            TxtBoundaryVertices.Text = "-";
            TxtBoundaryPerimeter.Text = "-";
            TxtBoundaryArea.Text = "-";
        }

        private void UpdateBoundaryDisplay()
        {
            var context = CurrentContext ?? throw new Exception("Current context is null.");

            bool isValid = false;

            using (var tr = context.Document.Database.TransactionManager.StartTransaction())
            {
                if (NODCore.TryGetBoundaryBeamRoot(tr, context.Document.Database, out var boundaryRoot))
                {
                    foreach (var (key, _) in NODCore.EnumerateDictionary(boundaryRoot))
                    {
                        if (!NODCore.TryGetObjectIdFromHandleString(
                                tr,
                                context.Document.Database,
                                key,
                                out ObjectId id))
                            continue;

                        var pl = tr.GetObject(id, OpenMode.ForRead) as Polyline;

                        if (pl == null || pl.IsErased || !pl.Closed)
                            continue;

                        isValid = true;

                        TxtBoundaryStatus.Text = "Boundary valid - " + pl.Handle.ToString();
                        TxtBoundaryVertices.Text = (pl.NumberOfVertices - 1).ToString();

                        double perimeter = 0;

                        for (int i = 0; i < pl.NumberOfVertices; i++)
                        {
                            perimeter += pl.GetPoint2dAt(i)
                                .GetDistanceTo(pl.GetPoint2dAt((i + 1) % pl.NumberOfVertices));
                        }

                        TxtBoundaryPerimeter.Text = perimeter.ToString("F2");

                        TxtBoundaryArea.Text =
                            MathHelperManager.ComputePolylineEnclosedArea(pl).ToString("F2");

                        break;
                    }
                }

                tr.Commit();
            }

            if (!isValid)
                ClearBoundaryText();

            SetBoundaryUIState(isValid);

            RefreshGradeBeamSummary();

            PrelimGBControl.Visibility =
                _gradeBeamService.HasAnyGradeBeams(CurrentContext)
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

        private void UpdateTables()
        {
            /// GRADEBEAM LENGTH TABLES
            Point3d? insert = _boundaryService.GetBoundaryUpperRight(CurrentContext);

            if (insert.HasValue)
            {
                // Move 50 units to the right (positive X)
                Point3d tableInsert = new Point3d(insert.Value.X + 75, insert.Value.Y, insert.Value.Z);

                // Use tableInsert as the insertion point for your table
                _gradeBeamService.DrawGradeBeamLengthTable(CurrentContext, tableInsert, 50);
            }
        }

        /// <summary>
        /// Helper function to update calculations, tables, measurements, and UI based on calcs
        /// </summary>
        private void UpdateAll()
        {
            Dispatcher.BeginInvoke(new Action(UpdateTreeViewUI));  // updates the tree view in UI

            //UpdateAll();
            Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
            //UpdateBoundaryDisplay();  // update the boundary disoplay in UI

            Dispatcher.BeginInvoke(new Action(UpdateTables));   // updates the drawing calculation tables

        }
        #endregion

        #region --- NOD / TreeView ---
        private void TreeViewExtensionData_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (!(e.NewValue is TreeViewItem tvi)) return;
            if (!(tvi.Tag is TreeViewManager.TreeNodeInfo nodeInfo)) return;

            var doc = CurrentContext?.Document;
            if (doc == null) return;

            // Collect all ObjectIds recursively
            var ids = GetAllEntitiesFromTreeNode(tvi);

            if (ids.Length == 0) return;

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                try
                {
                    doc.Editor.SetImpliedSelection(ids);
                    doc.Editor.UpdateScreen();
                    tr.Commit();
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    doc.Editor.WriteMessage($"\nError selecting object: {ex.Message}");
                }
            }

            UpdateTreeViewUI();
        }

        /// <summary>
        /// Recursively collects ObjectIds from a TreeViewItem and all its children
        /// </summary>
        private ObjectId[] GetAllEntitiesFromTreeNode(TreeViewItem node)
        {
            var ids = new List<ObjectId>();

            if (node.Tag is TreeViewManager.TreeNodeInfo info)
            {
                // If we have a wrapped entity, use that
                if (info.NODObject?.Entity != null)
                    ids.Add(info.NODObject.Entity.ObjectId);
                // Otherwise, fallback to ObjectId directly
                else if (info.ObjectId != ObjectId.Null)
                    ids.Add(info.ObjectId);
            }

            foreach (TreeViewItem child in node.Items)
                ids.AddRange(GetAllEntitiesFromTreeNode(child));

            return ids.ToArray();
        }




        #endregion


        #region --- UI Button Click Handlers ---




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

            try
            {
                bool convertedAny = false;

                while (true)
                {
                    // --- Prompt user for a Line or Polyline
                    var peo = new PromptEntityOptions(
                        "\nSelect a Line or Polyline to convert to GradeBeam <Enter to finish>:")
                    {
                        AllowNone = true
                    };
                    peo.SetRejectMessage("\nOnly Line or Polyline objects are allowed.");
                    peo.AddAllowedClass(typeof(Line), true);
                    peo.AddAllowedClass(typeof(Polyline), true);

                    var per = ed.GetEntity(peo);

                    if (per.Status == PromptStatus.Cancel)
                        return; // user hit ESC
                    if (per.Status != PromptStatus.OK || per.ObjectId.IsNull)
                        break; // user hit ENTER with no selection

                    // --- Check if object already belongs to a grade beam
                    var existingHandles = _gradeBeamService.ResolveGradeBeamHandles(context, new[] { per.ObjectId });
                    if (existingHandles.Any())
                    {
                        ed.WriteMessage($"\n[DEBUG] Object {per.ObjectId.Handle} already belongs to grade beam '{existingHandles.First()}'");
                        continue; // skip conversion
                    }

                    // --- Convert the object to a new grade beam
                    const int minVertexCount = 5;
                    _gradeBeamService.ConvertToGradeBeam(context, per.ObjectId, minVertexCount);
                    ed.WriteMessage($"\n[DEBUG] Converted object {per.ObjectId.Handle} to grade beam.");
                    convertedAny = true;
                }

                if (convertedAny)
                {
                    // --- Clean all existing edges before rebuilding
                    int edgesDeleted = _gradeBeamService.DeleteEdgesForAllGradeBeams(context);
                    ed.WriteMessage($"\n[DEBUG] Deleted {edgesDeleted} existing grade beam edges.");

                    // --- Regenerate edges for all grade beams
                    _gradeBeamService.GenerateEdgesForAllGradeBeams(context);
                    ed.WriteMessage("\n[DEBUG] Rebuilt grade beam edges.");

                    TxtStatus.Text = "Grade beams updated and edges regenerated.";

                    UpdateAll();
                    //Dispatcher.BeginInvoke(new Action(UpdateBoundaryDisplay));
                }
                else
                {
                    TxtStatus.Text = "No grade beams were converted.";
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
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


            DrawNEqualSpaces(CurrentContext, new SpacingRequest()
            {
                Count = spaces.Value,
                Direction = dir.Value,
                MaxSpa = (Math.Abs(length) / (spaces.Value - 1)),
                MinSpa = (Math.Abs(length) / (spaces.Value - 1)),
                Start = start.Value,
                End = end.Value

            });
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

        /// <summary>
        /// Rebuilds the TreeView based on the NOD tree and allows entity selection.
        /// </summary>
        internal void UpdateTreeViewUI()
        {

        }
    }
}
