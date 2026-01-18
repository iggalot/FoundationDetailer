using Autodesk.AutoCAD.DatabaseServices;
using FoundationDetailsLibraryAutoCAD.AutoCAD.NOD;
using FoundationDetailsLibraryAutoCAD.Managers;

internal sealed class PolylineTreeItemViewModel
{
    public string Handle { get; }
    public double TotalLength { get; }

    public PolylineTreeItemViewModel(NODObjectWrapper nodObject, Transaction tr)
    {
        if (nodObject == null || tr == null)
        {
            Handle = "(null)";
            TotalLength = 0.0;
            return;
        }

        Polyline pl = null;

        // Check if the wrapper has an Entity and it's a Polyline
        if (nodObject.Entity is Polyline poly)
        {
            pl = poly;
        }
        else if (!nodObject.Entity?.IsErased ?? false)
        {
            // Try to get the entity from ObjectId if it exists
            if (!nodObject.Original?.ObjectId.IsNull ?? false)
            {
                try
                {
                    pl = tr.GetObject(nodObject.Original.ObjectId, OpenMode.ForRead) as Polyline;
                }
                catch
                {
                    pl = null;
                }
            }
        }

        if (pl != null)
        {
            Handle = pl.Handle.ToString();
            TotalLength = MathHelperManager.ComputePolylineLength(pl);
        }
        else
        {
            Handle = "(not a Polyline)";
            TotalLength = 0.0;
        }
    }
}

