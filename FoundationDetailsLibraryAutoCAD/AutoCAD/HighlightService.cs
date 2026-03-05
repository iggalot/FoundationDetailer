using Autodesk.AutoCAD.DatabaseServices;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD
{
    public static class HighlightService
    {
        const string tempHighlightLayer = "_TEMP_HIGHLIGHT";

        public static void HighlightPolylines(
            FoundationContext context,
            IEnumerable<ObjectId> ids,
            double width = 12,
            string layerName = tempHighlightLayer)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var db = doc.Database;
            var ed = doc.Editor;


            List<Polyline> polylines = new List<Polyline>();

            // Read entities
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in ids)
                {
                    var pl = tr.GetObject(id, OpenMode.ForRead) as Polyline;
                    if (pl != null)
                        polylines.Add(pl);
                }

                tr.Commit();
            }

            if (polylines.Count == 0)
                return;

            // Draw temporary graphics
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

                ObjectId layerId;

                if (!lt.Has(layerName))
                {
                    lt.UpgradeOpen();

                    var ltr = new LayerTableRecord
                    {
                        Name = layerName,
                        Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                            Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 2)
                    };

                    layerId = lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }
                else
                {
                    layerId = lt[layerName];
                }

                foreach (var pl in polylines)
                {
                    var clone = (Polyline)pl.Clone();
                    clone.LayerId = layerId;

                    for (int i = 0; i < clone.NumberOfVertices; i++)
                    {
                        clone.SetStartWidthAt(i, width);
                        clone.SetEndWidthAt(i, width);
                    }

                    ms.AppendEntity(clone);
                    tr.AddNewlyCreatedDBObject(clone, true);
                }

                tr.Commit();
            }

            ed.WriteMessage("\nPress SPACE / ENTER / ESC to clear highlight...");
            ed.GetString("\n");

            CleanupHighlightLayer(context);

            ed.SetImpliedSelection(ids.ToArray());
        }

        private static void CleanupHighlightLayer(FoundationContext context, string layerName = tempHighlightLayer)
        {
            var doc = context.Document;
            var db = doc.Database;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

                if (!lt.Has(layerName))
                    return;

                var layerId = lt[layerName];

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
