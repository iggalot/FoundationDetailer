using Autodesk.AutoCAD.DatabaseServices;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Collections.Generic;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.NOD
{
    public static class GradeBeamInteriorNOD
    {
        public static IEnumerable<(string Handle, DBDictionary Dict)> EnumerateInteriorGradeBeams(
            FoundationContext context,
            Transaction tr)
        {
            if (context == null || tr == null)
            {
                System.Diagnostics.Debug.WriteLine("[DEBUG] Context or transaction is null.");
                yield break;
            }

            var db = context.Document.Database;

            // --- Get the grade beam root dictionary safely
            if (!NODCore.TryGetGradeBeamInteriorRoot(tr, db, out var gradeBeamRoot))
            {
                System.Diagnostics.Debug.WriteLine("[DEBUG] GradeBeam root dictionary not found.");
                yield break;
            }

            foreach (var (handle, dictId) in NODCore.EnumerateDictionary(gradeBeamRoot))
            {
                string status = $"Handle={handle} | ";

                if (dictId.IsNull)
                    status += "dictId.IsNull ";
                if (!dictId.IsValid)
                    status += "dictId.IsValid=false ";
                if (dictId.IsErased)
                    status += "dictId.IsErased ";

                System.Diagnostics.Debug.WriteLine("[DEBUG] GradeBeam entry: " + status);

                var gbDict = tr.GetObject(dictId, OpenMode.ForRead, false) as DBDictionary;

                if (gbDict != null)
                {
                    System.Diagnostics.Debug.WriteLine($"  Subdictionary '{handle}' found, count={gbDict.Count}");
                    yield return (handle, gbDict);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"  Subdictionary '{handle}' could not be opened (null).");
                }
            }
        }

        /// <summary>
        /// Stores left and right edge handles under a grade beam node.
        /// </summary>
        public static void StoreEdgeObjects(
            FoundationContext context,
            Transaction tr,
            Database db,
            string gradeBeamHandle,
            IEnumerable<ObjectId> leftEdgeIds,
            IEnumerable<ObjectId> rightEdgeIds)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (string.IsNullOrWhiteSpace(gradeBeamHandle)) throw new ArgumentException("Grade beam handle is required.", nameof(gradeBeamHandle));

            // --- Get or create the grade beam node
            var gbNode = NODCore.GetOrCreateInteriorGradeBeamNode(tr, db, gradeBeamHandle);

            // --- Get or create the edges subdictionary
            if (!NODCore.TryGetBeamEdges(tr, gbNode, out var edgesDict))
            {
                edgesDict = NODCore.GetOrCreateNestedSubDictionary(tr, gbNode, NODCore.KEY_EDGES_SUBDICT);
            }

            // --- Add or overwrite LEFT and RIGHT edges
            AddEdges(edgesDict, tr, "LEFT", leftEdgeIds);
            AddEdges(edgesDict, tr, "RIGHT", rightEdgeIds);
        }

        /// <summary>
        /// Helper to add edges to the FD_EDGES dictionary.
        /// Uses Xrecords storing handle strings.
        /// </summary>
        private static void AddEdges(
            DBDictionary edgesDict,
            Transaction tr,
            string keyPrefix,
            IEnumerable<ObjectId> ids)
        {
            if (edgesDict == null || ids == null)
                return;

            if (!edgesDict.IsWriteEnabled)
                edgesDict.UpgradeOpen();

            // --- Remove existing edges with the same prefix and handle
            var handlesToAdd = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in ids)
            {
                if (!id.IsNull)
                    handlesToAdd.Add(id.Handle.ToString());
            }

            var keysToRemove = new List<string>();
            foreach (DBDictionaryEntry entry in edgesDict)
            {
                if (entry.Key.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase) &&
                    entry.Value is ObjectId xrecId && xrecId.IsValid)
                {
                    var xrec = tr.GetObject(xrecId, OpenMode.ForWrite) as Xrecord;
                    if (xrec?.Data != null)
                    {
                        foreach (TypedValue tv in xrec.Data)
                        {
                            if (tv.TypeCode == (int)DxfCode.Text &&
                                tv.Value is string handleStr &&
                                handlesToAdd.Contains(handleStr))
                            {
                                // Mark old Xrecord for removal
                                keysToRemove.Add(entry.Key);
                                xrec.Erase();
                                break;
                            }
                        }
                    }
                }
            }

            // --- Remove old keys from dictionary
            foreach (var key in keysToRemove)
                edgesDict.Remove(key);

            // --- Add new edges
            int counter = 0;
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

        internal static bool TryResolveOwningGradeBeam(
            FoundationContext context,
            Transaction tr,
            ObjectId selectedId,
            out string gradeBeamHandle,
            out bool isCenterline,
            out bool isEdge)
        {
            gradeBeamHandle = null;
            isCenterline = false;
            isEdge = false;

            if (context == null || tr == null || selectedId.IsNull)
                return false;

            var db = context.Document?.Database;
            if (db == null)
                return false;

            // Get the handle string of the selected object
            string selectedHandle = selectedId.Handle.ToString();

            // Enumerate all grade beams
            foreach (var (handle, gbDict) in EnumerateInteriorGradeBeams(context, tr))
            {
                // --- Check if the selected object is the centerline
                if (handle == selectedHandle)
                {
                    gradeBeamHandle = handle;
                    isCenterline = true;
                    return true;
                }

                // --- Check edges
                if (NODCore.TryGetBeamEdges(tr, gbDict, out var edgesDict))
                {
                    foreach (var (_, xrecId) in NODCore.EnumerateDictionary(edgesDict))
                    {
                        if (!xrecId.IsValid || xrecId.IsErased)
                            continue;

                        var xrec = tr.GetObject(xrecId, OpenMode.ForRead) as Xrecord;
                        if (xrec?.Data == null)
                            continue;

                        foreach (TypedValue tv in xrec.Data)
                        {
                            if (tv.TypeCode != (int)DxfCode.Text)
                                continue;

                            var edgeHandle = tv.Value as string;
                            if (edgeHandle == selectedHandle)
                            {
                                gradeBeamHandle = handle;
                                isEdge = true;
                                return true;
                            }
                        }
                    }
                }
            }

            // Not found
            return false;
        }

        internal static int DeleteBeamFull(FoundationContext context, Transaction tr, string handle)
        {
            if (context?.Document == null || string.IsNullOrWhiteSpace(handle))
                return 0;

            // --- Get the grade beam node dictionary by handle
            if (!NODCore.TryGetGradeBeamInteriorBeamNode(tr, context.Document.Database, handle, out var beamNode) || beamNode == null)
                return 0;

            int deletedCount = 0;

            // --- Delete SECTION metadata under FD_METADATA if it exists
            if (NODCore.TryGetGradeBeamSectionFromMetaDict(tr, beamNode, out var sectionDict) && sectionDict != null)
            {
                sectionDict.UpgradeOpen();
                sectionDict.Erase();
                deletedCount++;
            }

            // --- Delete FD_METADATA itself if it exists
            if (NODCore.TryGetGradeBeamMeta(tr, beamNode, out var metadataDict) && metadataDict != null)
            {
                metadataDict.UpgradeOpen();
                metadataDict.Erase();
                deletedCount++;
            }

            // --- Delete all edges
            if (NODCore.TryGetBeamEdges(tr, beamNode, out var edgesDict) && edgesDict != null)
            {
                var keys = new List<string>();
                foreach (DBDictionaryEntry entry in edgesDict)
                    keys.Add(entry.Key);

                foreach (var key in keys)
                {
                    var xrec = tr.GetObject(edgesDict.GetAt(key), OpenMode.ForWrite) as Xrecord;
                    xrec?.Erase();
                    edgesDict.Remove(key);
                    deletedCount++;
                }
            }

            // --- Remove the beam node from its parent
            var parentDict = (DBDictionary)tr.GetObject(beamNode.OwnerId, OpenMode.ForWrite);
            if (parentDict.Contains(handle))
                parentDict.Remove(handle);

            // --- Erase the beam node itself
            beamNode.UpgradeOpen();
            beamNode.Erase();
            deletedCount++;

            return deletedCount;
        }

        internal static int DeleteBeamEdgesOnly(
            FoundationContext context,
            Transaction tr,
            DBDictionary gradeBeamDict)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (gradeBeamDict == null) return 0;

            int deleted = 0;

            if (!NODCore.TryGetBeamEdges(tr, gradeBeamDict, out var edgesDict) || edgesDict == null)
                return 0;

            // Upgrade both the edges dictionary and the beam dictionary itself to write
            gradeBeamDict.UpgradeOpen();
            edgesDict.UpgradeOpen();

            var keys = new List<string>();
            foreach (DBDictionaryEntry entry in edgesDict)
                keys.Add(entry.Key);

            foreach (var key in keys)
            {
                var xrec = tr.GetObject(edgesDict.GetAt(key), OpenMode.ForWrite) as Xrecord;
                System.Diagnostics.Debug.WriteLine($"BeamDict IsErased={gradeBeamDict.IsErased}, IsWriteEnabled={gradeBeamDict.IsWriteEnabled}"
);
                xrec?.Erase();
                edgesDict.Remove(key);
                deleted++;
            }

            return deleted;
        }

        public static (double Width, double Depth) GetBeamSection(
            Transaction tr,
            DBDictionary beamNode)
        {
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (beamNode == null) throw new ArgumentNullException(nameof(beamNode));

            // Make sure our dictionary is writeable
            if (!beamNode.IsWriteEnabled && !beamNode.IsErased)
                beamNode.UpgradeOpen();

            // --- Get or create META dictionary
            var metaDict = NODCore.TryGetGradeBeamMeta(tr, beamNode, out var existingMeta)
                ? existingMeta
                : NODCore.GetOrCreateNestedSubDictionary(tr, beamNode, NODCore.KEY_METADATA_SUBDICT);

            // --- Get or create SECTION dictionary under META
            if (!metaDict.IsWriteEnabled && !metaDict.IsErased)
                metaDict.UpgradeOpen();

            var sectionDict = NODCore.TryGetGradeBeamSectionFromMetaDict(tr, beamNode, out var existingSection)
                ? existingSection
                : NODCore.GetOrCreateNestedSubDictionary(tr, metaDict, NODCore.KEY_META_SECTION_SUBDICT);

            if (!sectionDict.IsWriteEnabled && !sectionDict.IsErased)
                metaDict.UpgradeOpen();

            // --- Read stored width/depth
            var width = NODCore.GetXRecordValue(tr, sectionDict, NODCore.KEY_SECTION_WIDTH);
            var depth = NODCore.GetXRecordValue(tr, sectionDict, NODCore.KEY_SECTION_DEPTH);

            // --- Set defaults if missing
            if (!width.HasValue)
                NODCore.SetXRecordValue(tr, sectionDict, NODCore.KEY_SECTION_WIDTH, GradeBeamBuilder.DEFAULT_BEAM_WIDTH_IN);
            if (!depth.HasValue)
                NODCore.SetXRecordValue(tr, sectionDict, NODCore.KEY_SECTION_DEPTH, GradeBeamBuilder.DEFAULT_BEAM_DEPTH_IN);

            return (
                width ?? GradeBeamBuilder.DEFAULT_BEAM_WIDTH_IN,
                depth ?? GradeBeamBuilder.DEFAULT_BEAM_DEPTH_IN
            );
        }
    }
}