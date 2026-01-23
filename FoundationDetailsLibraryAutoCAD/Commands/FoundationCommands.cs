using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using FoundationDetailsLibraryAutoCAD.AutoCAD.NOD;
using FoundationDetailsLibraryAutoCAD.Data;
using FoundationDetailsLibraryAutoCAD.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

[assembly: CommandClass(typeof(FoundationDetailsLibraryAutoCAD.Commands.FoundationCommands))]

namespace FoundationDetailsLibraryAutoCAD.Commands
{
    //
    public class FoundationCommands : IExtensionApplication
    {
        private static PaletteSet _paletteSet;
        private static PaletteMain _paletteControl;

        public void Initialize()
        {
            Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage(
                "\nFoundationDetailer loaded. Run FD_SHOWPALETTE to begin.\n"
            );
        }

        public void Terminate() { }

        [CommandMethod("FD_SHOWPALETTE")]
        public void ShowPalette()
        {
            if (_paletteSet == null)
            {
                _paletteControl = new PaletteMain();

                ElementHost host = new ElementHost
                {
                    Dock = DockStyle.Fill,
                    Child = _paletteControl
                };

                _paletteSet = new PaletteSet("Foundation Detailer")
                {
                    Style = PaletteSetStyles.ShowPropertiesMenu |
                            PaletteSetStyles.ShowAutoHideButton,
                    DockEnabled = DockSides.Left | DockSides.Right
                };

                // Make it floating initially
                _paletteSet.Dock = DockSides.None;

                // Force WPF control to measure itself
                _paletteControl.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                _paletteControl.Arrange(new System.Windows.Rect(_paletteControl.DesiredSize));

                // Set the palette size to match the desired WPF size
                var width = (int)_paletteControl.DesiredSize.Width;
                var height = (int)_paletteControl.DesiredSize.Height;

                // Optional: clamp to min/max
                width = Math.Max(300, Math.Min(1000, width));
                height = Math.Max(400, Math.Min(800, height));

                _paletteSet.Size = new System.Drawing.Size(width, height);
                _paletteSet.Add("Main", host);

                _paletteSet.Visible = true;
            }
            else
            {
                _paletteSet.Visible = true;
            }
        }

        private static void DumpTopLevel(Editor ed, Transaction tr, Database db)
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

            ed.WriteMessage("\n--- TOP LEVEL NOD ---");
            foreach (DBDictionaryEntry e in nod)
                ed.WriteMessage($"\n  {e.Key}");

            if (!nod.Contains(NODCore.ROOT))
            {
                ed.WriteMessage("\n-- ROOT dictionary missing");
                return;
            }

            var root = (DBDictionary)tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForRead);

