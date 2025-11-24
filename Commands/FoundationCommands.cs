using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using FoundationDetailer.AutoCAD;
using FoundationDetailer.UI;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

[assembly: CommandClass(typeof(FoundationDetailer.Commands.FoundationCommands))]

namespace FoundationDetailer.Commands
{
    public class FoundationCommands : IExtensionApplication
    {
        private PaletteSet _paletteSet;
        private PaletteMain _paletteControl;

        public void Initialize()
        {
            // Auto-load palette if you want
            Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nFoundationDetailer loaded.\n");
        }

        public void Terminate()
        {
        }

        [CommandMethod("FD_SHOWPALETTE")]
        public void ShowPalette()
        {
            // Only create palette once
            if (_paletteSet == null)
            {
                _paletteControl = new PaletteMain();

                // Wrap WPF UserControl in ElementHost for AutoCAD PaletteSet
                ElementHost host = new ElementHost
                {
                    Dock = DockStyle.Fill,
                    Child = _paletteControl
                };

                _paletteSet = new PaletteSet("Foundation Detailer")
                {
                    Style = PaletteSetStyles.ShowPropertiesMenu | PaletteSetStyles.ShowAutoHideButton,
                    DockEnabled = DockSides.Left | DockSides.Right
                };

                _paletteSet.Add("Main", host);
                _paletteSet.Visible = true;
            }
            else
            {
                // If already exists, just show it
                _paletteSet.Visible = true;
            }
        }

        [CommandMethod("FD_COMMITFOUNDATION")]
        public void CommitFoundation()
        {
            var model = _paletteControl?.CurrentModel;
            if (model == null)
            {
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nNo foundation model loaded.\n");
                return;
            }

            try
            {
                AutoCADAdapter.CommitModelToDrawing(model);
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nFoundation committed to drawing.\n");
            }
            catch (System.Exception ex)
            {
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\nCommit error: {ex.Message}\n");
            }
        }

        [CommandMethod("FD_SAVEFOUNDATION")]
        public void SaveFoundation()
        {
            var model = _paletteControl?.CurrentModel;
            if (model == null) return;

            try
            {
                FoundationDetailer.Storage.JsonStorage.SaveModel(model);
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nModel saved.\n");
            }
            catch (System.Exception ex)
            {
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\nSave error: {ex.Message}\n");
            }
        }

        [CommandMethod("FD_LOADFOUNDATION")]
        public void LoadFoundation()
        {
            try
            {
                var model = FoundationDetailer.Storage.JsonStorage.LoadModel();
                if (model != null && _paletteControl != null)
                {
                    _paletteControl.CurrentModel = model;
                    Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nModel loaded.\n");
                }
                else
                {
                    Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nNo saved model found.\n");
                }
            }
            catch (System.Exception ex)
            {
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\nLoad error: {ex.Message}\n");
            }
        }
    }
}
