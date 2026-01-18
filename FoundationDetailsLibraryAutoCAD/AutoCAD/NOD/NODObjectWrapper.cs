using Autodesk.AutoCAD.DatabaseServices;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.NOD
{
    public sealed class NODObjectWrapper
    {
        public Entity Entity { get; }
        public Xrecord Xrecord { get; }
        public DBObject Original { get; }

        public string Type => Entity != null ? "Entity" :
                              Xrecord != null ? "Xrecord" :
                              Original?.GetType().Name ?? "Unknown";

        public NODObjectWrapper(Entity ent)
        {
            Entity = ent;
            Original = ent;
        }

        public NODObjectWrapper(Xrecord xr)
        {
            Xrecord = xr;
            Original = xr;
        }

        public NODObjectWrapper(DBObject obj)
        {
            Original = obj;
        }
    }

}
