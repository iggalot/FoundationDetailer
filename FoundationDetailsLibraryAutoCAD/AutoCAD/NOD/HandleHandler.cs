using Autodesk.AutoCAD.DatabaseServices;

namespace FoundationDetailsLibraryAutoCAD.AutoCAD.NOD
{
    public partial class HandleHandler
    {
        public static class HandleStatus
        {
            public const string Valid = "Valid";
            public const string Missing = "Missing";
            public const string Invalid = "Invalid";
            public const string Error = "Error";
        }

        public class HandleEntry
        {
            public string GroupName { get; set; }   // FD_BOUNDARY, FD_GRADEBEAM, etc.
            public string HandleKey { get; set; }   // handle string
            public string Status { get; set; }      // Valid | Missing | Invalid | Error
            public ObjectId Id { get; set; }         // only set when valid
            public Handle Handle { get; set; }     
            public Entity Entity { get; set; }      
        }
    }
}