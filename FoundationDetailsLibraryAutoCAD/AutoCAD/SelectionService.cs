using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using FoundationDetailer.AutoCAD;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD
{
    internal static class SelectionService
    {
        public static void SafeSetImpliedSelection(
            FoundationContext context,
            IEnumerable<ObjectId> ids,
            string debugTag = null)
        {
            if (context == null) return;

            var doc = context.Document;
            if (doc == null) return;

            var ed = doc.Editor;

            var valid = ids
                .Where(id =>
                    !id.IsNull &&
                    id.IsValid &&
                    !id.IsErased &&
                    id.Database == doc.Database)
                .ToArray();

            if (valid.Length == 0)
                return;

            try
            {
                ed.SetImpliedSelection(valid);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
#if DEBUG
                DebugSetImpliedSelection(context, valid, debugTag ?? "SafeSetImpliedSelection");
#else
            ed.WriteMessage($"\nSelection failed: {ex.ErrorStatus}");
#endif
            }
        }

#if DEBUG
        private static void DebugSetImpliedSelection(
            FoundationContext context,
            IEnumerable<ObjectId> ids,
            string tag)
        {
            var doc = context.Document;
            var ed = doc.Editor;

            ed.WriteMessage($"\n[Selection Debug] {tag}");

            int i = 0;
            foreach (var id in ids)
            {
                i++;
                try
                {
                    ed.SetImpliedSelection(new[] { id });
                    ed.WriteMessage($"\n  #{i}: OK ({id.Handle})");
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    ed.WriteMessage(
                        $"\n  #{i}: FAILED ({id.Handle}) : {ex.ErrorStatus}");
                }
            }

            try { ed.SetImpliedSelection(Array.Empty<ObjectId>()); } catch { }
        }


#endif

        public static List<ObjectId> FilterValidIds(
    FoundationContext context,
    IEnumerable<ObjectId> ids,
    out List<ObjectId> invalidIds)
        {
            var valid = new List<ObjectId>();
            invalidIds = new List<ObjectId>();

            if (context == null || context.Document == null) return valid;

            var doc = context.Document;

            foreach (var id in ids)
            {
                if (id.IsNull || !id.IsValid || id.IsErased || id.Database != doc.Database)
                {
                    invalidIds.Add(id);
                    continue;
                }

                valid.Add(id);
            }

            return valid;
        }


        public static void FocusAndHighlight(FoundationContext context, IEnumerable<ObjectId> ids, string debugTag = null)
        {
            if (context == null) return;

            // Bring AutoCAD window to front
            AcadFocusHelper.FocusAutoCADWindow();

            // Defer slightly to let Windows message pump update
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
            {
                SelectionService.SafeSetImpliedSelection(context, ids, debugTag);
            }), DispatcherPriority.Background);
        }
    }

}
