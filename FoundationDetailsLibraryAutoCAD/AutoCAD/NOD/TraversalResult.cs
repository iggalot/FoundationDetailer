using Autodesk.AutoCAD.DatabaseServices;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.NOD
{
    internal enum TraversalStatus
    {
        Success,
        InvalidHandle,
        MissingObjectId,
        ErasedObject,
        NotEntity
    }

    public partial class NODCore
    {
        internal sealed class TraversalResult
        {
            public string Key { get; }
            public string Path { get; }          // FULL dictionary path
            public Handle Handle { get; }
            public ObjectId ObjectId { get; }
            public Entity Entity { get; }
            public TraversalStatus Status { get; }

            private TraversalResult(
                string key,
                string path,
                TraversalStatus status,
                Handle handle = default,
                ObjectId objectId = default,
                Entity entity = null)
            {
                Key = key;
                Path = path;
                Status = status;
                Handle = handle;
                ObjectId = objectId;
                Entity = entity;
            }

            // ===============================
            // FACTORIES
            // ===============================

            public static TraversalResult Success(
                string key,
                string path,
                Handle handle,
                ObjectId id,
                Entity ent)
            {
                return new TraversalResult(
                    key,
                    path,
                    TraversalStatus.Success,
                    handle,
                    id,
                    ent);
            }

            public static TraversalResult InvalidHandle(string key, string path)
            {
                return new TraversalResult(
                    key,
                    path,
                    TraversalStatus.InvalidHandle);
            }

            public static TraversalResult MissingObjectId(
                string key,
                string path,
                Handle handle)
            {
                return new TraversalResult(
                    key,
                    path,
                    TraversalStatus.MissingObjectId,
                    handle);
            }

            public static TraversalResult Erased(
                string key,
                string path,
                Handle handle,
                ObjectId id)
            {
                return new TraversalResult(
                    key,
                    path,
                    TraversalStatus.ErasedObject,
                    handle,
                    id);
            }

            public static TraversalResult NotEntity(
                string key,
                string path,
                Handle handle,
                ObjectId id)
            {
                return new TraversalResult(
                    key,
                    path,
                    TraversalStatus.NotEntity,
                    handle,
                    id);
            }
        }

    }
}