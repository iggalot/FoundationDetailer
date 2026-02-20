using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
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
        public static IEnumerable<(string Handle, DBDictionary Dict)> EnumerateBoundaryBeam(
            FoundationContext context,
            Transaction tr)
        {
            if (context == null || tr == null)
                yield break;

            var doc = context.Document;
            var db = doc.Database;

            DBDictionary boundaryBeamContainer = GetBoundaryBeamRoot(tr, db);
            if (boundaryBeamContainer == null)
                yield break;

            foreach (var (key, id) in NODCore.EnumerateDictionary(boundaryBeamContainer))
            {
                if (!id.IsValid || id.IsErased)
                    continue;

                DBDictionary handleDict = tr.GetObject(id, OpenMode.ForRead) as DBDictionary;
                if (handleDict != null)
                    yield return (key, handleDict);
            }
        }


        /// <summary>
        /// Adds a boundary beam polyline handle to the EE_Foundation NOD under FD_BOUNDARY.
        /// </summary>
        /// <param name="id">The ObjectId of the boundary beam polyline.</param>
        internal static void AddBoundaryBeamCenterlineHandleToNOD(
            FoundationContext context,
            ObjectId centerlineId,
            Transaction tr)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (centerlineId.IsNull || !centerlineId.IsValid) return;

            var db = context.Document.Database;

            // Use generic helper to create full structure safely
            var handleDict = NODCore.GetOrCreateNestedSubDictionary(
                tr,
                (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite),
                NODCore.ROOT,
                NODCore.KEY_BOUNDARY_SUBDICT,
                centerlineId.Handle.ToString()
            );

            // Store CENTERLINE Xrecord
            if (!handleDict.Contains(NODCore.KEY_CENTERLINE))
            {
                Xrecord xrec = new Xrecord
                {
                    Data = new ResultBuffer(
                        new TypedValue((int)DxfCode.Text, centerlineId.Handle.ToString()))
                };
                handleDict.SetAt(NODCore.KEY_CENTERLINE, xrec);
                tr.AddNewlyCreatedDBObject(xrec, true);
            }

            // Ensure edges subdictionary exists
            NODCore.GetOrCreateNestedSubDictionary(tr, handleDict, NODCore.KEY_EDGES_SUBDICT);

            // Ensure Metadata Xrecord exists
            NODCore.GetOrCreateMetadataXrecord(tr, handleDict, NODCore.KEY_METADATA_SUBDICT);
        }

        // GradeBeamNOD
        public static DBDictionary GetBoundaryEdgesDictionary(
            Transaction tr,
            Database db,
            string gradeBeamHandle,
            bool forWrite,
            Editor ed = null)  // optional editor for debug messages
        {
            // Open root dictionary
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(NODCore.ROOT))
            {
                ed?.WriteMessage("\n[DEBUG] Root dictionary not found.");
                return null;
            }
            var root = (DBDictionary)tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForRead);

            // Open boundary beam container
            if (!root.Contains(NODCore.KEY_BOUNDARY_SUBDICT))
            {
                ed?.WriteMessage("\n[DEBUG] Boundary subdictionary not found.");
                return null;
            }
            var gbRoot = (DBDictionary)tr.GetObject(root.GetAt(NODCore.KEY_BOUNDARY_SUBDICT), OpenMode.ForRead);

            // Open the boundary beam subdictionary
            if (!gbRoot.Contains(gradeBeamHandle))
            {
                ed?.WriteMessage($"\n[DEBUG] Grade beam subdictionary '{gradeBeamHandle}' not found.");
                return null;
            }
            var gbDict = (DBDictionary)tr.GetObject(
                gbRoot.GetAt(gradeBeamHandle),
                forWrite ? OpenMode.ForWrite : OpenMode.ForRead);

            // Open FD_EDGES subdictionary
            if (!gbDict.Contains(NODCore.KEY_EDGES_SUBDICT))
            {
                ed?.WriteMessage($"\n[DEBUG] FD_EDGES subdictionary not found for grade beam '{gradeBeamHandle}'.");
                return null;
            }

            var edgesDict = (DBDictionary)tr.GetObject(
                gbDict.GetAt(NODCore.KEY_EDGES_SUBDICT),
                forWrite ? OpenMode.ForWrite : OpenMode.ForRead);

            ed?.WriteMessage($"\n[DEBUG] Found FD_EDGES subdictionary for grade beam '{gradeBeamHandle}' with {edgesDict.Count} entries.");
            return edgesDict;
        }

        internal static bool TryGetBoundaryCenterline(
            FoundationContext context,
            Transaction tr,
            DBDictionary boundaryBeamDict,
            out ObjectId centerlineId)
        {
            centerlineId = ObjectId.Null;

            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (boundaryBeamDict == null) return false;

            if (!boundaryBeamDict.Contains(NODCore.KEY_CENTERLINE))
                return false;

            var xrec = tr.GetObject(
                boundaryBeamDict.GetAt(NODCore.KEY_CENTERLINE),
                OpenMode.ForRead) as Xrecord;

            if (xrec?.Data == null)
                return false;

            foreach (TypedValue tv in xrec.Data)
            {
                if (tv.TypeCode != (int)DxfCode.Text)
                    continue;

                if (!NODCore.TryGetObjectIdFromHandleString(
                    context,
                    context.Document.Database,
                    tv.Value as string,
                    out ObjectId oid))
                    continue;

                if (!oid.IsValid || oid.IsErased)
                    continue;

                centerlineId = oid;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Retrieves grade beam polylines (centerline, edges, or both) from a grade beam dictionary.
        /// </summary>
        public static bool TryGetBoundaryBeamObjects(
            FoundationContext context,
            Transaction tr,
            DBDictionary boundaryBeamDict,
            out List<Polyline> polylines,
            bool includeCenterline = true,
            bool includeEdges = true)
        {
            polylines = new List<Polyline>();

            if (context == null || tr == null || boundaryBeamDict == null)
                return false;

            var db = context.Document.Database;

            // --- Centerline
            if (includeCenterline && boundaryBeamDict.Contains(NODCore.KEY_CENTERLINE))
            {
                var xrecObj = boundaryBeamDict.GetAt(NODCore.KEY_CENTERLINE);
                if (!xrecObj.IsNull && !xrecObj.IsErased)
                {
                    Xrecord xrec = null;
                    try
                    {
                        xrec = tr.GetObject(xrecObj, OpenMode.ForRead) as Xrecord;
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { }

                    if (xrec?.Data != null)
                    {
                        foreach (TypedValue tv in xrec.Data)
                        {
                            if (tv.TypeCode != (int)DxfCode.Text) continue;

                            string handleStr = tv.Value as string;
                            if (string.IsNullOrWhiteSpace(handleStr)) continue;

                            if (!NODCore.TryGetObjectIdFromHandleString(context, db, handleStr, out var oid))
                                continue;

                            if (oid.IsNull || !oid.IsValid || oid.IsErased) continue;

                            var obj = tr.GetObject(oid, OpenMode.ForRead, false) as Polyline;
                            if (obj != null) polylines.Add(obj);
                        }
                    }
                }
            }

            // --- Edges
            if (includeEdges && boundaryBeamDict.Contains(NODCore.KEY_EDGES_SUBDICT))
            {
                var edgesDict = tr.GetObject(boundaryBeamDict.GetAt(NODCore.KEY_EDGES_SUBDICT), OpenMode.ForRead) as DBDictionary;
                if (edgesDict != null)
                {
                    foreach (DBDictionaryEntry entry in edgesDict)
                    {
                        if (entry.Value is ObjectId oid && oid.IsValid && !oid.IsNull && !oid.IsErased)
                        {
                            var obj = tr.GetObject(oid, OpenMode.ForRead, false) as Polyline;
                            if (obj != null) polylines.Add(obj);
                        }
                    }
                }
            }

            return polylines.Count > 0;
        }



        public static void StoreBoundaryEdgeObjects(
            FoundationContext context,
            Transaction tr,
            ObjectId centerlineId,
            IEnumerable<ObjectId> leftEdgeIds,
            IEnumerable<ObjectId> rightEdgeIds)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (centerlineId.IsNull) throw new ArgumentNullException(nameof(centerlineId));

            var db = context.Document.Database;
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

            // --- Get or create GradeBeam dictionary (safe here because we are NOT enumerating)
            var boundarybeamDict = NODCore.GetOrCreateNestedSubDictionary(
                tr,
                nod,
                NODCore.ROOT,
                NODCore.KEY_BOUNDARY_SUBDICT,
                centerlineId.Handle.ToString());

            var edgesDict = NODCore.GetOrCreateNestedSubDictionary(
                tr,
                boundarybeamDict,
                NODCore.KEY_EDGES_SUBDICT);

            AddBoundaryEdges(edgesDict, tr, "LEFT", leftEdgeIds);
            AddBoundaryEdges(edgesDict, tr, "RIGHT", rightEdgeIds);
        }

        private static void AddBoundaryEdges(
            DBDictionary edgesDict,
            Transaction tr,
            string keyPrefix,
            IEnumerable<ObjectId> ids)
        {
            if (edgesDict == null || ids == null)
                return;

            // Ensure write access
            if (!edgesDict.IsWriteEnabled)
                edgesDict.UpgradeOpen();

            // Count existing edges with this prefix (NO LINQ CAST)
            int counter = 0;
            foreach (DBDictionaryEntry entry in edgesDict)
            {
                if (entry.Key.StartsWith(keyPrefix))
                    counter++;
            }

            foreach (var id in ids)
            {
                if (id.IsNull)
                    continue;

                string key = $"{keyPrefix}_{counter++}";

                var xrec = new Xrecord
                {
                    Data = new ResultBuffer(
                        new TypedValue((int)DxfCode.Text, id.Handle.ToString()))
                };

                edgesDict.SetAt(key, xrec);
                tr.AddNewlyCreatedDBObject(xrec, true);
            }
        }


        /// <summary>
        /// Returns true if the specified grade beam has an edges dictionary in the NOD tree.
        /// </summary>
        public static bool HasBoundaryEdgesDictionary(Transaction tr, Database db, string gradeBeamHandle)
        {
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (string.IsNullOrWhiteSpace(gradeBeamHandle)) return false;

            try
            {
                // Open the root dictionary
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                if (!nod.Contains(NODCore.ROOT)) return false;

                var root = (DBDictionary)tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForRead);
                if (!root.Contains(NODCore.KEY_BOUNDARY_SUBDICT)) return false;

                var gbRoot = (DBDictionary)tr.GetObject(root.GetAt(NODCore.KEY_BOUNDARY_SUBDICT), OpenMode.ForRead);
                if (!gbRoot.Contains(gradeBeamHandle)) return false;

                var gbDict = (DBDictionary)tr.GetObject(gbRoot.GetAt(gradeBeamHandle), OpenMode.ForRead);

                // Return true if the edges sub-dictionary exists
                return gbDict.Contains(NODCore.KEY_EDGES_SUBDICT);
            }
            catch
            {
                return false;
            }
        }

        public static bool TryGetBoundaryEdges(
                    FoundationContext context,
                    Transaction tr,
                    DBDictionary boundaryBeamDict,
                    out ObjectId[] leftEdges,
                    out ObjectId[] rightEdges)
        {
            leftEdges = Array.Empty<ObjectId>();
            rightEdges = Array.Empty<ObjectId>();

            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null || boundaryBeamDict == null) return false;

            if (!NODCore.TryGetNestedSubDictionary(tr, boundaryBeamDict, out DBDictionary edgesDict, NODCore.KEY_EDGES_SUBDICT))
                return false;

            var leftList = new List<ObjectId>();
            var rightList = new List<ObjectId>();

            foreach (DBDictionaryEntry entry in edgesDict)
            {
                if (entry.Value.IsNull || entry.Value.IsErased) continue;

                // Get the Xrecord
                Xrecord xrec = tr.GetObject(entry.Value, OpenMode.ForRead) as Xrecord;
                if (xrec?.Data == null) continue;

                TypedValue[] tvs = xrec.Data.AsArray();
                if (tvs.Length == 0) continue;

                // Expecting a single TypedValue containing the handle string
                string handleStr = tvs[0].Value as string;
                if (string.IsNullOrWhiteSpace(handleStr)) continue;

                if (!NODCore.TryGetObjectIdFromHandleString(context, boundaryBeamDict.Database, handleStr, out ObjectId oid))
                    continue;

                if (oid.IsNull || oid.IsErased) continue;

                if (entry.Key.StartsWith("LEFT_", StringComparison.OrdinalIgnoreCase))
                    leftList.Add(oid);
                else if (entry.Key.StartsWith("RIGHT_", StringComparison.OrdinalIgnoreCase))
                    rightList.Add(oid);
            }

            leftEdges = leftList.ToArray();
            rightEdges = rightList.ToArray();

            return leftEdges.Length > 0 || rightEdges.Length > 0;
        }

        internal static bool TryResolveOwningBoundaryBeam(
            FoundationContext context,
            Transaction tr,
            ObjectId selectedId,
            out string boundaryBeamHandle,
            out bool isCenterline,
            out bool isEdge)
        {
            boundaryBeamHandle = null;
            isCenterline = false;
            isEdge = false;

            foreach (var (handle, gbDict) in EnumerateBoundaryBeam(context, tr))
            {
                // --- Check centerline
                if (TryGetBoundaryCenterline(context, tr, gbDict, out ObjectId clId) &&
                    !clId.IsNull &&
                    clId == selectedId)
                {
                    boundaryBeamHandle = handle;
                    isCenterline = true;
                    return true;
                }

                // --- Check edges
                if (!HasBoundaryEdgesDictionary(tr, context.Document.Database, handle))
                    continue;

                var edgesDict = GetBoundaryEdgesDictionary(
                    tr,
                    context.Document.Database,
                    handle,
                    forWrite: false);

                foreach (var (_, xrecId) in NODCore.EnumerateDictionary(edgesDict))
                {
                    if (xrecId.IsNull || xrecId.IsErased)
                        continue;

                    var xrec = tr.GetObject(xrecId, OpenMode.ForRead) as Xrecord;
                    if (xrec?.Data == null)
                        continue;

                    foreach (TypedValue tv in xrec.Data)
                    {
                        if (tv.TypeCode != (int)DxfCode.Text)
                            continue;

                        if (!NODCore.TryGetObjectIdFromHandleString(
                                context,
                                context.Document.Database,
                                tv.Value as string,
                                out ObjectId edgeId))
                            continue;

                        if (edgeId == selectedId)
                        {
                            boundaryBeamHandle = handle;
                            isEdge = true;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the top-level boundary beam container dictionary (FD_GRADEBEAM subdictionary) from the NOD.
        /// Returns null if it does not exist.
        /// </summary>
        public static DBDictionary GetBoundaryBeamRoot(Transaction tr, Database db, bool forWrite = false)
        {
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (db == null) throw new ArgumentNullException(nameof(db));

            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(NODCore.ROOT))
                return null;

            var root = (DBDictionary)tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForRead);
            if (!root.Contains(NODCore.KEY_BOUNDARY_SUBDICT))
                return null;

            return (DBDictionary)tr.GetObject(
                root.GetAt(NODCore.KEY_BOUNDARY_SUBDICT),
                forWrite ? OpenMode.ForWrite : OpenMode.ForRead);
        }


        internal static int DeleteBeamFull(
            FoundationContext context,
            Transaction tr,
            string handle)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (string.IsNullOrWhiteSpace(handle)) return 0;

            int deleted = 0;

            var gbRoot = GetBoundaryBeamRoot(tr, context.Document.Database, true);
            if (gbRoot == null || !gbRoot.Contains(handle))
                return 0;

            var gbDict = (DBDictionary)tr.GetObject(
                gbRoot.GetAt(handle),
                OpenMode.ForWrite);

            // 1️ Delete edges
            deleted += DeleteBoundaryBeamEdgesOnly(context, tr, gbDict);

            // 2️ Delete centerline
            if (TryGetBoundaryCenterline(context, tr, gbDict, out ObjectId clId))
            {
                var ent = tr.GetObject(clId, OpenMode.ForWrite) as Entity;
                if (ent != null && !ent.IsErased)
                {
                    ent.Erase();
                    deleted++;
                }
            }

            // 3️ Remove beam node
            gbRoot.Remove(handle);
            gbDict.Erase();

            return deleted;
        }


        internal static int DeleteBoundaryBeamEdgesOnly(
            FoundationContext context,
            Transaction tr,
            DBDictionary boundaryBeamDict)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (boundaryBeamDict == null) return 0;

            int deleted = 0;

            if (!NODCore.TryGetNestedSubDictionary(
                tr,
                boundaryBeamDict,
                out DBDictionary edgesDict,
                NODCore.KEY_EDGES_SUBDICT))
                return 0;

            var keys = new List<string>();
            foreach (DBDictionaryEntry entry in edgesDict)
                keys.Add(entry.Key);

            foreach (var key in keys)
            {
                var xrec = tr.GetObject(
                    edgesDict.GetAt(key),
                    OpenMode.ForWrite) as Xrecord;

                if (xrec?.Data != null)
                {
                    foreach (TypedValue tv in xrec.Data)
                    {
                        if (tv.TypeCode != (int)DxfCode.Text)
                            continue;

                        if (!NODCore.TryGetObjectIdFromHandleString(
                            context,
                            context.Document.Database,
                            tv.Value as string,
                            out ObjectId oid))
                            continue;

                        if (!oid.IsValid || oid.IsErased)
                            continue;

                        var ent = tr.GetObject(oid, OpenMode.ForWrite) as Entity;
                        ent?.Erase();
                        deleted++;
                    }
                }

                edgesDict.Remove(key);
                xrec?.Erase();
            }

            return deleted;

        }

        internal static DBDictionary GetBoundaryBeamDictionaryByHandle(
    FoundationContext context,
    Transaction tr,
    string handle)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (string.IsNullOrWhiteSpace(handle)) return null;

            var root = GetBoundaryBeamRoot(tr, context.Document.Database, false);
            if (root == null) return null;

            if (!root.Contains(handle))
                return null;

            return tr.GetObject(root.GetAt(handle), OpenMode.ForRead) as DBDictionary;
        }
    }
}