            ed.WriteMessage("\n--- ROOT CONTENTS ---");
            foreach (DBDictionaryEntry e in root)
                ed.WriteMessage($"\n  {e.Key}");
        }

        private static void DumpGradeBeams(Editor ed, Transaction tr, DBDictionary root)
        {
            if (!root.Contains(NODCore.KEY_GRADEBEAM_SUBDICT))
            {
                ed.WriteMessage("\n-- FD_GRADEBEAM missing");
                return;
            }

            var gbContainer = (DBDictionary)tr.GetObject(
                root.GetAt(NODCore.KEY_GRADEBEAM_SUBDICT),
                OpenMode.ForRead);

            ed.WriteMessage("\n--- GRADE BEAMS ---");

            foreach (DBDictionaryEntry gb in gbContainer)
            {
                ed.WriteMessage($"\nGradeBeam Key: {gb.Key}");

                var gbDict = (DBDictionary)tr.GetObject(gb.Value, OpenMode.ForRead);
                foreach (DBDictionaryEntry child in gbDict)
                    ed.WriteMessage($"\n  --- {child.Key}");
            }
        }

        private static void DumpEdges(Editor ed, Transaction tr, DBDictionary gbDict)
        {
            if (!gbDict.Contains(NODCore.KEY_EDGES_SUBDICT))
            {
                ed.WriteMessage("\n    -- FD_EDGES missing");
                return;
            }

            var edges = (DBDictionary)tr.GetObject(
                gbDict.GetAt(NODCore.KEY_EDGES_SUBDICT),
                OpenMode.ForRead);

            ed.WriteMessage("\n    --- EDGES ---");
            foreach (DBDictionaryEntry e in edges)
            {
                ed.WriteMessage($"\n      {e.Key}");

                var xr = tr.GetObject(e.Value, OpenMode.ForRead) as Xrecord;
                if (xr?.Data != null)
                {
                    foreach (TypedValue tv in xr.Data)
                        ed.WriteMessage($" - {tv.Value}");
                }
            }
        }

        private static void VerifyTryGetEdges(
    FoundationContext context,
    Transaction tr,
    Editor ed,
    DBDictionary gbDict)
        {
            if (!GradeBeamNOD.TryGetEdges(context, tr, gbDict, out var lefts, out var rights))
            {
                ed.WriteMessage("\n TryGetEdges returned FALSE");
                return;
            }

            ed.WriteMessage($"\n TryGetEdges LEFT count: {lefts.Length}");
            ed.WriteMessage($"\n TryGetEdges RIGHT count: {rights.Length}");
        }

        [CommandMethod("FD_NOD_AUDIT")]
        public static void AuditGradeBeamNOD()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            ed.WriteMessage("\n==============================");
            ed.WriteMessage("\n FD_NOD_AUDIT START");
            ed.WriteMessage("\n==============================");

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    AuditTopLevel(ed, tr, db);
                    AuditGradeBeams(ed, tr, db);
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n-- EXCEPTION: {ex.Message}");
                }
            }

            ed.WriteMessage("\n==============================");
            ed.WriteMessage("\n FD_NOD_AUDIT END");
            ed.WriteMessage("\n==============================");
        }

        private static void AuditTopLevel(Editor ed, Transaction tr, Database db)
        {
            var nod = (DBDictionary)tr.GetObject(
                db.NamedObjectsDictionaryId,
                OpenMode.ForRead);

            ed.WriteMessage("\n\n--- TOP LEVEL NOD ---");

            foreach (DBDictionaryEntry e in nod)
                ed.WriteMessage($"\n  {e.Key}");

            if (!nod.Contains(NODCore.ROOT))
            {
                ed.WriteMessage("\n-- ROOT MISSING");
                return;
            }

            var root = (DBDictionary)tr.GetObject(
                nod.GetAt(NODCore.ROOT),
                OpenMode.ForRead);

            ed.WriteMessage("\n\n--- ROOT CONTENTS ---");
            foreach (DBDictionaryEntry e in root)
                ed.WriteMessage($"\n  {e.Key}");

            if (!root.Contains(NODCore.KEY_GRADEBEAM_SUBDICT))
                ed.WriteMessage("\n-- FD_GRADEBEAM MISSING");
            else
                ed.WriteMessage("\n-- FD_GRADEBEAM FOUND");
        }

        private static void AuditGradeBeams(Editor ed, Transaction tr, Database db)
        {
            var nod = (DBDictionary)tr.GetObject(
                db.NamedObjectsDictionaryId,
                OpenMode.ForRead);

            if (!nod.Contains(NODCore.ROOT))
                return;

            var root = (DBDictionary)tr.GetObject(
                nod.GetAt(NODCore.ROOT),
                OpenMode.ForRead);

            if (!root.Contains(NODCore.KEY_GRADEBEAM_SUBDICT))
                return;

            var gbContainer = (DBDictionary)tr.GetObject(
                root.GetAt(NODCore.KEY_GRADEBEAM_SUBDICT),
                OpenMode.ForRead);

            ed.WriteMessage("\n\n--- GRADE BEAM AUDIT ---");

            foreach (DBDictionaryEntry gbEntry in gbContainer)
            {
                string gbKey = gbEntry.Key;
                ed.WriteMessage($"\n\n GradeBeam Key: {gbKey}");

                var gbDict = tr.GetObject(
                    gbEntry.Value,
                    OpenMode.ForRead) as DBDictionary;

                if (gbDict == null)
                {
                    ed.WriteMessage("\n  -- Not a dictionary");
                    continue;
                }

                foreach (DBDictionaryEntry child in gbDict)
                    ed.WriteMessage($"\n  -- {child.Key}");

                // ---- CENTERLINE CHECK ----
                if (!gbDict.Contains(NODCore.KEY_CENTERLINE))
                {
                    ed.WriteMessage("\n  -- FD_CENTERLINE MISSING");
                }
                else
                {
                    var xr = tr.GetObject(
                        gbDict.GetAt(NODCore.KEY_CENTERLINE),
                        OpenMode.ForRead) as Xrecord;

                    string handle = null;

                    if (xr != null && xr.Data != null)
                    {
                        foreach (TypedValue tv in xr.Data)
                        {
                            if (tv.TypeCode == (int)DxfCode.Text)
                            {
                                handle = tv.Value as string;
                                break;
                            }
                        }
                    }

                    ed.WriteMessage($"\n   CENTERLINE HANDLE: {handle}");

                    if (!string.IsNullOrWhiteSpace(handle) &&
                        NODCore.TryGetObjectIdFromHandleString(
                            new FoundationContext(Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument),
                            db,
                            handle,
                            out ObjectId oid))
                    {
                        ed.WriteMessage($" -- ObjectId OK ({oid})");
                    }
                    else
                    {
                        ed.WriteMessage(" -- Handle resolution FAILED");
                    }
                }

                // ---- EDGES CHECK ----
                if (!gbDict.Contains(NODCore.KEY_EDGES_SUBDICT))
                {
                    ed.WriteMessage("\n  -- FD_EDGES MISSING");
                    continue;
                }

                var edgesDict = (DBDictionary)tr.GetObject(
                    gbDict.GetAt(NODCore.KEY_EDGES_SUBDICT),
                    OpenMode.ForRead);

                ed.WriteMessage("\n  --- EDGES RAW DUMP ---");

                foreach (DBDictionaryEntry e in edgesDict)
                {
                    ed.WriteMessage($"\n    Key: {e.Key}");

                    var xr = tr.GetObject(e.Value, OpenMode.ForRead) as Xrecord;
                    if (xr?.Data == null)
                    {
                        ed.WriteMessage(" - -- No Xrecord data");
                        continue;
                    }

                    foreach (TypedValue tv in xr.Data)
                    {
                        ed.WriteMessage($" - Value: {tv.Value}");

                        if (tv.TypeCode == (int)DxfCode.Text &&
                            NODCore.TryGetObjectIdFromHandleString(
                                new FoundationContext(Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument),
                                db,
                                tv.Value.ToString(),
                                out ObjectId oid))
                        {
                            ed.WriteMessage($" -- ObjectId OK ({oid})");
                        }
                        else
                        {
                            ed.WriteMessage(" -- Handle resolution FAILED");
                        }
                    }
                }

                // ---- TryGetEdges CHECK ----
                ed.WriteMessage("\n  --- TryGetEdges RESULT ---");

                if (GradeBeamNOD.TryGetEdges(
                    new FoundationContext(Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument),
                    tr,
                    gbDict,
                    out ObjectId[] lefts,
                    out ObjectId[] rights))
                {
                    ed.WriteMessage($"\n  -- LEFT count: {lefts.Length}");
                    ed.WriteMessage($"\n  -- RIGHT count: {rights.Length}");
                }
                else
                {
                    ed.WriteMessage("\n  -- TryGetEdges returned FALSE");
                }
            }
        }

        [CommandMethod("FD_TEST_BUILD_GRADEBEAM_NOD")]
        public static void TestBuildGradeBeamNOD()
        {
            var context = new FoundationContext(Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument);
            if (context == null) return;

            var doc = context.Document;
            if (doc == null) return;

            var db = doc.Database;
            var ed = doc.Editor;

            if (context == null)
            {
                ed.WriteMessage("\n[TEST] No FoundationContext.");
                return;
            }

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db),
                    OpenMode.ForWrite);

                ed.WriteMessage("\n[TEST] Creating faux grade beams...");

                // --------------------------------------------------
                // 1) Create centerlines
                // --------------------------------------------------
                var centerlines = new List<ObjectId>();

                centerlines.Add(CreatePolyline(tr, ms,
                    new Point2d(0, 0),
                    new Point2d(100, 0)));     // Horizontal

                centerlines.Add(CreatePolyline(tr, ms,
                    new Point2d(50, -50),
                    new Point2d(50, 50)));    // Vertical

                centerlines.Add(CreatePolyline(tr, ms,
                    new Point2d(0, 40),
                    new Point2d(100, 40)));   // Parallel

                // --------------------------------------------------
                // 2) Build NOD entries + edges
                foreach (var clId in centerlines)
                {
                    var left = OffsetCopy(tr, ms, clId, +5);
                    var right = OffsetCopy(tr, ms, clId, -5);

                    GradeBeamNOD.StoreEdgeObjects(context, tr, clId,
                        new[] { left },
                        new[] { right });
                }


                tr.Commit();
            }

            // --------------------------------------------------
            // 3) VERIFY CONTENTS
            // --------------------------------------------------
            using (var tr = db.TransactionManager.StartTransaction())
            {
                ed.WriteMessage("\n\n[VERIFY] Enumerating Grade Beams:");

                foreach (var (handle, gbDict) in GradeBeamNOD.EnumerateGradeBeams(context, tr))
                {
                    ed.WriteMessage($"\n  Beam {handle}");

                    if (!GradeBeamNOD.TryGetEdges(context, tr, gbDict,
                        out var lefts, out var rights))
                    {
                        ed.WriteMessage("\n    -- No edges found");
                        continue;
                    }

                    ed.WriteMessage($"\n    LEFT: {lefts.Length}  RIGHT: {rights.Length}");

                    foreach (var id in lefts.Concat(rights))
                    {
                        ed.WriteMessage($"\n      Edge ObjectId: {id}");
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage("\n\n[TEST] Done.");
        }

        private static ObjectId CreatePolyline(
            Transaction tr,
            BlockTableRecord ms,
            Point2d p1,
            Point2d p2)
        {
            var pl = new Polyline();
            pl.AddVertexAt(0, p1, 0, 0, 0);
            pl.AddVertexAt(1, p2, 0, 0, 0);

            ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);

            return pl.ObjectId;
        }


        private static ObjectId OffsetCopy(
    Transaction tr,
    BlockTableRecord ms,
    ObjectId sourceId,
    double offsetDistance)
        {
            if (sourceId.IsNull || !sourceId.IsValid)
                throw new ArgumentException(nameof(sourceId));

            var src = tr.GetObject(sourceId, OpenMode.ForRead) as Polyline;
            if (src == null)
                throw new InvalidOperationException("Source entity is not a Polyline.");

            // AutoCAD returns a DBObjectCollection
            var offsets = src.GetOffsetCurves(offsetDistance);

            if (offsets == null || offsets.Count == 0)
                throw new InvalidOperationException("Offset operation returned no results.");

            // Expect exactly ONE polyline for a simple centerline
            var offsetPl = offsets[0] as Polyline;
            if (offsetPl == null)
                throw new InvalidOperationException("Offset result is not a Polyline.");

            ms.AppendEntity(offsetPl);
            tr.AddNewlyCreatedDBObject(offsetPl, true);

            return offsetPl.ObjectId;
        }


        [CommandMethod("FD_VIEW_FDNNOD_WITHHANDLES")]
        public static void ViewFDNNODWithHandles()
        {
            var context = new FoundationContext(Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument);
            if (context == null) return;

            var doc = context.Document;
            if (doc == null) return;

            var db = doc.Database;
            var ed = doc.Editor;

            if (context == null)
            {
                ed.WriteMessage("\n[TEST] No FoundationContext.");
                return;
            }

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    NODViewer.ViewFoundationNODWithHandles(context);

                    tr.Commit();
                    ed.WriteMessage("\n[FD_DUMP] Done.");
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    ed.WriteMessage($"\n[FD_DUMP] Exception: {ex.Message}");
                }
            }
        }

        [CommandMethod("FD_TEST_GET_EDGES")]
        public static void TestGetEdges()
        {
            var context = new FoundationContext(Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument);
            if (context == null) return;

            var doc = context.Document;
            if (doc == null) return;

            var db = doc.Database;
            var ed = doc.Editor;

            if (context == null)
            {
                ed.WriteMessage("\n[TEST] No FoundationContext.");
                return;
            }

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var rootDict = NODCore.GetFoundationRootDictionary(tr, db);
                if (rootDict == null)
                {
                    ed.WriteMessage("\nNo EE_Foundation dictionary found.");
                    return;
                }

                if (!rootDict.Contains(NODCore.KEY_GRADEBEAM_SUBDICT))
                {
                    ed.WriteMessage("\nNo grade beams found.");
                    return;
                }

                var gbContainer = tr.GetObject(rootDict.GetAt(NODCore.KEY_GRADEBEAM_SUBDICT), OpenMode.ForRead) as DBDictionary;

                foreach (DBDictionaryEntry entry in gbContainer)
                {
                    var gbDict = tr.GetObject(entry.Value, OpenMode.ForRead) as DBDictionary;
                    if (gbDict == null) continue;

                    if (!GradeBeamNOD.TryGetEdges(context, tr, gbDict, out ObjectId[] leftEdges, out ObjectId[] rightEdges))
                    {
                        ed.WriteMessage($"\nGradeBeam {entry.Key} - No edges found.");
                        continue;
                    }

                    ed.WriteMessage($"\nGradeBeam {entry.Key}:");

                    if (leftEdges.Length > 0)
                    {
                        ed.WriteMessage("\n  Left Edges:");
                        foreach (var oid in leftEdges)
                        {
                            ed.WriteMessage($"\n    {oid.Handle}");
                        }
                    }

                    if (rightEdges.Length > 0)
                    {
                        ed.WriteMessage("\n  Right Edges:");
                        foreach (var oid in rightEdges)
                        {
                            ed.WriteMessage($"\n    {oid.Handle}");
                        }
                    }
                }

                tr.Commit();
            }
        }
    }
}
