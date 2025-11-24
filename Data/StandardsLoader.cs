using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using FoundationDetailer.Standards;

namespace FoundationDetailer.AutoCAD
{
    public static class StandardsLoader
    {
        /// <summary>
        /// Ensures all layers defined in company standards exist in the DWG.
        /// </summary>
        public static void CreateLayers(CompanyStandards standards, Transaction tr, Database db)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);

            foreach (var layerDef in standards.Layers)
            {
                if (!lt.Has(layerDef.Name))
                {
                    LayerTableRecord ltr = new LayerTableRecord
                    {
                        Name = layerDef.Name,
                        Color = Color.FromColorIndex(ColorMethod.ByAci, layerDef.ColorIndex),
                        LinetypeObjectId = db.LinetypeTableId,
                        LineWeight = (LineWeight)(layerDef.Lineweight * 100) // AutoCAD uses 100x
                    };
                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }
            }
        }

        /// <summary>
        /// Ensures all dim styles defined in company standards exist in the DWG.
        /// </summary>
        public static void CreateDimStyles(CompanyStandards standards, Transaction tr, Database db)
        {
            DimStyleTable dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForWrite);

            foreach (var dim in standards.DimStyles)
            {
                if (!dst.Has(dim.Name))
                {
                    DimStyleTableRecord dsr = new DimStyleTableRecord
                    {
                        Name = dim.Name,
                        Dimtxt = dim.TextHeight,
                        Dimasz = dim.ArrowSize,
                        Dimdli = dim.Offset
                    };
                    dst.Add(dsr);
                    tr.AddNewlyCreatedDBObject(dsr, true);
                }
            }
        }

        /// <summary>
        /// Creates or ensures the company text style exists.
        /// </summary>
        public static ObjectId CreateTextStyle(CompanyStandards standards, Transaction tr, Database db)
        {
            TextStyleTable tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForWrite);
            string styleName = standards.TextStyle ?? "Standard";

            if (!tst.Has(styleName))
            {
                TextStyleTableRecord tsr = new TextStyleTableRecord
                {
                    Name = styleName,
                    Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor("Arial", false, false, 0, 0)
                };
                tst.Add(tsr);
                tr.AddNewlyCreatedDBObject(tsr, true);
            }

            return tst[styleName];
        }
    }
}
