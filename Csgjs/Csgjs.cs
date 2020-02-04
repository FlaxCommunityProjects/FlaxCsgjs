using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FlaxEngine;

namespace FlaxCsgjs.Source
{
    public class Csgjs
    {
        private const float Epsilon = 1e-4f;

        [NoSerialize]
        public readonly List<CsgPolygon> Polygons = new List<CsgPolygon>();

        public static Csgjs FromPolygons(List<CsgPolygon> polygons)
        {
            var csg = new Csgjs();
            csg.Polygons.AddRange(polygons);
            return csg;
        }

        public Csgjs Clone()
        {
            var csg = new Csgjs();
            csg.Polygons.AddRange(Polygons.Select(p => p.Clone()));
            return csg;
        }

        public static Csgjs CreateCube(Vector3 center, Vector3 size, out List<CsgSurfaceSharedData> surfaces)
        {
            size *= 0.5f;
            Vector3[] normals = new[]
            {
                new Vector3(-1, 0, 0),
                new Vector3(+1, 0, 0),
                new Vector3(0, -1, 0),
                new Vector3(0, +1, 0),
                new Vector3(0, 0, -1),
                new Vector3(0, 0, +1)
            };
            string[] names = new[]
            {
                "Back",
                "Front",
                "Bottom",
                "Top",
                "Left",
                "Right"
            };

            Vector2[] uvs = new[]
            {
                new Vector2(1, 1),
                new Vector2(1, 0),
                new Vector2(0, 0),
                new Vector2(0, 1)
            };

            int[][] faces = new[]
            {
                new int[] { 0, 4, 6, 2 },
                new int[] { 5, 1, 3, 7 }, //
                new int[] { 0, 1, 5, 4 },
                new int[] { 3, 2, 6, 7 }, //
                new int[] { 1, 0, 2, 3 }, //
                new int[] { 4, 5, 7, 6 },
            };

            var cubeSurfaces = new List<CsgSurfaceSharedData>(faces.Length);
            var polygons = faces
                .Select((face, index) =>
                {
                    var vertices = face.Select((i, j) =>
                    {
                        Vector3 pos = center + size * new Vector3(
                            2 * (((i & 1) == 0) ? 0 : 1) - 1,
                            2 * (((i & 2) == 0) ? 0 : 1) - 1,
                            2 * (((i & 4) == 0) ? 0 : 1) - 1
                        );
                        return new CsgVertex(pos, normals[index], uvs[j]);
                    }).ToList();

                    var surface = new CsgSurfaceSharedData(names[index]);
                    cubeSurfaces.Add(surface);
                    return new CsgPolygon(vertices, surface);
                })
                .ToList();

            surfaces = cubeSurfaces;

            var csg = FromPolygons(polygons);
            // And set the shared data
            for (int i = 0; i < surfaces.Count; i++)
            {
                surfaces[i].Csgjs = csg;
            }
            return csg;
        }

        public static Csgjs CreateSphere(Vector3 center, Vector3 size, out List<CsgSurfaceSharedData> surfaces)
        {
            size *= 0.5f;

            float slices = 16;
            float stacks = 8;
            var polygons = new List<CsgPolygon>();
            List<CsgVertex> vertices = null;
            void AddVertex(float theta, float phi)
            {
                Vector2 uvs = new Vector2(theta, phi);
                theta *= Mathf.Pi * 2;
                theta += Mathf.PiOverTwo;
                phi *= Mathf.Pi;
                var dir = new Vector3(
                  Mathf.Cos(theta) * Mathf.Sin(phi),
                  Mathf.Cos(phi),
                  Mathf.Sin(theta) * Mathf.Sin(phi)
                );
                vertices.Add(new CsgVertex(center + dir * size, dir, uvs));
            }

            var sphereSurface = new CsgSurfaceSharedData("Sphere");
            for (var i = 0; i < slices; i++)
            {
                for (var j = 0; j < stacks; j++)
                {
                    vertices = new List<CsgVertex>();
                    AddVertex(i / slices, j / stacks);
                    if (j > 0) AddVertex((i + 1) / slices, j / stacks);
                    if (j < stacks - 1) AddVertex((i + 1) / slices, (j + 1) / stacks);
                    AddVertex(i / slices, (j + 1) / stacks);
                    polygons.Add(new CsgPolygon(vertices, sphereSurface));
                }
            }

            surfaces = new List<CsgSurfaceSharedData>(1)
            {
                sphereSurface
            };

            var csg = FromPolygons(polygons);
            // And set the shared data
            for (int i = 0; i < surfaces.Count; i++)
            {
                surfaces[i].Csgjs = csg;
            }
            return csg;
        }

