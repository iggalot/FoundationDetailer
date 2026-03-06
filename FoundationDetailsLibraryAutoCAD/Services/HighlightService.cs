using Autodesk.AutoCAD.DatabaseServices;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD
{
    public static class HighlightService
    {
        private const string TempLayer = "_TEMP_HIGHLIGHT";
        private const double DefaultWidth = 15;


        /*
        Common AutoCAD ACI (Color Index) colors:
        0 = BYBLOCK
        1 = red
        2 = yellow
        3 = green
        4 = cyan
        5 = blue
        6 = magenta
        7 = white / black (depends on background)
        */

        public static void HighlightEntities(
            FoundationContext context,
            IEnumerable<ObjectId> ids,
            short colorIndex = 2,    // yellow
            double width = DefaultWidth)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var db = doc.Database;
            var ed = doc.Editor;

            var idList = ids?
                .Where(id => !id.IsNull && id.IsValid)
                .Distinct()
                .ToList();

            if (idList == null || idList.Count == 0)
            {
                ed.WriteMessage("\nNothing to highlight.");
                return;
            }

            // -------------------------------
            // Draw temporary highlight objects
            // -------------------------------
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                ObjectId layerId;

                if (!lt.Has(TempLayer))
                {
                    lt.UpgradeOpen();

                    var ltr = new LayerTableRecord
                    {
                        Name = TempLayer,
                        Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                            Autodesk.AutoCAD.Colors.ColorMethod.ByAci,
                            colorIndex)
                    };

                    layerId = lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }
                else
                {
                    layerId = lt[TempLayer];
                }

                foreach (var id in idList)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null)
                        continue;

                    var clone = ent.Clone() as Entity;
                    if (clone == null)
                        continue;

                    clone.LayerId = layerId;
                    clone.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                        Autodesk.AutoCAD.Colors.ColorMethod.ByAci,
                        colorIndex);

                    // --- Set width for variable-width entities ---
                    if (width > 0)
                    {
                        switch (clone)
                        {
                            case Polyline pl:
                                for (int i = 0; i < pl.NumberOfVertices; i++)
                                {
                                    pl.SetStartWidthAt(i, width);
                                    pl.SetEndWidthAt(i, width);
                                }
                                break;

                            case Line ln:
                                ln.LineWeight = LineWeight.LineWeight050; // 50 = ~0.5mm
                                break;

                            case Polyline2d pl2d:
                                // Polyline2d does not support width per vertex, skip or handle differently
                                break;

                            case Circle circ:
                                // Circles don't have line width per se; could set LineWeight
                                circ.LineWeight = LineWeight.LineWeight050;
                                break;

                                // Add other types as needed
                        }
                    }

                    ms.AppendEntity(clone);
                    tr.AddNewlyCreatedDBObject(clone, true);
                }

                tr.Commit();
            }

            ed.WriteMessage("\nPress SPACE / ENTER / ESC to clear highlight...");
            ed.GetString("\n");

            Cleanup(context);

            // Select original objects
            ed.SetImpliedSelection(idList.ToArray());
        }
        private static void Cleanup(FoundationContext context)
        {
            var doc = context.Document;
            var db = doc.Database;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

                if (!lt.Has(TempLayer))
                    return;

                var layerId = lt[TempLayer];

                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;

                    if (ent != null && ent.LayerId == layerId)
                    {
                        ent.UpgradeOpen();
                        ent.Erase();
                    }
                }

                var layer = (LayerTableRecord)tr.GetObject(layerId, OpenMode.ForWrite);
                layer.Erase();

                tr.Commit();
            }
        }
    }
}
