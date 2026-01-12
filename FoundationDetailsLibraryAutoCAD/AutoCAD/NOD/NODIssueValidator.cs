//var issues = ValidateNodTree(context, tr, db, rootDict);

//if (issues.Count > 0)
//{
//    foreach (var issue in issues)
//        ed.WriteMessage($"\n[NOD] {issue.Path}: {issue.Issue}");
//}

using Autodesk.AutoCAD.DatabaseServices;
using FoundationDetailsLibraryAutoCAD.Data;
using System.Collections;
using System.Collections.Generic;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.NOD
{
    internal class NODIssueValidator
    {
        public string Path { get; set; }
        public string Key { get; set; }
        public string Issue { get; set; }

        internal static List<NODIssueValidator> ValidateNodTree(
        FoundationContext context,
        Transaction tr,
        Database db,
        DBDictionary dict,
        string path = "")
        {
            var issues = new List<NODIssueValidator>();

            foreach (DictionaryEntry entry in dict)
            {
                if (!(entry.Key is string key))
                {
                    issues.Add(new NODIssueValidator
                    {
                        Path = path,
                        Key = "<non-string>",
                        Issue = "Invalid dictionary key type"
                    });
                    continue;
                }

                string currentPath = $"{path}/{key}";

                if (!(entry.Value is ObjectId id) || !id.IsValid)
                {
                    issues.Add(new NODIssueValidator
                    {
                        Path = currentPath,
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
                    issues.Add(new NODIssueValidator
                    {
                        Path = currentPath,
                        Key = key,
                        Issue = "Unreadable object"
                    });
                    continue;
                }

                if (obj is DBDictionary subDict)
                {
                    issues.AddRange(
                        ValidateNodTree(context, tr, db, subDict, currentPath));
                }
                else if (obj is Xrecord || obj is Entity)
                {
                    // OK
                }
                else
                {
                    issues.Add(new NODIssueValidator
                    {
                        Path = currentPath,
                        Key = key,
                        Issue = $"Unexpected type: {obj.GetType().Name}"
                    });
                }
            }

            return issues;
        }
    }
}