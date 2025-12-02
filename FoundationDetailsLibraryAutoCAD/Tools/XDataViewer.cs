using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using System.Text;
using System.Windows; // WPF MessageBox

[assembly: CommandClass(typeof(FoundationDetailer.AutoCAD.XDataViewerMulti))]

namespace FoundationDetailer.AutoCAD
{
    public class XDataViewerMulti
    {
        [CommandMethod("ShowXDataMulti")]
        public static void ShowXDataMulti()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                // Prompt user to select an entity
                PromptEntityOptions peo = new PromptEntityOptions("\nSelect an object: ");
                PromptEntityResult per = ed.GetEntity(peo);

                if (per.Status != PromptStatus.OK)
                    return;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Entity ent = tr.GetObject(per.ObjectId, OpenMode.ForRead) as Entity;

                    if (ent == null)
                    {
                        MessageBox.Show("Selected object is not a valid entity.", "XData Viewer", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    ResultBuffer xdata = ent.XData;

                    if (xdata == null)
                    {
                        MessageBox.Show("No XData found on this object.", "XData Viewer", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    TypedValue[] arr = xdata.AsArray();
                    StringBuilder display = new StringBuilder();

                    int i = 0;
                    while (i < arr.Length)
                    {
                        if (arr[i].TypeCode == (int)DxfCode.ExtendedDataRegAppName)
                        {
                            string regApp = arr[i].Value?.ToString() ?? "";
                            display.AppendLine($"RegApp: {regApp}");

                            // Collect all subsequent ASCII strings until next RegApp
                            i++;
                            while (i < arr.Length && arr[i].TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                            {
                                display.AppendLine($"  Value: {arr[i].Value}");
                                i++;
                            }
                        }
                        else
                        {
                            i++;
                        }
                    }

                    if (display.Length == 0)
                        display.AppendLine("No readable XData found.");

                    MessageBox.Show(display.ToString(), "XData Viewer", MessageBoxButton.OK, MessageBoxImage.Information);

                    tr.Commit();
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "XData Viewer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public class NodXDataViewer
    {
        private const string XrecordKey = "FD_BOUNDARY"; // or FD_GRADEBEAM depending on what you want

        [CommandMethod("ShowNodXData")]
        public static void ShowNodXData()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // Open Named Objects Dictionary
                    DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

                    if (!nod.Contains(XrecordKey))
                    {
                        MessageBox.Show($"No NOD Xrecord found with key '{XrecordKey}'.", "NOD XData Viewer", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    Xrecord xr = (Xrecord)tr.GetObject(nod.GetAt(XrecordKey), OpenMode.ForRead);

                    if (xr.Data == null)
                    {
                        MessageBox.Show("Xrecord has no data.", "NOD XData Viewer", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    TypedValue[] arr = xr.Data.AsArray();
                    StringBuilder display = new StringBuilder();

                    foreach (var tv in arr)
                    {
                        if (tv.TypeCode == (int)DxfCode.Handle)
                        {
                            string handleStr = tv.Value?.ToString() ?? "";
                            display.AppendLine($"Handle: {handleStr}");
                        }
                        else
                        {
                            display.AppendLine($"Type {tv.TypeCode}: {tv.Value}");
                        }
                    }

                    MessageBox.Show(display.ToString(), $"NOD XData Viewer - {XrecordKey}", MessageBoxButton.OK, MessageBoxImage.Information);

                    tr.Commit();
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "NOD XData Viewer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