        public static Csgjs CreateCylinder(Vector3 start, Vector3 end, Vector3 size, out List<CsgSurfaceSharedData> surfaces)
        {
            size *= 0.5f;

            // TODO: This does not entirely make sense for cylinders with arbitrary start and end coordinates
            start *= size.Y;
            end *= size.Y;

            Vector3 ray = end - start;
            float slices = 16;
            Vector3 axisZ = ray.Normalized;
            bool isY = (Mathf.Abs(axisZ.Y) > 0.5);
            Vector3 axisX = Vector3.Cross(new Vector3(isY ? 1 : 0, (!isY) ? 1 : 0, 0), axisZ).Normalized;
            Vector3 axisY = Vector3.Cross(axisX, axisZ).Normalized;
            Vector3 negatedAxisZ = axisZ;
            negatedAxisZ.Negate();
            var startVertex = new CsgVertex(start, negatedAxisZ, Vector2.One * 0.5f);
            var endVertex = new CsgVertex(end, axisZ, Vector2.One * 0.5f);

            var polygons = new List<CsgPolygon>();
            CsgVertex CreatePoint(float stack, float slice, float normalBlend, bool isSide = true)
            {
                var angle = slice * Mathf.Pi * 2;
                var outVector = axisX * Mathf.Cos(angle) + axisY * Mathf.Sin(angle);
                var pos = start + ray * stack + outVector * size;
                var normal = outVector * (1 - Mathf.Abs(normalBlend)) + axisZ * normalBlend;
                Vector2 uv;
                if (isSide)
                {
                    uv = new Vector2(slice, stack);
                }
                else
                {
                    uv = new Vector2(1f - (Mathf.Cos(angle) + 1f) * 0.5f, (Mathf.Sin(angle) + 1f) * 0.5f);
                }

                return new CsgVertex(pos, normal, uv);
            }

            var topSurface = new CsgSurfaceSharedData("Top");
            var sideSurface = new CsgSurfaceSharedData("Side");
            var bottomSurface = new CsgSurfaceSharedData("Bottom");
            for (var i = 0; i < slices; i++)
            {
                float t0 = i / slices;
                float t1 = (i + 1) / slices;
                // Top triangle
                polygons.Add(new CsgPolygon(new List<CsgVertex>() { startVertex.Clone(), CreatePoint(0, t0, -1, false), CreatePoint(0, t1, -1, false) }, topSurface));
                // Side rectangle
                polygons.Add(new CsgPolygon(new List<CsgVertex>() { CreatePoint(0, t1, 0), CreatePoint(0, t0, 0), CreatePoint(1, t0, 0), CreatePoint(1, t1, 0) }, sideSurface));
                // Bottom triangle
                polygons.Add(new CsgPolygon(new List<CsgVertex>() { endVertex.Clone(), CreatePoint(1, t1, 1, false), CreatePoint(1, t0, 1, false) }, bottomSurface));
            }

            surfaces = new List<CsgSurfaceSharedData>(1)
            {
                topSurface,
                sideSurface,
                bottomSurface
            };
            var csg = FromPolygons(polygons);
            // And set the shared data
            for (int i = 0; i < surfaces.Count; i++)
            {
                surfaces[i].Csgjs = csg;
            }
            return csg;
        }

