using Autodesk.AutoCAD.Geometry;
using System.Collections.Generic;

namespace FoundationDetailer.Model
{
    /// <summary>
    /// Main foundation model containing all elements of the foundation.
    /// </summary>
    public class FoundationModel
    {
        // --- Global settings ---
        public FoundationSettings Settings { get; set; } = new FoundationSettings();

        // --- Geometry ---
        public List<Boundary> Boundaries { get; set; } = new List<Boundary>();
        public List<Pier> Piers { get; set; } = new List<Pier>();
        public List<GradeBeam> GradeBeams { get; set; } = new List<GradeBeam>();

        // --- Reinforcement ---
        public List<RebarBar> Rebars { get; set; } = new List<RebarBar>();
        public List<Strand> Strands { get; set; } = new List<Strand>();

        // --- Special regions ---
        public List<SlopeRegion> Slopes { get; set; } = new List<SlopeRegion>();
        public List<DropRegion> Drops { get; set; } = new List<DropRegion>();
        public List<CurbRegion> Curbs { get; set; } = new List<CurbRegion>();
    }

    /// <summary>
    /// Outer boundary of a foundation slab.
    /// </summary>
    public class Boundary
    {
        public List<Point3d> Points { get; set; } = new List<Point3d>();
        public double Elevation { get; set; } = 0.0;
    }

    /// <summary>
    /// Pier: circular or square vertical element.
    /// </summary>
    public class Pier
    {
        public Point3d Location { get; set; } = new Point3d();
        public bool IsCircular { get; set; } = true;
        public double Diameter { get; set; } = 12.0; // for circular
        public double Width { get; set; } = 12.0;    // for square
        public double Depth { get; set; } = 12.0;
    }

    /// <summary>
    /// Grade beam: horizontal beam connecting piers or foundations.
    /// </summary>
    public class GradeBeam
    {
        public Point3d Start { get; set; } = new Point3d();
        public Point3d End { get; set; } = new Point3d();
        public double Width { get; set; } = 12.0;
        public double Depth { get; set; } = 12.0;
    }

    /// <summary>
    /// Reinforcing bar in slab or beam.
    /// </summary>
    public class RebarBar
    {
        public Point3d Start { get; set; } = new Point3d();
        public Point3d End { get; set; } = new Point3d();
        public string BarSize { get; set; } = "#5";
        public double Spacing { get; set; } = 12.0;
        public string Layer { get; set; } = "REBAR";
    }

    /// <summary>
    /// Prestress or strand reinforcement.
    /// </summary>
    public class Strand
    {
        public Point3d Start { get; set; } = new Point3d();
        public Point3d End { get; set; } = new Point3d();
        public string StrandSize { get; set; } = "0.6in";
        public string Layer { get; set; } = "STRAND";
    }

    /// <summary>
    /// Sloped region of slab.
    /// </summary>
    public class SlopeRegion
    {
        public List<Point3d> Boundary { get; set; } = new List<Point3d>();
        public double Slope { get; set; } = 0.0; // slope ratio, e.g., 1/8
    }

    /// <summary>
    /// Dropped slab region.
    /// </summary>
    public class DropRegion
    {
        public List<Point3d> Boundary { get; set; } = new List<Point3d>();
        public double Depth { get; set; } = 0.0; // positive down
    }

    /// <summary>
    /// Curb or brick ledge region.
    /// </summary>
    public class CurbRegion
    {
        public List<Point3d> Boundary { get; set; } = new List<Point3d>();
        public double Height { get; set; } = 6.0;
        public double Width { get; set; } = 6.0;
    }

    /// <summary>
    /// Global foundation defaults and settings.
    /// </summary>
    public class FoundationSettings
    {
        // Concrete & structural
        public bool IsReinforced { get; set; } = true;
        public bool HasPrestress { get; set; } = false;
        public double SlabThickness { get; set; } = 8.0;
        public double GradeBeamDepth { get; set; } = 12.0;
        public double PierDiameter { get; set; } = 12.0;
        public double PierWidth { get; set; } = 12.0;

        // Bar & strand defaults
        public string DefaultRebarSize { get; set; } = "#5";
        public double DefaultBarSpacing { get; set; } = 12.0;
        public string DefaultStrandSize { get; set; } = "0.6in";

        // Curb / brick ledge
        public bool AddCurbs { get; set; } = true;
        public double CurbHeight { get; set; } = 6.0;
        public double CurbWidth { get; set; } = 6.0;
        public bool AddBrickLedge { get; set; } = false;

        // Slopes / drops
        public double DefaultSlope { get; set; } = 1.0 / 8.0;
        public double DefaultDrop { get; set; } = 0.0;

        // Misc
        public string ConcreteStrength { get; set; } = "4000 psi";
        public string ProjectNotes { get; set; } = "";
    }
}
