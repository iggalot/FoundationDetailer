using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using FoundationDetailsLibraryAutoCAD.AutoCAD;
using FoundationDetailsLibraryAutoCAD.AutoCAD.NOD;
using FoundationDetailsLibraryAutoCAD.Data;
using FoundationDetailsLibraryAutoCAD.Managers;
using FoundationDetailsLibraryAutoCAD.Services;
using FoundationDetailsLibraryAutoCAD.UI.Controls.EqualSpacingGBControl;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FoundationDetailer.AutoCAD
{
    public class GradeBeamManager
    {
        // Track grade beams per document
        private readonly Dictionary<Document, List<ObjectId>> _gradeBeams = new Dictionary<Document, List<ObjectId>>();



        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Placeholder for future use")]
        public void Initialize(FoundationContext context)
        {

        }

        // -------------------------
        // Internal Helpers
        // -------------------------
        public List<Polyline> CreatePreliminaryGradeBeamLayout(
    FoundationContext context,
    Polyline boundary,
    double horizMin,
    double horizMax,
    double vertMin,
    double vertMax,
    int vertexCount = 5)
        {
            if (boundary == null) throw new ArgumentNullException(nameof(boundary));
            if (context?.Document == null) throw new ArgumentNullException(nameof(context));

            var db = context.Document.Database;
            List<Polyline> createdBeams = new List<Polyline>();

            // --- Compute horizontal and vertical gridlines using GridlineManager ---
            var gridlines = GridlineManager.ComputeBothGridlines(
                boundary,
                horizMin,
                horizMax,
                vertMin,
                vertMax,
                vertexCount
            );

            using (context.Document.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // --- Horizontal grade beams ---
                foreach (var linePts in gridlines.Horizontal)
                {
                    List<Point2d> verts = linePts.Select(p => new Point2d(p.X, p.Y)).ToList();
                    Polyline pl = PolylineConversionService.CreatePolylineFromVertices(verts);
                    RegisterGradeBeam(context, pl, tr, appendToModelSpace: true);
                    createdBeams.Add(pl);
                }

                // --- Vertical grade beams ---
                foreach (var linePts in gridlines.Vertical)
                {
                    List<Point2d> verts = linePts.Select(p => new Point2d(p.X, p.Y)).ToList();
                    Polyline pl = PolylineConversionService.CreatePolylineFromVertices(verts);
                    RegisterGradeBeam(context, pl, tr, appendToModelSpace: true);
                    createdBeams.Add(pl);
                }

                tr.Commit();
            }

            return createdBeams;
        }

        internal Polyline RegisterGradeBeam(
    FoundationContext context,
    Polyline pl,
    Transaction tr,
    bool appendToModelSpace = false)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (pl == null) throw new ArgumentNullException(nameof(pl));

            Database db = context.Document.Database;

            // --- Append to ModelSpace if requested ---
            if (appendToModelSpace)
            {
                ModelSpaceWriterService.AppendToModelSpace(tr, db, pl);
            }

            // --- Write grade beam metadata (domain-specific) ---
            FoundationEntityData.Write(tr, pl, NODCore.KEY_GRADEBEAM_SUBDICT);

            // --- Add centerline handle (domain-specific) ---
            GradeBeamNOD.AddGradeBeamCenterlineHandleToNOD(context, pl.ObjectId, tr);

            return pl;
        }


        ///Adds a new gradebeam object between the two selected user points <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <param name="vertexCount"></param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        internal Polyline AddInterpolatedGradeBeam(FoundationContext context, Point3d start, Point3d end, int vertexCount)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (vertexCount < 2) throw new ArgumentException("Vertex count must be >= 2", nameof(vertexCount));

            var db = context.Document.Database;

            using (context.Document.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // Create the interpolated Polyline
                Polyline pl = new Polyline();
                for (int i = 0; i < vertexCount; i++)
                {
                    double t = (double)i / (vertexCount - 1);
                    double x = start.X + (end.X - start.X) * t;
                    double y = start.Y + (end.Y - start.Y) * t;
                    pl.AddVertexAt(i, new Point2d(x, y), 0, 0, 0);
                }

                // Register in NOD and append to ModelSpace
                RegisterGradeBeam(context, pl, tr, appendToModelSpace: true);

                tr.Commit();
                return pl;
            }
        }

        // ---------------------------
        // GradeBeam service function
        // ---------------------------
        internal void AddExistingAsGradeBeam(
            FoundationContext context,
            ObjectId polylineId,
            Transaction tr)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (polylineId.IsNull) throw new ArgumentException("Invalid Polyline ObjectId.", nameof(polylineId));

            Polyline pl = tr.GetObject(polylineId, OpenMode.ForRead) as Polyline;
            if (pl == null)
                throw new ArgumentException("Object is not a Polyline.", nameof(polylineId));

            // Just register in NOD, no append needed
            RegisterGradeBeam(context, pl, tr, appendToModelSpace: false);
        }

        internal void ConvertToGradeBeam(
            FoundationContext context,
            ObjectId oldEntityId,
            int vertexCount,
            Transaction tr)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (oldEntityId.IsNull) throw new ArgumentException("Invalid ObjectId.", nameof(oldEntityId));
            if (tr == null) throw new ArgumentNullException(nameof(tr));

            var db = context.Document.Database;
            var oldEnt = tr.GetObject(oldEntityId, OpenMode.ForRead) as Entity;
            if (oldEnt == null)
                throw new ArgumentException("Object is not a valid entity.", nameof(oldEntityId));

            // --- Convert old entity to new Polyline ---
            var verts = PolylineConversionService.GetVertices(oldEnt);

            // Ensure minimum vertex count
            verts = PolylineConversionService.EnsureMinimumVertices(verts, vertexCount);

            Polyline newPl = PolylineConversionService.CreatePolylineFromVertices(verts, oldEnt);

            // --- Append to ModelSpace (infrastructure) if needed ---
            ModelSpaceWriterService.AppendToModelSpace(tr, db, newPl);

            // --- Write GradeBeam metadata and register in NOD ---
            RegisterGradeBeam(context, newPl, tr, appendToModelSpace: false);

            // --- Remove old GradeBeam NOD entry if it exists ---
            GradeBeamNOD.EraseGradeBeamEntry(tr, db, oldEnt.Handle.ToString());

            // --- Delete old entity from ModelSpace ---
            oldEnt.UpgradeOpen();
            oldEnt.Erase();
        }

        public void ClearAllGradeBeams(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var db = context.Document?.Database;
            if (db == null) return;

            using (context.Document.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // Delete entities stored under the GradeBeam subdictionary
                NODCore.DeleteEntitiesInSubDictionary(context, tr, db, NODCore.KEY_GRADEBEAM_SUBDICT);

                // Clear the GradeBeam subdictionary itself
                NODCore.ClearFoundationSubDictionaryInternal(tr, db, NODCore.KEY_GRADEBEAM_SUBDICT);

                tr.Commit();
            }
        }


        public bool HasAnyGradeBeams(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            bool exists = false;

            var doc = context.Document;
            var db = doc.Database;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                exists = NODScanner.TryGetFirstEntity(
                    context,
                    tr,
                    db,
                    NODCore.KEY_GRADEBEAM_SUBDICT,  // The sub-dictionary key for grade beams
                    out ObjectId oid
                );

                // No need to commit; we're just reading
            }

            return exists;
        }

        public (int Quantity, double TotalLength) GetGradeBeamSummary(FoundationContext context)
        {
            int quantity = 0;
            double totalLength = 0;

            var db = context.Document.Database;

            using (context.Document.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // Get the ROOT dictionary
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                var root = NODCore.GetOrCreateNestedSubDictionary(tr, nod, NODCore.ROOT);

                // Get the top-level GradeBeam dictionary (contains all grade beam handles)
                var gradebeamDict = NODCore.GetOrCreateNestedSubDictionary(tr, root, NODCore.KEY_GRADEBEAM_SUBDICT);

                if (gradebeamDict == null)
                    return (0, 0);

                // Loop over all individual grade beams (sub-dictionaries keyed by handle)
                foreach (DBDictionaryEntry entry in gradebeamDict)
                {
                    if (!(tr.GetObject(entry.Value, OpenMode.ForRead) is DBDictionary handleDict))
                        continue;

                    // Collect all ObjectIds in this handleDict
                    foreach (DBDictionaryEntry subEntry in handleDict)
                    {
                        if (tr.GetObject(subEntry.Value, OpenMode.ForRead) is Entity ent)
                        {
                            quantity++;

                            if (ent is Line line)
                            {
                                totalLength += line.Length;
                            }
                            else if (ent is Polyline pl)
                            {
                                totalLength += MathHelperManager.ComputePolylineLength(pl);
                            }
                            // Extend for other entity types if needed
                        }
                        else if (tr.GetObject(subEntry.Value, OpenMode.ForRead) is Xrecord xr)
                        {
                            // Optionally handle Xrecords if they store length info
                        }
                    }
                }

                tr.Commit();
            }

            return (quantity, totalLength);
        }

        public void HighlightGradeBeams(FoundationContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var ed = doc.Editor;

            // STEP 1 — Collect grade beams
            if (!GradeBeamNOD.TryGetGradeBeamPolylines(context, out List<Polyline> beams) ||
                beams == null || beams.Count == 0)
            {
                ed.WriteMessage("\n[GradeBeam] No grade beams found.");
                return;
            }

            // STEP 2 — Extract ObjectIds
            var ids = beams
                .Where(b => b != null)
                .Select(b => b.ObjectId);

            // STEP 3 — Use SelectionService to filter valid IDs and get invalid ones for logging
            var validIds = SelectionService.FilterValidIds(context, ids, out List<ObjectId> invalidIds);

            // STEP 4 — Log diagnostics
            ed.WriteMessage($"\n[GradeBeam] Found={beams.Count}, Valid={validIds.Count}, Invalid={invalidIds.Count}");
            foreach (var id in invalidIds)
            {
                string handle = id.IsNull ? "<null>" : id.Handle.ToString();
                ed.WriteMessage($"\n  {handle} (invalid/erased)");
            }

            // Bring AutoCAD to front and highlight selected objects
            SelectionService.FocusAndHighlight(context, ids, "HighlightGradeBeam");

        }

        // Track which documents have already registered the RegApp
        private readonly HashSet<Document> _regAppRegistered = new HashSet<Document>();
        /// <summary>
        /// Registers the FD_GRADEBEAM RegApp if not already registered for this document.
        /// </summary>
        public void RegisterGradeBeamRegApp(Document doc, Transaction tr)
        {
            if (_regAppRegistered.Contains(doc)) return;

            var db = doc.Database;
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForWrite);
            if (!rat.Has(NODCore.KEY_GRADEBEAM_SUBDICT))
            {
                var ratr = new RegAppTableRecord { Name = NODCore.KEY_GRADEBEAM_SUBDICT };
                rat.Add(ratr);
                tr.AddNewlyCreatedDBObject(ratr, true);
            }

            _regAppRegistered.Add(doc);
        }

    }
}


