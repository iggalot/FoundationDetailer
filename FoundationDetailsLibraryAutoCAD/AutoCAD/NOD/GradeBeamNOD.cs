using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.NOD
{
    public static class GradeBeamNOD
    {
        public static IEnumerable<(string Handle, DBDictionary Dict)> EnumerateGradeBeams(
            FoundationContext context,
            Transaction tr)
        {
            if (context == null || tr == null)
                yield break;

            var db = context.Document.Database;
            var nod = tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead) as DBDictionary;
            if (nod == null || !nod.Contains(NODCore.ROOT))
                yield break;

            var root = tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForRead) as DBDictionary;
            if (root == null || !root.Contains(NODCore.KEY_GRADEBEAM_SUBDICT))
                yield break;

            var gradeBeamContainer = tr.GetObject(root.GetAt(NODCore.KEY_GRADEBEAM_SUBDICT), OpenMode.ForRead) as DBDictionary;
            if (gradeBeamContainer == null)
                yield break;

            foreach (DBDictionaryEntry entry in gradeBeamContainer)
            {
                if (entry.Value is ObjectId oid)
                {
                    var handleDict = tr.GetObject(oid, OpenMode.ForRead) as DBDictionary;
                    if (handleDict != null)
                        yield return (entry.Key, handleDict);
                }
            }
        }

        public static IEnumerable<ObjectId> EnumerateGradeBeamEdges(FoundationContext context, Transaction tr)
        {
            if (context == null || tr == null)
                yield break;

            // Enumerate all grade beams first
            foreach (var (handle, gbDict) in EnumerateGradeBeams(context, tr))
            {
                if (!gbDict.Contains(NODCore.KEY_EDGES_SUBDICT))
                    continue;

                var edgesDict = tr.GetObject(gbDict.GetAt(NODCore.KEY_EDGES_SUBDICT), OpenMode.ForRead) as DBDictionary;
                if (edgesDict == null)
                    continue;

                foreach (DBDictionaryEntry entry in edgesDict)
                {
                    if (entry.Value is ObjectId oid && oid.IsValid && !oid.IsNull && !oid.IsErased)
                        yield return oid;
                }
            }
        }

        public static IEnumerable<ObjectId> EnumerateAllGradeBeamCenterlines(FoundationContext context, Transaction tr)
        {
            if (context == null || tr == null)
                yield break;

            // Enumerate all grade beams first
            foreach (var (_, gbDict) in EnumerateGradeBeams(context, tr))
            {
                if (TryGetCenterline(context, tr, gbDict, out ObjectId centerlineId))
                    yield return centerlineId;
            }
        }

        /// <summary>
        /// Returns the DBDictionary for a specific grade beam handle (key), or null if not found.
        /// </summary>
        public static DBDictionary GetGradeBeamDictionaryByHandle(
            FoundationContext context,
            Transaction tr,
            string gradeBeamHandle)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (string.IsNullOrWhiteSpace(gradeBeamHandle)) return null;

            // Enumerate all grade beams
            foreach (var (handle, gbDict) in EnumerateGradeBeams(context, tr))
            {
                if (handle.Equals(gradeBeamHandle, StringComparison.OrdinalIgnoreCase))
                    return gbDict;
            }

            return null; // Not found
        }

        public static bool TryGetAllGradeBeamHandleKeys(
    FoundationContext context,
    Transaction tr,
    out List<string> handleStrings)
        {
            handleStrings = new List<string>();

            if (context == null || tr == null)
                return false;

            var db = context.Document.Database;

            var nod = (DBDictionary)
                tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

            if (!nod.Contains(NODCore.ROOT))
                return false;

            var root = (DBDictionary)
                tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForRead);

            if (!root.Contains(NODCore.KEY_GRADEBEAM_SUBDICT))
                return false;

            var gradeBeamContainer = (DBDictionary)
                tr.GetObject(root.GetAt(NODCore.KEY_GRADEBEAM_SUBDICT), OpenMode.ForRead);

            // Each entry here is a grade beam dictionary named by handle
            foreach (DBDictionaryEntry entry in gradeBeamContainer)
                handleStrings.Add(entry.Key);

            return handleStrings.Count > 0;
        }

        /// <summary>
        /// Adds a grade beam polyline handle to the EE_Foundation NOD under FD_GRADEBEAM.
        /// </summary>
        /// <param name="id">The ObjectId of the grade beam polyline.</param>
        internal static void AddGradeBeamCenterlineHandleToNOD(
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
                NODCore.KEY_GRADEBEAM_SUBDICT,
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

            // Metadata Xrecord
            NODCore.GetOrCreateMetadataXrecord(tr, handleDict, NODCore.KEY_METADATA_SUBDICT);
        }


        internal static void EraseGradeBeamEntry(
    Transaction tr,
    Database db,
    string gradeBeamHandle)
        {
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (string.IsNullOrWhiteSpace(gradeBeamHandle))
                throw new ArgumentException("Invalid grade beam handle.", nameof(gradeBeamHandle));

            gradeBeamHandle = gradeBeamHandle.Trim().ToUpperInvariant();

            DBDictionary nod =
                (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

            if (!nod.Contains(NODCore.ROOT))
                return;

            DBDictionary root =
                (DBDictionary)tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForRead);

            if (!root.Contains(NODCore.KEY_GRADEBEAM_SUBDICT))
                return;

            DBDictionary gradeBeams =
                (DBDictionary)tr.GetObject(root.GetAt(NODCore.KEY_GRADEBEAM_SUBDICT), OpenMode.ForWrite);

            if (!gradeBeams.Contains(gradeBeamHandle))
                return;

            // Erase the grade beam dictionary (children erased automatically)
            DBDictionary gbDict =
                (DBDictionary)tr.GetObject(gradeBeams.GetAt(gradeBeamHandle), OpenMode.ForWrite);

            gbDict.Erase();
        }

        // GradeBeamNOD
        public static DBDictionary GetEdgesDictionary(
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

            // Open grade beam container
            if (!root.Contains(NODCore.KEY_GRADEBEAM_SUBDICT))
            {
                ed?.WriteMessage("\n[DEBUG] GradeBeam subdictionary not found.");
                return null;
            }
            var gbRoot = (DBDictionary)tr.GetObject(root.GetAt(NODCore.KEY_GRADEBEAM_SUBDICT), OpenMode.ForRead);

            // Open the individual grade beam subdictionary
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



        public static bool TryGetCenterline(
            FoundationContext context,
            Transaction tr,
            DBDictionary gradeBeamDict,
            out ObjectId centerlineId)
        {
            centerlineId = ObjectId.Null;

            if (context == null || tr == null || gradeBeamDict == null)
                return false;

            // Use the new unified function to get only the centerline
            if (TryGetGradeBeamObjects(context, tr, gradeBeamDict, out var polylines, includeCenterline: true, includeEdges: false))
            {
                // There should typically be only one centerline
                if (polylines.Count > 0)
                {
                    centerlineId = polylines[0].ObjectId;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Retrieves grade beam polylines (centerline, edges, or both) from a grade beam dictionary.
        /// </summary>
        public static bool TryGetGradeBeamObjects(
            FoundationContext context,
            Transaction tr,
            DBDictionary gradeBeamDict,
            out List<Polyline> polylines,
            bool includeCenterline = true,
            bool includeEdges = true)
        {
            polylines = new List<Polyline>();

            if (context == null || tr == null || gradeBeamDict == null)
                return false;

            var db = context.Document.Database;

            // --- Centerline
            if (includeCenterline && gradeBeamDict.Contains(NODCore.KEY_CENTERLINE))
            {
                var xrecObj = gradeBeamDict.GetAt(NODCore.KEY_CENTERLINE);
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
            if (includeEdges && gradeBeamDict.Contains(NODCore.KEY_EDGES_SUBDICT))
            {
                var edgesDict = tr.GetObject(gradeBeamDict.GetAt(NODCore.KEY_EDGES_SUBDICT), OpenMode.ForRead) as DBDictionary;
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



        public static void StoreEdgeObjects(
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
            var gradebeamDict = NODCore.GetOrCreateNestedSubDictionary(
                tr,
                nod,
                NODCore.ROOT,
                NODCore.KEY_GRADEBEAM_SUBDICT,
                centerlineId.Handle.ToString());

            var edgesDict = NODCore.GetOrCreateNestedSubDictionary(
                tr,
                gradebeamDict,
                NODCore.KEY_EDGES_SUBDICT);

            AddEdges(edgesDict, tr, "LEFT", leftEdgeIds);
            AddEdges(edgesDict, tr, "RIGHT", rightEdgeIds);
        }

        private static void AddEdges(
            DBDictionary edgesDict,
            Transaction tr,
            string keyPrefix,
            IEnumerable<ObjectId> ids)
        {
            if (edgesDict == null || ids == null)
                return;

            // 🔓 Ensure write access
            if (!edgesDict.IsWriteEnabled)
                edgesDict.UpgradeOpen();

            // 🔢 Count existing edges with this prefix (NO LINQ CAST)
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
        public static bool HasEdgesDictionary(Transaction tr, Database db, string gradeBeamHandle)
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
                if (!root.Contains(NODCore.KEY_GRADEBEAM_SUBDICT)) return false;

                var gbRoot = (DBDictionary)tr.GetObject(root.GetAt(NODCore.KEY_GRADEBEAM_SUBDICT), OpenMode.ForRead);
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





        /// <summary>
        /// Deletes a single grade beam node and all its subdictionaries/XRecords.
        /// Only affects the NOD structure; does NOT touch AutoCAD entities.
        /// </summary>
        /// <param name="context">Current foundation context</param>
        /// <param name="centerlineHandle">Handle string of the centerline for the grade beam to delete</param>
        /// <returns>True if deletion succeeded, false otherwise</returns>
        public static bool DeleteGradeBeamNode(FoundationContext context, string centerlineHandle)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(centerlineHandle)) throw new ArgumentNullException(nameof(centerlineHandle));

            Document doc = context.Document;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    // Get the top-level NOD
                    var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

                    if (!nod.Contains(NODCore.ROOT))
                    {
                        ed.WriteMessage($"\n[GradeBeamNOD] No {NODCore.ROOT} dictionary exists.");
                        return false;
                    }

                    // Get the FD_GRADEBEAM container
                    var root = (DBDictionary)tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForWrite);
                    if (!root.Contains(NODCore.KEY_GRADEBEAM_SUBDICT))
                    {
                        ed.WriteMessage($"\n[GradeBeamNOD] No {NODCore.KEY_GRADEBEAM_SUBDICT} container exists.");
                        return false;
                    }

                    var gradeBeamContainer = (DBDictionary)tr.GetObject(root.GetAt(NODCore.KEY_GRADEBEAM_SUBDICT), OpenMode.ForWrite);

                    // Delete using NODCore helper
                    bool deleted = NODCore.DeleteNODSubDictionary(context, tr, gradeBeamContainer, centerlineHandle);

                    if (deleted)
                        tr.Commit();

                    return deleted;
                }
                catch (Exception ex)
                {
                    ed.WriteMessage($"\n[GradeBeamNOD] Failed to delete grade beam: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Recursively searches the grade beam container to find which top-level grade beam dictionary owns the handle/key.
        /// </summary>
        internal static string FindGradeBeamKeyForHandle(Transaction tr, DBDictionary gradeBeamContainer, string handleOrKey)
        {
            foreach (var (gradeBeamKey, gbId) in NODCore.EnumerateDictionary(gradeBeamContainer))
            {
                var gbDict = tr.GetObject(gbId, OpenMode.ForRead) as DBDictionary;
                if (gbDict == null) continue;

                if (string.Equals(gradeBeamKey, handleOrKey, StringComparison.OrdinalIgnoreCase))
                    return gradeBeamKey;

                if (NODCore.IsKeyOrHandleInDictionary(tr, gbDict, handleOrKey))
                    return gradeBeamKey;
            }

            return null;
        }

        public static bool TryGetEdges(
                    FoundationContext context,
                    Transaction tr,
                    DBDictionary gradeBeamDict,
                    out ObjectId[] leftEdges,
                    out ObjectId[] rightEdges)
        {
            leftEdges = Array.Empty<ObjectId>();
            rightEdges = Array.Empty<ObjectId>();

            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null || gradeBeamDict == null) return false;

            if (!NODCore.TryGetNestedSubDictionary(tr, gradeBeamDict, out DBDictionary edgesDict, NODCore.KEY_EDGES_SUBDICT))
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

                if (!NODCore.TryGetObjectIdFromHandleString(context, gradeBeamDict.Database, handleStr, out ObjectId oid))
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


    }
}
