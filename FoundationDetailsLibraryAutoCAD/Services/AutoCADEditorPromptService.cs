using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using FoundationDetailsLibraryAutoCAD.Data;
using FoundationDetailsLibraryAutoCAD.UI.Controls.EqualSpacingGBControl;
using System;

namespace FoundationDetailsLibraryAutoCAD.Services
{
    public class AutoCADEditorPromptService
    {
        public static (Point3d? start, Point3d? end) PromptForSpacingPoints(FoundationContext context)
        {
            if (context == null)
                return (null, null);

            Document doc = context.Document;

            if (doc == null)
                return (null, null);

            Editor ed = doc.Editor;

            PromptPointResult p1 = ed.GetPoint("\nPick first reference point:");
            if (p1.Status != PromptStatus.OK)
                return (null, null);

            PromptPointOptions ppo = new PromptPointOptions("\nPick second reference point:")
            {
                BasePoint = p1.Value,
                UseBasePoint = true
            };

            PromptPointResult p2 = ed.GetPoint(ppo);
            if (p2.Status != PromptStatus.OK)
                return (null, null);

            return (p1.Value, p2.Value);
        }

        public static int? PromptForEqualSpacingCount(FoundationContext context, int min = 1, int max = 1000)
        {
            if (context == null)
                return 1;

            Document doc = context.Document;

            if (doc == null)
                return 1;

            Editor ed = doc.Editor;

            var opts = new PromptIntegerOptions(
                $"\nEnter number of equal spaces [{min}–{max}]:")
            {
                AllowNegative = false,
                AllowZero = false,
                LowerLimit = min,
                UpperLimit = max
            };

            PromptIntegerResult res = ed.GetInteger(opts);

            if (res.Status != PromptStatus.OK)
                return null;

            return res.Value;
        }

        public static string PromptForSpacingSource(FoundationContext context)
        {
            Editor ed = context.Document.Editor;

            PromptKeywordOptions pko =
                new PromptKeywordOptions(
                    "\nSelect spacing source [Points/Edge] <Points>: ");

            pko.Keywords.Add("Points");
            pko.Keywords.Add("Edge");
            pko.Keywords.Default = "Edge";
            pko.AllowNone = true;

            PromptResult res = ed.GetKeywords(pko);

            if (res.Status != PromptStatus.OK)
                return null;

            return string.IsNullOrWhiteSpace(res.StringResult)
                ? "Edge"
                : res.StringResult;
        }

        public static (Point3d? Start, Point3d? End) PromptForBoundaryEdge(FoundationContext context)
        {
            Document doc = context.Document;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            PromptEntityOptions peo =
                new PromptEntityOptions("\nSelect boundary edge: ");

            peo.SetRejectMessage("\nSelect a closed polyline only.");
            peo.AddAllowedClass(typeof(Polyline), false);

            PromptEntityResult per = ed.GetEntity(peo);

            if (per.Status != PromptStatus.OK)
                return (null, null);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Polyline pl =
                    tr.GetObject(per.ObjectId, OpenMode.ForRead) as Polyline;

                if (pl == null || !pl.Closed)
                {
                    ed.WriteMessage("\nSelected object must be a closed polyline.");
                    return (null, null);
                }

                // Snap pick to polyline
                Point3d snapped =
                    pl.GetClosestPointTo(per.PickedPoint, false);

                double param = pl.GetParameterAtPoint(snapped);

                int i = (int)Math.Floor(param);
                if (i >= pl.NumberOfVertices)
                    i = pl.NumberOfVertices - 1;

                int j = (i + 1) % pl.NumberOfVertices;

                Point3d start = pl.GetPoint3dAt(i);
                Point3d end = pl.GetPoint3dAt(j);

                tr.Commit();

                return (start, end);
            }
        }

        public static SpacingDirections? PromptForSpacingDirection(FoundationContext context)
        {
            if (context == null)
                return null;

            Document doc = context.Document;

            if (doc == null)
                return null;

            Editor ed = doc.Editor;

            var opts = new PromptKeywordOptions(
                "\nSpacing direction [Horizontal/Vertical/Perpendicular] <Perpendicular>:")
            {
                AllowNone = true
            };

            // Add keywords
            opts.Keywords.Add("Horizontal", "H", "Horizontal");
            opts.Keywords.Add("Vertical", "V", "Vertical");
            opts.Keywords.Add("Perpendicular", "P", "Perpendicular");

            PromptResult res = ed.GetKeywords(opts);

            // ESC / Cancel
            if (res.Status == PromptStatus.Cancel)
                return null;

            // ENTER pressed - default
            if (res.Status == PromptStatus.None || string.IsNullOrEmpty(res.StringResult))
                return SpacingDirections.Perpendicular;

            // Map keyword to enum
            switch (res.StringResult)
            {
                case "Horizontal":
                    return SpacingDirections.Horizontal;

                case "Vertical":
                    return SpacingDirections.Vertical;

                case "Perpendicular":
                    return SpacingDirections.Perpendicular;

                default:
                    // Should never happen, but safe guard
                    ed.WriteMessage("\nInvalid direction selected.");
                    return null;
            }
        }
    }
}
