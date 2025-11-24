using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;


namespace FoundationDetailer.Data
{
    public enum StrandType { Beam, Slab }


    public class Strand
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public StrandType Type { get; set; }
        public List<Point3d> Path { get; set; } = new List<Point3d>();
        public double Eccentricity { get; set; }
        public double Force { get; set; }
    }
}