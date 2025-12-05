using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
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
                                    Handle h;

                                    // Robust handle resolution: cover all common Xrecord handle types
                                    switch (tv.Value)
                                    {
                                        case Handle handleObj:
                                            h = handleObj;
                                            break;

                                        case string s:
                                            // Hex string
                                            h = new Handle(Convert.ToInt64(s, 16));
                                            break;

                                        case int i:
                                            h = new Handle(i);
                                            break;

                                        case long l:
                                            h = new Handle(l);
                                            break;

                                        case short sh:
                                            h = new Handle(sh);
                                            break;

                                        default:
                                            display.AppendLine($"Unsupported handle format: {tv.Value} ({tv.Value.GetType()})");
                                            continue;
                                    }

                                    // Resolve ObjectId
                                    ObjectId id = db.GetObjectId(false, h, 0);
                                    display.AppendLine($"Handle: {h}  ObjectId: {id}");

                                    // Optional: inspect entity type and polyline vertices
                                    Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                                    if (ent != null)
                                    {
                                        display.AppendLine($"  Entity type: {ent.GetType().Name}");
                                        if (ent is Polyline pl)
                                        {
                                            display.AppendLine($"  Polyline vertices: {pl.NumberOfVertices}");
                                        }
                                    }
                                }
                                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                                {
                                    display.AppendLine($"Handle: {tv.Value} (unable to resolve ObjectId) - {ex.Message}");
                                }
                                catch (System.Exception ex)
                                {
                                    display.AppendLine($"Handle: {tv.Value} (unexpected error) - {ex.Message}");
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
            catch (System.Exception ex)
            {
                MessageBox.Show($"Unexpected error: {ex.Message}", "NOD XData Viewer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        [CommandMethod("CleanNodHandlesSafe")]
        public static void CleanNodHandlesSafe()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                MessageBox.Show("No active document.", "NOD Cleanup", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Database db = doc.Database;

            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // Open NOD for read
                    DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

                    int removedHandles = 0;
                    int removedXrecords = 0;

                    string[] keysToCheck = { "FD_BOUNDARY", "FD_GRADEBEAM" };

                    foreach (string key in keysToCheck)
                    {
                        if (!nod.Contains(key))
                            continue;

                        // Open Xrecord for write
                        Xrecord xr = (Xrecord)tr.GetObject(nod.GetAt(key), OpenMode.ForWrite);
                        TypedValue[] arr = xr.Data?.AsArray() ?? new TypedValue[0];

                        List<TypedValue> validHandles = new List<TypedValue>();

                        for (int i = 0; i < arr.Length; i++)
                        {
                            TypedValue tv = arr[i];
                            if (tv.TypeCode != (int)DxfCode.Handle)
                                continue;

                            Handle h;
                            try
                            {
                                if (tv.Value is string s)
                                    h = new Handle(Convert.ToInt64(s, 16));
                                else if (tv.Value is int ii)
                                    h = new Handle(ii);
                                else if (tv.Value is long ll)
                                    h = new Handle(ll);
                                else if (tv.Value is Handle hh)
                                    h = hh;
                                else
                                    continue;
                            }
                            catch
                            {
                                removedHandles++;
                                continue;
                            }

                            try
                            {
                                ObjectId id = db.GetObjectId(false, h, 0);
                                if (!id.IsErased)
                                {
                                    validHandles.Add(new TypedValue((int)DxfCode.Handle, h.ToString()));
                                }
                                else
                                {
                                    removedHandles++;
                                }
                            }
                            catch
                            {
                                removedHandles++;
                            }
                        }

                        if (validHandles.Count > 0)
                        {
                            xr.Data = new ResultBuffer(validHandles.ToArray());
                        }
                        else
                        {
                            // Only upgrade NOD when actually removing an Xrecord
                            if (!nod.IsWriteEnabled)
                                nod.UpgradeOpen();

                            nod.Remove(key);
                            removedXrecords++;
                        }
                    }

                    tr.Commit();

                    MessageBox.Show(
                        string.Format("NOD cleanup complete.\nRemoved handles: {0}\nRemoved Xrecords: {1}", removedHandles, removedXrecords),
                        "NOD Cleanup", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("Error during NOD cleanup: " + ex.Message, "NOD Cleanup", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }



    }
}