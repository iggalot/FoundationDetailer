using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;

namespace FoundationDetailer.Model
{
    public class FoundationModel
    {
        public FoundationModel()
        {
            Id = Guid.NewGuid();
            Metadata = new ModelMetadata { SchemaVersion = ModelMetadata.CurrentSchemaVersion };
        }

        public Guid Id { get; set; }
        public ModelMetadata Metadata { get; set; }

        public FoundationSettings Settings { get; set; } = new FoundationSettings();

        public List<Boundary> Boundaries { get; set; } = new List<Boundary>();
        public List<Pier> Piers { get; set; } = new List<Pier>();
        public List<GradeBeam> GradeBeams { get; set; } = new List<GradeBeam>();
        public List<RebarBar> Rebars { get; set; } = new List<RebarBar>();
        public List<Strand> Strands { get; set; } = new List<Strand>();

        public List<SlopeRegion> Slopes { get; set; } = new List<SlopeRegion>();
        public List<DropRegion> Drops { get; set; } = new List<DropRegion>();
        public List<CurbRegion> Curbs { get; set; } = new List<CurbRegion>();
    }

    public class ModelMetadata
    {
        public const int CurrentSchemaVersion = 1;
        public int SchemaVersion { get; set; }
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime LastSavedUtc { get; set; } = DateTime.UtcNow;
    }

    public class FoundationSettings
    {
        public bool IsReinforced { get; set; } = true;
        public bool HasPrestress { get; set; } = false;
        public double SlabThicknessIn { get; set; } = 8.0;
        public double GradeBeamDepthIn { get; set; } = 12.0;
        public double DefaultCurbHeightIn { get; set; } = 6.0;
        public bool AddCurbs { get; set; } = true;
        public string DefaultRebarSize { get; set; } = "#5";
        public double DefaultBarSpacingIn { get; set; } = 12.0;
    }

    public class Boundary
    {
        public List<Point3d> Points { get; set; } = new List<Point3d>();
        public double Elevation { get; set; } = 0.0;
        public string Name { get; set; }
    }

    public class Pier
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Point3d Location { get; set; } = new Point3d();
        public bool IsCircular { get; set; } = true;
        public double DiameterIn { get; set; } = 12.0;
        public double WidthIn { get; set; } = 12.0; // for square
        public double DepthIn { get; set; } = 12.0;
        public string Layer { get; set; } = "FOUNDATION-PIER";
    }

    public class GradeBeam
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Point3d Start { get; set; } = new Point3d();
        public Point3d End { get; set; } = new Point3d();
        public double WidthIn { get; set; } = 12.0;
        public double DepthIn { get; set; } = 12.0;
        public string Layer { get; set; } = "FOUNDATION-GRADEBEAM";
    }

    public class RebarBar
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Point3d Start { get; set; } = new Point3d();
        public Point3d End { get; set; } = new Point3d();
        public string BarSize { get; set; } = "#5";
        public double SpacingIn { get; set; } = 12.0;
        public string Layer { get; set; } = "FOUNDATION-REBAR";
    }

    public class Strand
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Point3d Start { get; set; } = new Point3d();
        public Point3d End { get; set; } = new Point3d();
        public string StrandSize { get; set; } = "0.6in";
        public string Layer { get; set; } = "FOUNDATION-STRAND";
    }

    public class SlopeRegion
    {
        public List<Point3d> Boundary { get; set; } = new List<Point3d>();
        public double SlopeRatio { get; set; } = 1.0 / 8.0;
    }

    public class DropRegion
    {
        public List<Point3d> Boundary { get; set; } = new List<Point3d>();
        public double DepthIn { get; set; } = 0.0;
    }

    public class CurbRegion
    {
        public List<Point3d> Boundary { get; set; } = new List<Point3d>();
        public double HeightIn { get; set; } = 6.0;
        public double WidthIn { get; set; } = 6.0;
    }
}
