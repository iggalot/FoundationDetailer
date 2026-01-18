using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using FoundationDetailsLibraryAutoCAD.AutoCAD.NOD;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Text;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.Testing
{
    /// <summary>
    /// Test class for verifying GradeBeam JSON export/import and NOD structure.
    /// </summary>
    internal static class FoundationTestTools
    {
        public static void TestGradeBeamJsonRoundTrip(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            Document doc = context.Document;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // ==========================================================
            // Step 1: Create GradeBeam dictionary with Edges and BeamStrands
            // ==========================================================
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Ensure ROOT exists
                DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                DBDictionary root = NODCore.GetOrCreateNestedSubDictionary(tr, nod, NODCore.ROOT);

                // Ensure FD_GRADEBEAM exists
                DBDictionary gradeBeamDict = NODCore.GetOrCreateNestedSubDictionary(tr, root, "FD_GRADEBEAM");

                // Create two sample GradeBeams
                for (int i = 1; i <= 2; i++)
                {
                    string gbName = $"GRADEBEAM_{i}";
                    DBDictionary gbEntry = NODCore.GetOrCreateNestedSubDictionary(tr, gradeBeamDict, gbName);

                    // BeamStrands subdict
                    DBDictionary beamStrands = NODCore.GetOrCreateNestedSubDictionary(tr, gbEntry, "BeamStrands");

                    // Edges subdict
                    DBDictionary edges = NODCore.GetOrCreateNestedSubDictionary(tr, gbEntry, "Edges");

                    // Dummy Xrecord
                    Xrecord xr = new Xrecord
                    {
                        Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, $"Test {gbName}"))
                    };
                    beamStrands.SetAt("Strand1", xr);
                    tr.AddNewlyCreatedDBObject(xr, true);
                }

                // Dump the tree **before committing**
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(NODDebugger.DumpDictionaryTree(root, tr, "ROOT"));
                doc.Editor.WriteMessage(sb.ToString());

                tr.Commit();
            }


            ed.WriteMessage("\n Created test GradeBeam dictionary with Edges and BeamStrands.");

            // ==========================================================
            // Step 2: Export to JSON
            // ==========================================================
            FoundationPersistenceManager.ExportFoundationNOD(context);
            ed.WriteMessage("\n Exported to JSON.");

            // ==========================================================
            // Step 3: Clear the test GradeBeam dictionary from NOD
            // ==========================================================
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                if (nod.Contains(NODCore.ROOT))
                {
                    DBDictionary root = (DBDictionary)tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForWrite);
                    if (root.Contains("GRADEBEAM_TEST"))
                    {
                        DBObject gbObj = tr.GetObject(root.GetAt("GRADEBEAM_TEST"), OpenMode.ForWrite);
                        gbObj?.Erase();
                    }
                }
                tr.Commit();
            }

            ed.WriteMessage("\n Cleared test GradeBeam from NOD.");

            // ==========================================================
            // Step 4: Import from JSON
            // ==========================================================
            FoundationPersistenceManager.ImportFoundationNOD(context);
            ed.WriteMessage("\n Imported JSON back into NOD.");

            // ==========================================================
            // Step 5: Dump dictionary tree to verify structure
            // ==========================================================
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                if (nod.Contains(NODCore.ROOT))
                {
                    DBDictionary root = (DBDictionary)tr.GetObject(nod.GetAt(NODCore.ROOT), OpenMode.ForRead);
                    string tree = NODDebugger.DumpDictionaryTree(root, tr, "ROOT");
                    ed.WriteMessage("\n" + tree);
                }
                tr.Commit();
            }

            ed.WriteMessage("\n Test complete: GradeBeam JSON round-trip verified.");
        }

        public static void TestDumpGradeBeamNod(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            Document doc = context.Document;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // --- Initialize ROOT dictionary ---
                DBDictionary root = NODCore.InitFoundationNOD(context, tr); // pass null if not using context

                // --- Create FD_GRADEBEAM if missing ---
                DBDictionary gradeBeamRoot = NODCore.GetOrCreateNestedSubDictionary(tr, root, "FD_GRADEBEAM");

                // --- Add a test GradeBeam ---
                string gradeBeamName = "GRADEBEAM_TEST";
                DBDictionary gradeBeamDict = NODCore.GetOrCreateNestedSubDictionary(tr, gradeBeamRoot, gradeBeamName);

                // --- Add FD_BEAMSTRAND under this GradeBeam ---
                DBDictionary beamStrands = NODCore.GetOrCreateNestedSubDictionary(tr, gradeBeamDict, "FD_BEAMSTRAND");

                // --- Add FD_EDGES under this GradeBeam ---
                DBDictionary edges = NODCore.GetOrCreateNestedSubDictionary(tr, gradeBeamDict, "FD_EDGES");

                // --- Optionally, add an Xrecord under BeamStrands ---
                Xrecord xr = new Xrecord();
                xr.Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, "TestBeamStrand1"));
                beamStrands.SetAt("BS_001", xr);
                tr.AddNewlyCreatedDBObject(xr, true);

                tr.Commit();

                // --- Dump the full NOD tree ---
                string dump = NODDebugger.DumpDictionaryTree(root, tr, "ROOT");
                doc.Editor.WriteMessage("\n" + dump);
            }
        }

        public static void TestGradeBeamNOD(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            Document doc = context.Document;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Ensure ROOT exists
                DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                DBDictionary root = NODCore.GetOrCreateNestedSubDictionary(tr, nod, NODCore.ROOT);

                // Ensure FD_GRADEBEAM exists
                DBDictionary gradeBeamDict = NODCore.GetOrCreateNestedSubDictionary(tr, root, "FD_GRADEBEAM");

                // Create two sample GradeBeams
                for (int i = 1; i <= 2; i++)
                {
                    string gbName = $"GRADEBEAM_{i}";
                    DBDictionary gbEntry = NODCore.GetOrCreateNestedSubDictionary(tr, gradeBeamDict, gbName);

                    // BeamStrands subdict
                    DBDictionary beamStrands = NODCore.GetOrCreateNestedSubDictionary(tr, gbEntry, "BeamStrands");

                    // Edges subdict
                    DBDictionary edges = NODCore.GetOrCreateNestedSubDictionary(tr, gbEntry, "Edges");

                    // Dummy Xrecord
                    Xrecord xr = new Xrecord
                    {
                        Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, $"Test {gbName}"))
                    };
                    beamStrands.SetAt("Strand1", xr);
                    tr.AddNewlyCreatedDBObject(xr, true);
                }

                // Dump the tree **before committing**
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(NODDebugger.DumpDictionaryTree(root, tr, "ROOT"));
                doc.Editor.WriteMessage(sb.ToString());

                tr.Commit();
            }

        }
    }
}