        public Csgjs Union(Csgjs csg)
        {
            if (Polygons.Count == 0) return csg.Clone();
            if (csg.Polygons.Count == 0) return Clone();

            var a = new CsgNode(Clone().Polygons);
            var b = new CsgNode(csg.Clone().Polygons);
            a.ClipTo(b);
            b.ClipTo(a);
            b.Invert();
            b.ClipTo(a);
            b.Invert();
            a.Build(b.AllPolygons());
            return FromPolygons(a.AllPolygons());
        }

        public Csgjs Subtract(Csgjs csg)
        {
            if (Polygons.Count == 0) return new Csgjs();
            if (csg.Polygons.Count == 0) return Clone();

            var a = new CsgNode(Clone().Polygons);
            var b = new CsgNode(csg.Clone().Polygons);
            a.Invert();
            a.ClipTo(b);
            b.ClipTo(a);
            b.Invert();
            b.ClipTo(a);
            b.Invert();
            a.Build(b.AllPolygons());
            a.Invert();
            return FromPolygons(a.AllPolygons());
        }

        public Csgjs Intersect(Csgjs csg)
        {
            if (Polygons.Count == 0) return new Csgjs();
            if (csg.Polygons.Count == 0) return new Csgjs();

            var a = new CsgNode(Clone().Polygons);
            var b = new CsgNode(csg.Clone().Polygons);
            a.Invert();
            b.ClipTo(a);
            b.Invert();
            a.ClipTo(b);
            b.ClipTo(a);
            a.Build(b.AllPolygons());
            a.Invert();
            return FromPolygons(a.AllPolygons());
        }

        public Csgjs Inverse()
        {
            var csg = Clone();
            csg.Polygons.ForEach(p => p.Flip());
            return csg;
        }

        public class CsgVertex
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Vector2 Uv;

            public CsgVertex(Vector3 position, Vector3 normal, Vector2 uv)
            {
                Position = position;
                Normal = normal;
                Normal.Normalize();
                Uv = uv;
            }

            public CsgVertex Clone()
            {
                return new CsgVertex(Position, Normal, Uv);
            }

            public void Flip()
            {
                Normal.Negate();
            }

            public CsgVertex Interpolate(CsgVertex other, float t)
            {
                t = Mathf.Saturate(t);
                return new CsgVertex(
                    Vector3.Lerp(Position, other.Position, t),
                    Vector3.Lerp(Normal, other.Normal, t),
                    Vector2.Lerp(Uv, other.Uv, t)
                );
            }
        }

        public class CsgPlane
        {
            public Vector3 Normal;
            public float W;

            public CsgPlane(Vector3 normal, float w)
            {
                Normal = normal;
                Normal.Normalize();
                W = w;
            }

            public static CsgPlane FromPoints(Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 n = Vector3.Cross(b - a, c - a).Normalized;
                return new CsgPlane(n, Vector3.Dot(n, a));
            }

            public static CsgPlane FromPoints(List<CsgVertex> points)
            {
                //https://github.com/evanw/csg.js/pull/15/files

                Vector3 first = points[0].Position;
                Vector3 direction = points[1].Position - first;
                Vector3 normal;
                int i = 2;
                do
                {
                    normal = Vector3.Cross(direction, points[i].Position - first);
                    i++;
                } while (i < points.Count && normal.IsZero);
                normal.Normalize();
                return new CsgPlane(normal, Vector3.Dot(ref normal, ref first));
            }

            public CsgPlane Clone()
            {
                return new CsgPlane(Normal, W);
            }

            public void Flip()
            {
                Normal.Negate();
                W = -W;
            }

