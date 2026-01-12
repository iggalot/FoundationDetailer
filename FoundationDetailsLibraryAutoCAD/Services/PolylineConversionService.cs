using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using FoundationDetailsLibraryAutoCAD.Managers;
using System;
using System.Collections.Generic;

namespace FoundationDetailsLibraryAutoCAD.Services
{
    internal static class PolylineConversionService
    {
        public static Polyline ConvertToPolyline(
            Entity source,
            int minimumVertexCount)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            List<Point2d> verts = ExtractVertices(source);

            if (verts.Count < minimumVertexCount)
                verts = PolylineConversionService.EnsureMinimumVertices(verts, minimumVertexCount);

            var pl = new Polyline();
            for (int i = 0; i < verts.Count; i++)
                pl.AddVertexAt(i, verts[i], 0, 0, 0);

            CopyEntityProperties(source, pl);
            return pl;
        }

        private static List<Point2d> ExtractVertices(Entity ent)
        {
            if (ent is Line line)
            {
                return new List<Point2d>
            {
                new Point2d(line.StartPoint.X, line.StartPoint.Y),
                new Point2d(line.EndPoint.X, line.EndPoint.Y)
            };
            }

            if (ent is Polyline pl)
            {
                var verts = new List<Point2d>();
                for (int i = 0; i < pl.NumberOfVertices; i++)
                    verts.Add(pl.GetPoint2dAt(i));
                return verts;
            }

            throw new NotSupportedException(
                "Only Line or Polyline entities can be converted.");
        }

        private static void CopyEntityProperties(Entity source, Entity target)
        {
            target.LayerId = source.LayerId;
            target.Color = source.Color;
            target.LinetypeId = source.LinetypeId;
            target.LineWeight = source.LineWeight;
        }

        /// <summary>
        /// Extracts 2D vertices from a Line or Polyline entity.
        /// </summary>
        public static List<Point2d> GetVertices(Entity ent)
        {
            if (ent == null) throw new ArgumentNullException(nameof(ent));

            var verts = new List<Point2d>();

            switch (ent)
            {
                case Line line:
                    verts.Add(new Point2d(line.StartPoint.X, line.StartPoint.Y));
                    verts.Add(new Point2d(line.EndPoint.X, line.EndPoint.Y));
                    break;

                case Polyline pl:
                    for (int i = 0; i < pl.NumberOfVertices; i++)
                        verts.Add(pl.GetPoint2dAt(i));
                    break;

                default:
                    throw new ArgumentException("Entity must be a Line or Polyline.", nameof(ent));
            }

            return verts;
        }

        /// <summary>
        /// Creates a new Polyline entity from a list of 2D vertices,
        /// copying basic properties from an optional source entity.
        /// </summary>
        public static Polyline CreatePolylineFromVertices(List<Point2d> verts, Entity sourceEnt = null)
        {
            if (verts == null) throw new ArgumentNullException(nameof(verts));
            if (verts.Count < 2) throw new ArgumentException("At least 2 vertices required.", nameof(verts));

            var pl = new Polyline();

            for (int i = 0; i < verts.Count; i++)
                pl.AddVertexAt(i, verts[i], 0, 0, 0);

            if (sourceEnt != null)
            {
                pl.LayerId = sourceEnt.LayerId;
                pl.Color = sourceEnt.Color;
                pl.LinetypeId = sourceEnt.LinetypeId;
                pl.LineWeight = sourceEnt.LineWeight;
            }

            return pl;
        }

        /// <summary>
        /// Returns a list of vertices from a Line or Polyline and ensures a minimum count by linear interpolation.
        /// </summary>
        internal static List<Point2d> GetVerticesWithMinCount(Entity ent, int minVertexCount = 5)
        {
            if (ent == null) throw new ArgumentNullException(nameof(ent));
            if (minVertexCount < 2) throw new ArgumentException("minVertexCount must be >= 2");

            List<Point2d> verts = new List<Point2d>();

            switch (ent)
            {
                case Line line:
                    verts.Add(new Point2d(line.StartPoint.X, line.StartPoint.Y));
                    verts.Add(new Point2d(line.EndPoint.X, line.EndPoint.Y));
                    break;

                case Polyline pl:
                    for (int i = 0; i < pl.NumberOfVertices; i++)
                        verts.Add(pl.GetPoint2dAt(i));
                    break;

                default:
                    throw new ArgumentException("Unsupported entity type");
            }

            return EnsureMinimumVertices(verts, minVertexCount);
        }

        // ==================================================
        // Subdivide until minimum vertex count is met
        // ==================================================
        internal static List<Point2d> EnsureMinimumVertices(
            List<Point2d> input, int minCount)
        {
            if (input.Count >= minCount)
                return input;

            List<Point2d> result = new List<Point2d>(input);

            while (result.Count < minCount)
            {
                int longestIndex = 0;
                double maxDist = 0.0;

                for (int i = 0; i < result.Count - 1; i++)
                {
                    double d =
                        result[i].GetDistanceTo(result[i + 1]);

                    if (d > maxDist)
                    {
                        maxDist = d;
                        longestIndex = i;
                    }
                }

                Point2d a = result[longestIndex];
                Point2d b = result[longestIndex + 1];

                Point2d mid = new Point2d(
                    (a.X + b.X) * 0.5,
                    (a.Y + b.Y) * 0.5);

                result.Insert(longestIndex + 1, mid);
            }

            return result;
        }
    }
}