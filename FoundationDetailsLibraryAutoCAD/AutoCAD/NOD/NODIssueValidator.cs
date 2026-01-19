using Autodesk.AutoCAD.DatabaseServices;
using FoundationDetailsLibraryAutoCAD.Data;
using System.Collections.Generic;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.NOD
{
    internal class NODIssueValidator
    {
        public string Path { get; set; }
        public string Key { get; set; }
        public string Issue { get; set; }

        internal static class NODGradeBeamValidator
        {
            public class NODIssue
            {
                public string GradeBeamHandle { get; set; }
                public string SubdictPath { get; set; }
                public string Key { get; set; }
                public string Issue { get; set; }
            }

            /// <summary>
            /// Validates all GradeBeam entries and their nested subdictionaries.
            /// </summary>
            public static List<NODIssue> ValidateGradeBeams(
                FoundationContext context,
                Transaction tr,
                Database db)
            {
                var issues = new List<NODIssue>();

                var rootDict = NODCore.GetFoundationRootDictionary(tr, db);
                if (rootDict == null) return issues;

                foreach (DBDictionaryEntry gbEntry in rootDict)
                {
                    string gbHandle = gbEntry.Key;
                    ObjectId gbId = gbEntry.Value;

                    DBObject gbObj;
                    try
                    {
                        gbObj = tr.GetObject(gbId, OpenMode.ForRead);
                    }
                    catch
                    {
                        issues.Add(new NODIssue
                        {
                            GradeBeamHandle = gbHandle,
                            SubdictPath = gbHandle,
                            Key = "<GradeBeam>",
                            Issue = "Unreadable GradeBeam entry"
                        });
                        continue;
                    }

                    if (!(gbObj is DBDictionary gbDict))
                    {
                        issues.Add(new NODIssue
                        {
                            GradeBeamHandle = gbHandle,
                            SubdictPath = gbHandle,
                            Key = "<GradeBeam>",
                            Issue = "Expected DBDictionary for GradeBeam"
                        });
                        continue;
                    }

                    // Validate all nested subdictionaries under this GradeBeam
                    ValidateSubDictionaries(context, tr, gbHandle, gbDict, issues, gbHandle);
                }

                return issues;
            }

            private static void ValidateSubDictionaries(
                FoundationContext context,
                Transaction tr,
                string gbHandle,
                DBDictionary dict,
                List<NODIssue> issues,
                string currentPath)
            {
                foreach (DBDictionaryEntry entry in dict)
                {
                    string key = entry.Key;
                    ObjectId id = entry.Value;
                    string path = currentPath + "/" + key;

                    if (!id.IsValid)
                    {
                        issues.Add(new NODIssue
                        {
                            GradeBeamHandle = gbHandle,
                            SubdictPath = path,
                            Key = key,
                            Issue = "Invalid ObjectId"
                        });
                        continue;
                    }

                    DBObject obj;
                    try
                    {
                        obj = tr.GetObject(id, OpenMode.ForRead);
                    }
                    catch
                    {
                        issues.Add(new NODIssue
                        {
                            GradeBeamHandle = gbHandle,
                            SubdictPath = path,
                            Key = key,
                            Issue = "Unreadable object"
                        });
                        continue;
                    }

                    if (obj is DBDictionary subDict)
                    {
                        // recurse
                        ValidateSubDictionaries(context, tr, gbHandle, subDict, issues, path);
                    }
                    else if (!(obj is Xrecord || obj is Entity))
                    {
                        issues.Add(new NODIssue
                        {
                            GradeBeamHandle = gbHandle,
                            SubdictPath = path,
                            Key = key,
                            Issue = "Unexpected object type: " + obj.GetType().Name
                        });
                    }
                }
            }
        }

    }
}