using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using System.Collections.Generic;

namespace FoundationDetailer.AutoCAD
{
    public class GradeBeamMultiVertexPreview
    {
        private readonly Editor _editor;
        private readonly Database _db;
        private readonly List<Point3d> _points;
        private Polyline _previewPolyline;

        public IReadOnlyList<Point3d> Points => _points;

        public GradeBeamMultiVertexPreview(Editor ed, Database db)
        {
            _editor = ed;
            _db = db;
            _points = new List<Point3d>();
        }

        public void AddPoint(Point3d pt)
        {
            _points.Add(pt);
            UpdatePreviewPolyline();
        }

        private void UpdatePreviewPolyline()
        {
            if (_previewPolyline != null)
            {
                _editor.UpdateScreen(); // remove previous preview
            }

            _previewPolyline = new Polyline();
            for (int i = 0; i < _points.Count; i++)
            {
                var pt = _points[i];
                _previewPolyline.AddVertexAt(i, new Point2d(pt.X, pt.Y), 0, 0, 0);
            }

            _editor.UpdateScreen();
        }

        public void ErasePreview()
        {
            if (_previewPolyline != null)
            {
                _previewPolyline = null;
                _editor.UpdateScreen();
            }
        }
    }

// Keep your existing GradeBeamPolylineJig unchanged
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

        protected override bool Update()
        {
            Polyline.SetPointAt(1, new Point2d(_end.X, _end.Y));
            return true;
        }
    }
}