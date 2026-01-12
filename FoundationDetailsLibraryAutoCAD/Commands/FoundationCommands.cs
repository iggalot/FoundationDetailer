using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using FoundationDetailsLibraryAutoCAD.AutoCAD.NOD;
using FoundationDetailsLibraryAutoCAD.Data;
using FoundationDetailsLibraryAutoCAD.UI;
using System;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

[assembly: CommandClass(typeof(FoundationDetailsLibraryAutoCAD.Commands.FoundationCommands))]

namespace FoundationDetailsLibraryAutoCAD.Commands
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

        [CommandMethod("DeleteFoundationEntities")]
        public void DeleteFoundationEntitiesCommand(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var doc = context.Document;
            var model = context.Model;

            if (doc == null) return;

            var db = doc.Database;
            var ed = doc.Editor;

            PromptStringOptions pso =
                new PromptStringOptions("\nEnter foundation sub-dictionary:");
            pso.AllowSpaces = false;

            var res = ed.GetString(pso);
            if (res.Status != PromptStatus.OK)
                return;

            string sub = res.StringResult.Trim().ToUpperInvariant();

            int count = NODCore.DeleteEntitiesFromFoundationSubDictionary(
                context,
                doc.Database,
                sub,
                removeHandlesFromNod: true);

            ed.WriteMessage($"\nDeleted {count} entities from {sub}.");
        }

        [CommandMethod("RemoveNODRecordManual")]
        public void RemoveNODRecordManual(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            if (doc == null) return;

            var db = doc.Database;
            var ed = doc.Editor;

            // Prompt for sub-dictionary name
            var psoSub = new PromptStringOptions("\nEnter sub-dictionary name:")
            {
                AllowSpaces = false
            };
            var resSub = ed.GetString(psoSub);
            if (resSub.Status != PromptStatus.OK) return;

            string subDictName = resSub.StringResult.Trim().ToUpperInvariant();

            // Validate against known sub-dictionaries dynamically
            if (Array.IndexOf(NODCore.KNOWN_SUBDIRS, subDictName) < 0)
            {
                ed.WriteMessage("\nInvalid sub-dictionary. Must be one of: " + string.Join(", ", NODCore.KNOWN_SUBDIRS));
                return;
            }

            // Prompt for handle to remove
            var psoHandle = new PromptStringOptions("\nEnter handle to remove:")
            {
                AllowSpaces = false
            };
            var resHandle = ed.GetString(psoHandle);
            if (resHandle.Status != PromptStatus.OK) return;

            string handleStr = resHandle.StringResult.Trim();
            if (!NODCore.TryParseHandle(context, handleStr, out Handle handle))
            {
                ed.WriteMessage("\nInvalid handle string.");
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    // Use nested helper to get the subdictionary under the root
                    DBDictionary subDict = NODCore.GetOrCreateNestedSubDictionary(
                        tr,
                        (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite),
                        NODCore.ROOT,
                        subDictName
                    );

                    if (subDict == null || !subDict.Contains(handleStr))
                    {
                        ed.WriteMessage($"\nHandle {handleStr} not found in sub-dictionary {subDictName}.");
                        return;
                    }

                    // Erase the Xrecord associated with the handle
                    Xrecord xr = (Xrecord)tr.GetObject(subDict.GetAt(handleStr), OpenMode.ForWrite);
                    xr.Erase();

                    tr.Commit();
                    ed.WriteMessage($"\nHandle {handleStr} successfully removed from {subDictName}.");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nTransaction failed: {ex.Message}");
                }
            }
        }

        [CommandMethod("ClearFoundationSubDict")]
        public static void ClearFoundationSubDictCommand()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptStringOptions pso =
                new PromptStringOptions("\nEnter sub-dictionary to clear:");
            pso.AllowSpaces = false;

            PromptResult res = ed.GetString(pso);
            if (res.Status != PromptStatus.OK)
                return;

            string subName = res.StringResult.Trim().ToUpperInvariant();

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                if (NODCore.ClearFoundationSubDictionaryInternal(tr, db, subName))
                {
                    ed.WriteMessage($"\nSubdictionary {subName} cleared.");
                }
                else
                {
                    ed.WriteMessage($"\nSubdictionary {subName} not found.");
                }

                tr.Commit();
            }
        }


    }
}
