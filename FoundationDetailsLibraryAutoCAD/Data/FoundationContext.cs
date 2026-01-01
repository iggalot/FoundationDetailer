using Autodesk.AutoCAD.ApplicationServices;
using FoundationDetailer.Model;
using System.Collections.Concurrent;

namespace FoundationDetailsLibraryAutoCAD.Data
{ 
    public class FoundationContext
    {
        private static readonly ConcurrentDictionary<Document, FoundationContext> _contexts
            = new ConcurrentDictionary<Document, FoundationContext>();

        public Document Document { get; }
        public FoundationModel Model { get; }

        private FoundationContext(Document doc)
        {
            Document = doc;
            Model = new FoundationModel();
        }

        public static FoundationContext For(Document doc)
        {
            if (doc == null) return null;
            return _contexts.GetOrAdd(doc, d => new FoundationContext(d));
        }

        public static void Remove(Document doc)
        {
            _contexts.TryRemove(doc, out _);
        }
    }
}
