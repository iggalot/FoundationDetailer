using Autodesk.AutoCAD.Runtime;
using FoundationDetailer.UI.Windows;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.NOD
{
    public class NODViewer
    {
        // ==========================================================
        //  VIEW NOD CONTENT helper function
        // ==========================================================
        public static void ViewFoundationNOD(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var model = context.Model;
            var db = doc.Database;

            // Get all entries across all subdictionaries
            var entries = NODCore.IterateFoundationNod(context, cleanStale: true);

            // Group entries by subdictionary name
            var grouped = entries
                .GroupBy(x => x.GroupName)
                .ToDictionary(g => g.Key, g => g.ToList());

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== EE_Foundation Contents ===");

            foreach (string subDir in NODCore.KNOWN_SUBDIRS)
            {
                sb.AppendLine();
                sb.AppendLine($"[{subDir}]");

                if (!grouped.ContainsKey(subDir) || grouped[subDir].Count == 0)
                {
                    sb.AppendLine("   No Objects");
                    continue;
                }

                foreach (var e in grouped[subDir])
                {
                    sb.AppendLine($"   {e.HandleKey} : {e.Status}");
                }
            }

            //MessageBox.Show(sb.ToString(), "EE_Foundation Viewer");
            ScrollableMessageBox.Show(sb.ToString());

        }

    }
}
