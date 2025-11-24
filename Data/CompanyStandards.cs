using System.Collections.Generic;

namespace FoundationDetailer.Standards
{
    public class CompanyStandards
    {
        public List<LayerDefinition> Layers { get; set; } = new List<LayerDefinition>();
        public List<DimStyleDefinition> DimStyles { get; set; } = new List<DimStyleDefinition>();
        public string TextStyle { get; set; } = "Standard";
    }

    public class LayerDefinition
    {
        public string Name { get; set; }
        public short ColorIndex { get; set; }
        public string Linetype { get; set; }
        public double Lineweight { get; set; }
    }

    public class DimStyleDefinition
    {
        public string Name { get; set; }
        public double TextHeight { get; set; }
        public double ArrowSize { get; set; }
        public double Offset { get; set; }
    }
}