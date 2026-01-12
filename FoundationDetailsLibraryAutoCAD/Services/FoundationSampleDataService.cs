using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FoundationDetailsLibraryAutoCAD.AutoCAD.NOD;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FoundationDetailsLibraryAutoCAD.Services
{
    internal class FoundationSampleDataService
    {
        // ==========================================================
        //  SAMPLE DATA CREATION
        // ==========================================================
        [CommandMethod("CreateSampleFoundationForNOD")]
        public void CreateSampleFoundationForNOD(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var model = context.Model;
            var db = doc.Database;
            var ed = doc.Editor;

            using (doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    NODCore.InitFoundationNOD(context, tr);
                    tr.Commit();
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                        DBDictionary root = (DBDictionary)tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForWrite);

                        DBDictionary boundaryDict = (DBDictionary)tr.GetObject(root.GetAt(NODCore.KEY_BOUNDARY_SUBDICT), OpenMode.ForWrite);
                        DBDictionary gradebeamDict = (DBDictionary)tr.GetObject(root.GetAt(NODCore.KEY_GRADEBEAM_SUBDICT), OpenMode.ForWrite);

                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                        // Create FD_BOUNDARY polyline
                        Polyline boundary = new Polyline();
                        boundary.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
                        boundary.AddVertexAt(1, new Point2d(1000, 0), 0, 0, 0);
                        boundary.AddVertexAt(2, new Point2d(1000, 500), 0, 0, 0);
                        boundary.AddVertexAt(3, new Point2d(0, 500), 0, 0, 0);
                        boundary.Closed = true;

                        ms.AppendEntity(boundary);
                        tr.AddNewlyCreatedDBObject(boundary, true);

                        FoundationEntityData.Write(tr, boundary, NODCore.KEY_BOUNDARY_SUBDICT);
                        NODCore.AddHandleToMetadataDictionary(tr, boundaryDict, boundary.Handle.ToString().ToUpperInvariant());

                        // Create 4 FD_GRADEBEAM polylines
                        for (int i = 0; i < 4; i++)
                        {
                            Polyline gb = new Polyline();
                            int y = 10 + i * 10;
                            gb.AddVertexAt(0, new Point2d(10, y), 0, 0, 0);
                            gb.AddVertexAt(1, new Point2d(90, y), 0, 0, 0);

                            ms.AppendEntity(gb);
                            tr.AddNewlyCreatedDBObject(gb, true);
                            NODCore.AddHandleToMetadataDictionary(tr, gradebeamDict, gb.Handle.ToString().ToUpperInvariant());
                        }
                        tr.Commit();
                        ed.WriteMessage("\nSample polylines created for FD_BOUNDARY and FD_GRADEBEAM.");
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage($"\nTransaction failed: {ex.Message}");
                    }
                }
            }
        }
    }
}
