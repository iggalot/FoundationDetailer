using FoundationDetailer.Model;
using FoundationDetailer.Storage;
using FoundationDetailer.AutoCAD;
using System.Windows;
using System.Windows.Controls;

namespace FoundationDetailer.UI
{
    public partial class PaletteMain : UserControl
    {
        public FoundationModel CurrentModel { get; set; } = new FoundationModel();

        public PaletteMain()
        {
            InitializeComponent();
            WireEvents();
        }

        private void WireEvents()
        {
            BtnSelectBoundary.Click += (s, e) => SelectBoundary();
            BtnAddPiers.Click += (s, e) => AddPiers();
            BtnAddGradeBeams.Click += (s, e) => AddGradeBeams();
            BtnAddRebar.Click += (s, e) => AddRebarBars();
            BtnAddStrands.Click += (s, e) => AddStrands();

            BtnPreview.Click += (s, e) => ShowPreview();
            BtnClearPreview.Click += (s, e) => ClearPreview();
            BtnCommit.Click += (s, e) => CommitToDrawing();

            BtnSave.Click += (s, e) => SaveModel();
            BtnLoad.Click += (s, e) => LoadModel();
        }

        private void SelectBoundary()
        {
            MessageBox.Show("TODO: implement boundary picker.");
        }

        private void AddPiers()
        {
            MessageBox.Show("TODO: add piers dialog.");
        }

        private void AddGradeBeams()
        {
            MessageBox.Show("TODO: add grade beams dialog.");
        }

        private void AddRebarBars()
        {
            MessageBox.Show("TODO: add rebar dialog.");
        }

        private void AddStrands()
        {
            MessageBox.Show("TODO: add strands dialog.");
        }

        private void ShowPreview()
        {
            PreviewManager.ShowPreview(CurrentModel);
            TxtStatus.Text = "Preview shown.";
        }

        private void ClearPreview()
        {
            PreviewManager.ClearPreview();
            TxtStatus.Text = "Preview cleared.";
        }

        private void CommitToDrawing()
        {
            AutoCADAdapter.CommitModelToDrawing(CurrentModel);
            TxtStatus.Text = "Committed to DWG.";
        }

        private void SaveModel()
        {
            JsonStorage.SaveModel(CurrentModel);
            TxtStatus.Text = "Model saved.";
        }

        private void LoadModel()
        {
            var loaded = JsonStorage.LoadModel();
            if (loaded != null)
            {
                CurrentModel = loaded;
                TxtStatus.Text = "Model loaded.";
            }
            else
            {
                TxtStatus.Text = "No saved model found.";
            }
        }
    }
}
