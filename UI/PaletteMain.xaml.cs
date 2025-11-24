using System;
using System.Windows;
using System.Windows.Controls;
using FoundationDetailer.Model;
using FoundationDetailer.Storage;
using FoundationDetailer.AutoCAD;

namespace FoundationDetailer.UI
{
    public partial class PaletteMain : UserControl
    {
        // The foundation model for this session
        public FoundationModel CurrentModel { get; set; } = new FoundationModel();

        public PaletteMain()
        {
            InitializeComponent();
            WireButtonEvents();
        }

        /// <summary>
        /// Hook all button click events to their handlers
        /// </summary>
        private void WireButtonEvents()
        {
            BtnSelectBoundary.Click += BtnSelectBoundary_Click;
            BtnAddPiers.Click += BtnAddPiers_Click;
            BtnAddGradeBeams.Click += BtnAddGradeBeams_Click;
            BtnAddRebar.Click += BtnAddRebar_Click;
            BtnAddStrands.Click += BtnAddStrands_Click;
            BtnPreview.Click += BtnPreview_Click;
            BtnClearPreview.Click += BtnClearPreview_Click;
            BtnCommit.Click += BtnCommit_Click;
            BtnSave.Click += BtnSave_Click;
            BtnLoad.Click += BtnLoad_Click;
        }

        #region --- Button Handlers ---

        private void BtnSelectBoundary_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Select boundary in AutoCAD.");
        }

        private void BtnAddPiers_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Add piers to model.");
        }

        private void BtnAddGradeBeams_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Add grade beams to model.");
        }

        private void BtnAddRebar_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Add rebar bars to model.");
        }

        private void BtnAddStrands_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Add slab/beam strands to model.");
        }

        private void BtnPreview_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PreviewManager.ShowPreview(CurrentModel);
                TxtStatus.Text = "Preview shown.";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Preview error: {ex.Message}";
            }
        }

        private void BtnClearPreview_Click(object sender, RoutedEventArgs e)
        {
            PreviewManager.ClearPreview();
            TxtStatus.Text = "Preview cleared.";
        }

        private void BtnCommit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AutoCADAdapter.CommitModelToDrawing(CurrentModel);
                TxtStatus.Text = "Model committed to DWG.";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Commit error: {ex.Message}";
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                JsonStorage.SaveModel(CurrentModel);
                TxtStatus.Text = "Model saved.";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Save error: {ex.Message}";
            }
        }

        private void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var model = JsonStorage.LoadModel();
                if (model != null)
                {
                    CurrentModel = model;
                    TxtStatus.Text = "Model loaded.";
                }
                else
                {
                    TxtStatus.Text = "No saved model found.";
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Load error: {ex.Message}";
            }
        }

        #endregion
    }
}
