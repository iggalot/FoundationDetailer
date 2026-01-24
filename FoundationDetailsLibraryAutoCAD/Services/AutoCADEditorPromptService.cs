using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using FoundationDetailsLibraryAutoCAD.Data;
using FoundationDetailsLibraryAutoCAD.UI.Controls.EqualSpacingGBControl;

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
