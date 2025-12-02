using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using FoundationDetailer.UI;
using FoundationDetailer.Utilities;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

[assembly: CommandClass(typeof(FoundationDetailer.Commands.FoundationCommands))]

namespace FoundationDetailer.Commands
{
    //
    public class FoundationCommands : IExtensionApplication
    {
        private static PaletteSet _paletteSet;
        private static PaletteMain _paletteControl;

        public void Initialize()
        {
            Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage(
                "\nFoundationDetailer loaded. Run FD_SHOWPALETTE to begin.\n"
            );
        }

        public void Terminate() { }

        [CommandMethod("FD_SHOWPALETTE")]
        public void ShowPalette()
        {
            if (_paletteSet == null)
            {
                _paletteControl = new PaletteMain();

                ElementHost host = new ElementHost
                {
                    Dock = DockStyle.Fill,
                    Child = _paletteControl
                };

                _paletteSet = new PaletteSet("Foundation Detailer")
                {
                    Style = PaletteSetStyles.ShowPropertiesMenu |
                            PaletteSetStyles.ShowAutoHideButton,
                    DockEnabled = DockSides.Left | DockSides.Right
                };

                _paletteSet.Add("Main", host);
                _paletteSet.Visible = true;
            }
            else
            {
                _paletteSet.Visible = true;
            }
        }

        [CommandMethod("FD_SELECTBOUNDARY")]
        public void FD_SELECTBOUNDARY()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            PromptEntityResult res = ed.GetEntity("\nSelect boundary polyline: ");
            if (res.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nBoundary selection canceled.");
                return;
            }

            if (PolylineBoundaryManager.TrySetBoundary(res.ObjectId, out string error))
            {
                ed.WriteMessage("\nBoundary accepted, validated, and saved.");
            }
            else
            {
                ed.WriteMessage($"\nBoundary error: {error}");
            }
        }


        [CommandMethod("FD_SHOWBOUNDARY")]
        public void ShowBoundaryCommand()
        {
            PolylineBoundaryManager.HighlightBoundary();
        }


    }
}
