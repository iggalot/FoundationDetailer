using Autodesk.AutoCAD.DatabaseServices;
using FoundationDetailsLibraryAutoCAD.AutoCAD;
using FoundationDetailsLibraryAutoCAD.AutoCAD.NOD;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using static FoundationDetailsLibraryAutoCAD.AutoCAD.NOD.HandleHandler;

namespace FoundationDetailsLibraryAutoCAD.Data
{
    internal class FoundationEntityData
    {
        public class FoundationEntityInfo
        {
            public string GroupName { get; set; }
            public int Version { get; set; }
            public string Handle { get; set; }
        }

        private const string ROOT = "EE_FOUNDATION";

        internal static void Write(
            Transaction tr,
            Entity ent,
            string groupName)
        {
            ent.UpgradeOpen();

            if (ent.ExtensionDictionary.IsNull)
                ent.CreateExtensionDictionary();

            var dict = (DBDictionary)tr.GetObject(
                ent.ExtensionDictionary, OpenMode.ForWrite);

            Xrecord xr = new Xrecord
            {
                Data = new ResultBuffer(
                    new TypedValue((int)DxfCode.Text, groupName),
                    new TypedValue((int)DxfCode.Int32, 1) // version
                )
            };

            dict.SetAt(ROOT, xr);
            tr.AddNewlyCreatedDBObject(xr, true);
        }

        internal static bool HasFoundationData(
            Transaction tr,
            Entity ent)
        {
            if (ent.ExtensionDictionary.IsNull)
                return false;

            var dict = (DBDictionary)tr.GetObject(
                ent.ExtensionDictionary, OpenMode.ForRead);

            return dict.Contains(ROOT);
        }

        internal bool TryRead(Transaction tr, Entity ent, out string groupName)
        {
            groupName = null;
            if (ent.ExtensionDictionary.IsNull) return false;

            var dict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, OpenMode.ForRead);
            if (!dict.Contains(ROOT)) return false;

            var xr = (Xrecord)tr.GetObject(dict.GetAt(ROOT), OpenMode.ForRead);
            if (xr.Data == null) return false;

            foreach (TypedValue tv in xr.Data)
            {
                if (tv.TypeCode == (int)DxfCode.Text)
                {
                    groupName = tv.Value as string;
                    return true;
                }
            }

            return false;
        }

        /// Recursively display all ExtensionDictionary data for an entity.
        /// </summary>
        /// <param name="ent">The entity to inspect</param>
        public static void DisplayExtensionData(FoundationContext context, Entity ent)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var model = context.Model;

            if (doc == null) return;

            var db = doc.Database;
            var ed = doc.Editor;

            if (ent == null)
                return;

            if (ent.ExtensionDictionary.IsNull)
            {
                ed.WriteMessage($"\nEntity {ent.Handle} has no ExtensionDictionary.");
                return;
            }