            public void SplitPolygon(CsgPolygon polygon, List<CsgPolygon> coplanarFront, List<CsgPolygon> coplanarBack, List<CsgPolygon> front, List<CsgPolygon> back)
            {
                const int COPLANAR = 0;
                const int FRONT = 1;
                const int BACK = 2;
                const int SPANNING = 3;

                int polygonType = 0;
                var types = new List<int>();
                for (var i = 0; i < polygon.Vertices.Count; i++)
                {
                    var t = Vector3.Dot(Normal, polygon.Vertices[i].Position) - W;
                    var type = (t < -Epsilon) ? BACK : (t > Epsilon) ? FRONT : COPLANAR;
                    polygonType |= type;
                    types.Add(type);
                }

                switch (polygonType)
                {
                case COPLANAR:
                    (Vector3.Dot(Normal, polygon.Plane.Normal) > 0 ? coplanarFront : coplanarBack).Add(polygon);
                    break;
                case FRONT:
                    front.Add(polygon);
                    break;
                case BACK:
                    back.Add(polygon);
                    break;
                case SPANNING:
                    var f = new List<CsgVertex>();
                    var b = new List<CsgVertex>();
                    for (var i = 0; i < polygon.Vertices.Count; i++)
                    {
                        var j = (i + 1) % polygon.Vertices.Count;
                        var ti = types[i];
                        var tj = types[j];
                        var vi = polygon.Vertices[i];
                        var vj = polygon.Vertices[j];
                        if (ti != BACK) f.Add(vi);
                        if (ti != FRONT) b.Add(ti != BACK ? vi.Clone() : vi);
                        if ((ti | tj) == SPANNING)
                        {
                            var t = (W - Vector3.Dot(Normal, vi.Position)) / Vector3.Dot(Normal, vj.Position - vi.Position);
                            var v = vi.Interpolate(vj, t);
                            f.Add(v);
                            b.Add(v.Clone());
                        }
                    }
                    //https://github.com/evanw/csg.js/pull/14/files
                    if (f.Count >= 3) front.Add(new CsgPolygon(f, polygon.Shared, polygon.Plane));
                    if (b.Count >= 3) back.Add(new CsgPolygon(b, polygon.Shared, polygon.Plane));
                    break;
                }
            }
        }

        /// <summary>
        /// Shared data for an entire surface.
        /// </summary>
        /// <example> 
        /// A cylinder has 3 surfaces: top, bottom and side
        /// </example>
        public class CsgSurfaceSharedData
        {
            [ReadOnly]
            public string Name;

            [HideInEditor]
            public Csgjs Csgjs;

            // TODO: Generics?
            public object SurfaceData;

            public CsgSurfaceSharedData(string name)
            {
                Name = name;
            }
        }

        public class CsgPolygon
        {
            public readonly List<CsgVertex> Vertices;
            public readonly CsgSurfaceSharedData Shared;
            public readonly CsgPlane Plane;

            public CsgPolygon(List<CsgVertex> vertices, CsgSurfaceSharedData shared = null, CsgPlane plane = null)
            {
                Vertices = vertices;
                Shared = shared;
                Plane = plane?.Clone() ?? CsgPlane.FromPoints(vertices);
            }

            public CsgPolygon Clone()
            {
                return new CsgPolygon(Vertices.Select(v => v.Clone()).ToList(), Shared, Plane);
            }

            public void Flip()
            {
                Vertices.Reverse();
                Vertices.ForEach(v => v.Flip());
                Plane.Flip();
            }

            public bool Intersects(ref Ray ray, out float distance)
            {
                // I need a negative sign because of a FlaxEngine bug
                Plane plane = new Plane(Plane.Normal, -Plane.W);

                // TODO: Write this function down in your diploma diary and compare it to the other ToLocalPosition.
                Func<Vector3, Vector2> toVector2;
                Vector3 absoluteNormal = Vector3.Abs(plane.Normal);
                float maxValue = absoluteNormal.MaxValue;
                if (maxValue == absoluteNormal.X)
                {
                    toVector2 = (v3) => new Vector2(v3.Y, v3.Z);
                }
                else if (maxValue == absoluteNormal.Y)
                {
                    toVector2 = (v3) => new Vector2(v3.X, v3.Z);
                }
                else
                {
                    toVector2 = (v3) => new Vector2(v3.X, v3.Y);
                }

                float isLeft(Vector2 a, Vector2 b, Vector2 point)
                {
                    return (b.X - a.X) * (point.Y - a.Y) -
                        (point.X - a.X) * (b.Y - a.Y);
                }

                if (ray.Intersects(ref plane, out distance))
                {
                    // Check if the ray really did intersect with this surface
                    // http://geomalgorithms.com/a03-_inclusion.html

                    int windingNumber = 0;
                    Vector2 point = toVector2(ray.Position + ray.Direction * distance);

                    // TODO: Document this neat pattern for looping over a circular array
                    // h is always the element before i
                    for (int h = Vertices.Count - 1, i = 0; i < Vertices.Count; i++)
                    {
                        Vector2 from = toVector2(Vertices[h].Position);
                        Vector2 to = toVector2(Vertices[i].Position);
                        if (from.Y <= point.Y)
                        {
                            if (to.Y > point.Y)
                            {
                                if (isLeft(from, to, point) > 0)
                                {
                                    windingNumber++;
                                }
                            }
                        }
                        else
                        {
                            if (to.Y <= point.Y)
                            {
                                if (isLeft(from, to, point) < 0)
                                {
                                    windingNumber--;
                                }
                            }
                        }
                        h = i;
                    }

                    return windingNumber != 0;
                }
                return false;
            }
        }

