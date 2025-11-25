using System;
using Autodesk.AutoCAD.Geometry;


namespace FoundationDetailer.Data
{
    public class BeamRebar
    {
        public string TopBarSize { get; set; }
        public string BottomBarSize { get; set; }
        public int NumTopBars { get; set; }
        public int NumBottomBars { get; set; }
        public string StirrupSize { get; set; }
        public double StirrupSpacing { get; set; }
    }


    public class GradeBeam
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Point3d Start { get; set; }
        public Point3d End { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public BeamRebar Rebar { get; set; } = new BeamRebar();
    }
}