            using (var tr = doc.TransactionManager.StartTransaction())
            {
                var dict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, OpenMode.ForRead);
                ed.WriteMessage($"\nEntity {ent.Handle} ExtensionDictionary contents:");
                ProcessDictionary(context, tr, dict, 1);
                tr.Commit();
            }
        }

        private static void ProcessDictionary(FoundationContext context, Transaction tr, DBDictionary dict, int indentLevel)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));

            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            string indent = new string(' ', indentLevel * 2);

            foreach (DBDictionaryEntry entry in dict)
            {
                DBObject obj = tr.GetObject(entry.Value, OpenMode.ForRead);

                if (obj is DBDictionary subDict)
                {
                    ed.WriteMessage($"\n{indent}Subdictionary: {entry.Key}");
                    ProcessDictionary(context, tr, subDict, indentLevel + 1);
                }
                else if (obj is Xrecord xr)
                {
                    ed.WriteMessage($"\n{indent}Xrecord: {entry.Key} -> ");
                    foreach (TypedValue tv in xr.Data)
                    {
                        ed.WriteMessage($"[{tv.TypeCode}: {tv.Value}] ");
                    }
                }
                else
                {
                    ed.WriteMessage($"\n{indent}{entry.Key} -> {obj.GetType().Name}");
                }
            }
        }

        /// <summary>
        /// Gets all ExtensionDictionary data for an entity in a structured object.
        /// </summary>
        public static ExtensionDataItem GetExtensionData(
            FoundationContext context,
            Entity ent,
            Transaction tr)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (ent == null || ent.ExtensionDictionary.IsNull)
                return null;

            var dict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, OpenMode.ForRead);

            var rootItem = new ExtensionDataItem
            {
                Name = $"Entity {ent.Handle}",
                Type = "Entity",
                Children = ProcessDictionary(context, tr, dict, ent.Database),
                ObjectId = ent.ObjectId,
                NODObject = new NODObjectWrapper(ent)
            };

            return rootItem;
        }

        private static ObservableCollection<ExtensionDataItem> ProcessDictionary(
            FoundationContext context,
            Transaction tr,
            DBDictionary dict,
            Database db)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (dict == null) throw new ArgumentNullException(nameof(dict));
            if (db == null) throw new ArgumentNullException(nameof(db));

            var items = new ObservableCollection<ExtensionDataItem>();

            foreach (DBDictionaryEntry entry in dict)
            {
                DBObject obj = tr.GetObject(entry.Value, OpenMode.ForRead);

                if (obj is DBDictionary subDict)
                {
                    items.Add(new ExtensionDataItem
                    {
                        Name = entry.Key,
                        Type = "Subdictionary",
                        Children = ProcessDictionary(context, tr, subDict, db)
                    });
                }
                else if (obj is Xrecord xr)
                {
                    var xrValues = new List<string>();
                    if (xr.Data != null)
                    {
                        foreach (TypedValue tv in xr.Data)
                            xrValues.Add($"[{tv.TypeCode}: {tv.Value}]");
                    }

                    items.Add(new ExtensionDataItem
                    {
                        Name = entry.Key,
                        Type = "XRecord",
                        Value = xrValues,
                        ObjectId = entry.Value,
                        NODObject = new NODObjectWrapper(xr)
                    });
                }
                else if (obj is Entity ent)
                {
                    items.Add(new ExtensionDataItem
                    {
                        Name = entry.Key,
                        Type = ent.GetType().Name,
                        ObjectId = ent.ObjectId,
                        NODObject = new NODObjectWrapper(ent)
                    });
                }
                else
                {
                    // fallback: attempt to resolve handle via NODCore
                    var handleEntry = NODCore.ValidateHandleOrId(context, tr, db, "ProcessDictionary", entry.Key);

                    ObjectId objectId = ObjectId.Null;
                    NODObjectWrapper wrapper = null;

                    if (handleEntry.Status == HandleStatus.Valid)
                    {
                        objectId = handleEntry.Id;

                        // Try to get Entity if possible
                        try
                        {
                            var resolvedObj = tr.GetObject(objectId, OpenMode.ForRead);
                            if (resolvedObj is Entity e)
                                wrapper = new NODObjectWrapper(e);
                            else if (resolvedObj is Xrecord xr2)
                                wrapper = new NODObjectWrapper(xr2);
                        }
                        catch
                        {
                            // unreadable, leave wrapper null
                        }
                    }

                    items.Add(new ExtensionDataItem
                    {
                        Name = entry.Key,
                        Type = obj?.GetType().Name ?? "Unknown",
                        ObjectId = objectId,
                        NODObject = wrapper
                    });
                }
            }

            return items;
        }
    }

    /// <summary>
    /// Represents an item in an ExtensionDictionary
    /// </summary>
    public class ExtensionDataItem
    {
        public string Name { get; set; }
        public string Type { get; set; }          // e.g., XRecord, Subdictionary, etc.
        public object Value { get; set; }         // For XRecord, could be List<TypedValue>
        public ObservableCollection<ExtensionDataItem> Children { get; set; } = new ObservableCollection<ExtensionDataItem>();
        public ObjectId? ObjectId { get; set; }   // Nullable ObjectId for entities
        public NODObjectWrapper NODObject { get; set; }  // <--- New

    }
}