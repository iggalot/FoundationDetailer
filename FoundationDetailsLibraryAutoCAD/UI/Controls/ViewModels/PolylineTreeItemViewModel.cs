using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using FoundationDetailsLibraryAutoCAD.Managers;
using System;

internal sealed class PolylineTreeItemViewModel
{
    public string Handle { get; }
    public double TotalLength { get; }

    public PolylineTreeItemViewModel(Polyline pl)
    {
        Handle = pl.Handle.ToString();
        TotalLength = MathHelperManager.ComputePolylineLength(pl);
    }


}
