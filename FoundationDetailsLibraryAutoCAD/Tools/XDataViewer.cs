using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using System;
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
        // NOD keys to check for Xrecords
        private static readonly string[] NodKeysToCheck = { "FD_BOUNDARY", "FD_GRADEBEAM" };

        [CommandMethod("ShowNodXData")]
        public static void ShowNodXData()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                MessageBox.Show("No active document.", "NOD XData Viewer", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Database db = doc.Database;

            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

                    StringBuilder display = new StringBuilder();

                    foreach (string key in NodKeysToCheck)
                    {
                        if (!nod.Contains(key))
                        {
                            display.AppendLine($"No Xrecord found with key '{key}'.");
                            display.AppendLine();
                            continue;
                        }

                        Xrecord xr = (Xrecord)tr.GetObject(nod.GetAt(key), OpenMode.ForRead);

                        TypedValue[] arr = xr.Data?.AsArray() ?? Array.Empty<TypedValue>();
                        if (arr.Length == 0)
                        {
                            display.AppendLine($"Xrecord '{key}' has no data.");
                            display.AppendLine();
                            continue;
                        }

                        display.AppendLine($"--- {key} ---");

                        foreach (TypedValue tv in arr)
                        {
                            if (tv.TypeCode == (int)DxfCode.Handle)
                            {
                                try
                                {
                                    Handle h = new Handle(Convert.ToInt64(tv.Value));
                                    ObjectId id = db.GetObjectId(false, h, 0);
                                    display.AppendLine($"Handle: {h}  ObjectId: {id}");
                                }
                                catch
                                {
                                    display.AppendLine($"Handle: {tv.Value} (unable to resolve ObjectId)");
                                }
                            }
                            else
                            {
                                display.AppendLine($"Type {tv.TypeCode}: {tv.Value}");
                            }
                        }

                        display.AppendLine();
                    }

                    MessageBox.Show(display.ToString(), "NOD XData Viewer", MessageBoxButton.OK, MessageBoxImage.Information);

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