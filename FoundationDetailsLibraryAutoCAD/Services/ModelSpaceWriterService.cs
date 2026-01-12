using Autodesk.AutoCAD.DatabaseServices;
using System;

namespace FoundationDetailsLibraryAutoCAD.Services
{
    internal static class ModelSpaceWriterService
    {
        /// <summary>
        /// Appends an entity to ModelSpace and registers it with the transaction.
        /// </summary>
        public static void AppendToModelSpace(
            Transaction tr,
            Database db,
            Entity entity)
        {
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var btr = (BlockTableRecord)tr.GetObject(
                bt[BlockTableRecord.ModelSpace],
                OpenMode.ForWrite);

            btr.AppendEntity(entity);
            tr.AddNewlyCreatedDBObject(entity, true);
        }
    }

}
