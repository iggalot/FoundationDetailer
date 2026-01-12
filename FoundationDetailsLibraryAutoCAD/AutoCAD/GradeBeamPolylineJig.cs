using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace FoundationDetailer.AutoCAD
{
    public class GradeBeamPolylineJig : EntityJig
    {
        private Point3d _start;
        private Point3d _end;

        public Polyline Polyline => (Polyline)Entity;

        public GradeBeamPolylineJig(Point3d startPoint)
            : base(CreateInitialPolyline(startPoint))
        {
            _start = startPoint;
            _end = startPoint;
        }

        private static Polyline CreateInitialPolyline(Point3d start)
        {
            var pl = new Polyline();
            pl.AddVertexAt(0, new Point2d(start.X, start.Y), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(start.X, start.Y), 0, 0, 0);
            return pl;
        }

        /// <summary>
        /// Need for inheritance from EntityJig
        /// </summary>
        /// <param name="prompts"></param>
        /// <returns></returns>
        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var opts = new JigPromptPointOptions("\nSelect second point:")
            {
                BasePoint = _start,
                UseBasePoint = true
            };

            var res = prompts.AcquirePoint(opts);
            if (res.Status != PromptStatus.OK)
                return SamplerStatus.Cancel;

            if (res.Value.IsEqualTo(_end))
                return SamplerStatus.NoChange;

            _end = res.Value;
            return SamplerStatus.OK;
        }

        /// <summary>
        /// Need for inheritance from EntityJig
        /// </summary>
        /// <returns></returns>
        protected override bool Update()
        {
            // Update preview polyline geometry
            Polyline.SetPointAt(1, new Point2d(_end.X, _end.Y));
            return true;
        }
    }

}