        public class CsgNode
        {
            public List<CsgPolygon> Polygons = new List<CsgPolygon>();
            public CsgNode Front;
            public CsgNode Back;
            public CsgPlane Plane;

            public CsgNode(List<CsgPolygon> polygons = null)
            {
                if (polygons != null) Build(polygons);
            }

            public CsgNode Clone()
            {
                var node = new CsgNode();
                node.Plane = Plane?.Clone();
                node.Front = Front?.Clone();
                node.Back = Back?.Clone();
                node.Polygons = Polygons.Select(p => p.Clone()).ToList();
                return node;
            }

            public void Invert()
            {
                for (int i = 0; i < Polygons.Count; i++)
                {
                    Polygons[i].Flip();
                }
                Plane.Flip();
                Front?.Invert();
                Back?.Invert();

                var temp = Front;
                Front = Back;
                Back = temp;
            }

            public List<CsgPolygon> ClipPolygons(List<CsgPolygon> polygons)
            {
                if (Plane == null) return new List<CsgPolygon>(polygons);

                var frontPolygons = new List<CsgPolygon>();
                var backPolygons = new List<CsgPolygon>();

                for (int i = 0; i < polygons.Count; i++)
                {
                    Plane.SplitPolygon(polygons[i], frontPolygons, backPolygons, frontPolygons, backPolygons);
                }

                if (Front != null) frontPolygons = Front.ClipPolygons(frontPolygons);
                if (Back != null) backPolygons = Back.ClipPolygons(backPolygons);
                else backPolygons.Clear();

                frontPolygons.AddRange(backPolygons);
                return frontPolygons;
            }

            public void ClipTo(CsgNode bsp)
            {
                Polygons = bsp.ClipPolygons(Polygons);
                Front?.ClipTo(bsp);
                Back?.ClipTo(bsp);
            }

            public List<CsgPolygon> AllPolygons()
            {
                var polygons = new List<CsgPolygon>(Polygons);
                if (Front != null)
                {
                    polygons.AddRange(Front.AllPolygons());
                }
                if (Back != null)
                {
                    polygons.AddRange(Back.AllPolygons());
                }
                return polygons;
            }

            public void Build(List<CsgPolygon> polygons)
            {
                if (polygons.Count == 0) return;

                if (Plane == null)
                {
                    // Don't randomly choose a split plane. Coherent results are important.
                    Plane = polygons[polygons.Count / 2].Plane.Clone();
                }
                var frontPolygons = new List<CsgPolygon>();
                var backPolygons = new List<CsgPolygon>();
                for (int i = 0; i < polygons.Count; i++)
                {
                    Plane.SplitPolygon(polygons[i], Polygons, Polygons, frontPolygons, backPolygons);
                }

                if (frontPolygons.Count > 0)
                {
                    if (Front == null) Front = new CsgNode();
                    Front.Build(frontPolygons);
                }
                if (backPolygons.Count > 0)
                {
                    if (Back == null) Back = new CsgNode();
                    Back.Build(backPolygons);
                }
            }
        }
    }
}
