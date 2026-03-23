using Autodesk.AutoCAD.DatabaseServices;
using FoundationDetailsLibraryAutoCAD.Data;
using System.Collections.Generic;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.NOD
{
    public static class BoundaryNOD
    {
        
        /// <summary>
        /// Enumerates the boundary beam subdictionary in the NOD tree, returning the dictionary.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="tr"></param>
        /// <returns></returns>
        internal static IEnumerable<(string Handle, DBDictionary Dict)> EnumerateBoundaryBeam(
            FoundationContext context,
            Transaction tr)
        {
            if (context == null || tr == null)
                yield break;

            var db = context.Document?.Database;
            if (db == null)
                yield break;

            // --- Get the boundary root dictionary
            if (!NODCore.TryGetBoundaryBeamRoot(tr, db, out var boundaryRoot))
                yield break;

            // --- Enumerate all entries in FD_BOUNDARY
            foreach (var (key, _) in NODCore.EnumerateDictionary(boundaryRoot))
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                // --- Use specialized function to get the boundary beam node by handle
                if (NODCore.TryGetBoundaryBeamNode(tr, db, out var beamDict))
                    yield return (key, beamDict);
            }
        }


        internal static bool TryResolveOwningBoundaryBeam(
            FoundationContext context,
            Transaction tr,
            ObjectId selectedId,
            out string boundaryHandle,
            out bool isCenterline,
            out bool isEdge)
        {
            boundaryHandle = null;
            isCenterline = false;
            isEdge = false;

            if (context == null || tr == null || selectedId.IsNull)
                return false;

            var db = context.Document?.Database;
            if (db == null)
                return false;

            // --- Get the FD_BOUNDARY root dictionary
            if (!NODCore.TryGetBoundaryBeamRoot(tr, db, out var boundaryRoot) || boundaryRoot.Count == 0)
                return false;

            // --- Boundary beam nodes are keyed by handle
            foreach (var (handle, bbDict) in EnumerateBoundaryBeam(context, tr))
            {
                if (string.IsNullOrWhiteSpace(handle) || bbDict == null)
                    continue;

                // --- Check if selected entity is the centerline
                if (NODCore.TryGetObjectIdFromHandleString(tr, db, handle, out var clId) &&
                    clId == selectedId)
                {
                    boundaryHandle = handle;
                    isCenterline = true;
                    return true;
                }

                // --- Check edges dictionary
                if (!NODCore.TryGetBeamEdges(tr, bbDict, out var edgesDict))
                    continue;

                foreach (var (edgeKey, xrecId) in NODCore.EnumerateDictionary(edgesDict))
                {
                    if (!xrecId.IsValid || xrecId.IsErased)
                        continue;

                    var xrec = tr.GetObject(xrecId, OpenMode.ForRead) as Xrecord;
                    if (xrec?.Data == null)
                        continue;

                    foreach (var tv in xrec.Data)
                    {
                        if (tv.TypeCode != (int)DxfCode.Text || !(tv.Value is string edgeHandle))
                            continue;

                        if (NODCore.TryGetObjectIdFromHandleString(tr, db, edgeHandle, out var edgeId) &&
                            edgeId == selectedId)
                        {
                            boundaryHandle = handle;
                            isEdge = true;
                            return true;
                        }
                    }
                }
            }

            return false;
        }
    }
}
