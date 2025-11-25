using Autodesk.AutoCAD.Geometry;
using FoundationDetailer.Model;
using FoundationDetailer.UI.Controls;

namespace FoundationDetailer.UI.Converters
{
    public static class PierConverter
    {
        /// <summary>
        /// Converts a PierData from the UI into a Pier in the model
        /// </summary>
        public static Pier ToModelPier(PierData data)
        {
            return new Pier
            {
                DiameterIn = data.Diameter,
                DepthIn = data.Depth,
                // Default to circular; adjust if you allow square piers later
                IsCircular = true,
                Location = new Point3d(data.X, data.Y, 0),
            };
        }
    }
}
