using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using FoundationDetailer.UI;
using System;
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



                // Make it floating initially
                _paletteSet.Dock = DockSides.None;

                // Force WPF control to measure itself
                _paletteControl.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                _paletteControl.Arrange(new System.Windows.Rect(_paletteControl.DesiredSize));

                // Set the palette size to match the desired WPF size
                var width = (int)_paletteControl.DesiredSize.Width;
                var height = (int)_paletteControl.DesiredSize.Height;

                // Optional: clamp to min/max
                width = Math.Max(300, Math.Min(1000, width));
                height = Math.Max(400, Math.Min(800, height));

                _paletteSet.Size = new System.Drawing.Size(width, height);
                _paletteSet.Add("Main", host);

                _paletteSet.Visible = true;
            }
            else
            {
                _paletteSet.Visible = true;
            }
        }
        
    }
}
