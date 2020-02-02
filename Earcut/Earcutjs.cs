using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FlaxEngine;

namespace FlaxCsgjs.Source
{
    public static class Earcutjs
    {
        public static List<Int3> Earcut(List<Vector2> data, List<int> holeIndices = null)
        {
            int outerLen = (holeIndices?[0]) ?? data.Count;
            EarcutNode outerNode = linkedList(data, 0, outerLen, true);
            List<Int3> triangles = new List<Int3>();
            if (outerNode == null || outerNode.next == outerNode.prev) return triangles;

            float minX = 0, minY = 0, maxX, maxY, x, y, invSize = 0;

            if (holeIndices?.Count > 0)
            {
                outerNode = eliminateHoles(data, holeIndices, outerNode);
            }

            if (data.Count > 80)
            {
                minX = maxX = data[0].X;
                minY = maxY = data[0].Y;

                for (var i = 1; i < outerLen; i++)
                {
                    x = data[i].X;
                    y = data[i].Y;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }

                // minX, minY and invSize are later used to transform coords into integers for z-order calculation
                invSize = Mathf.Max(maxX - minX, maxY - minY);
                invSize = Mathf.IsZero(invSize) ? 0 : 1 / invSize;
            }

            earcutLinked(outerNode, triangles, minX, minY, invSize, 0);

            return triangles;
        }

        // return a percentage difference between the polygon area and its triangulation area;
        // used to verify correctness of triangulation
        public static float Deviation(List<Int3> triangles, List<Vector2> data, List<int> holeIndices = null)
        {
            bool hasHoles = holeIndices != null && holeIndices.Count > 0;
            int outerLen = (holeIndices?[0]) ?? data.Count;

            var polygonArea = Mathf.Abs(signedArea(data, 0, outerLen));
            if (hasHoles)
            {
                int len = holeIndices.Count;
                for (int i = 0; i < len; i++)
                {
                    var start = holeIndices[i];
                    var end = i < len - 1 ? holeIndices[i + 1] : data.Count;
                    polygonArea -= Mathf.Abs(signedArea(data, start, end));
                }
            }

            float trianglesArea = 0;
            for (int i = 0; i < triangles.Count; i++)
            {
                var abc = triangles[i];
                trianglesArea += Mathf.Abs(
                  (data[abc.X].X - data[abc.Z].X) * (data[abc.Y].Y - data[abc.X].Y) -
                    (data[abc.X].X - data[abc.Y].X) * (data[abc.Z].Y - data[abc.X].Y)
                );
            }

            return polygonArea == 0 && trianglesArea == 0
              ? 0
              : Mathf.Abs((trianglesArea - polygonArea) / polygonArea);
        }

        // create a circular doubly linked list from polygon points in the specified winding order
        static EarcutNode linkedList(List<Vector2> data, int start, int end, bool clockwise)
        {
            EarcutNode last = null;
            bool isPositiveArea = signedArea(data, start, end) > 0;
            if (clockwise == isPositiveArea)
            {
                for (int i = start; i < end; i++)
                {
                    last = insertNode(i, data[i].X, data[i].Y, last);
                }
            }
            else
            {
                for (int i = end - 1; i >= start; i--)
                {
                    last = insertNode(i, data[i].X, data[i].Y, last);
                }

            }

            if (last != null && equals(last, last.next))
            {
                removeNode(last);
                last = last.next;
            }

            return last;
        }

        // eliminate colinear or duplicate points
        static EarcutNode filterPoints(EarcutNode start, EarcutNode end = null)
        {
            if (start == null) return start;
            if (end == null) end = start;

            var p = start;
            bool again;
            do
            {
                again = false;

                if (!p.steiner && (equals(p, p.next) || area(p.prev, p, p.next) == 0))
                {
                    removeNode(p);
                    p = end = p.prev;
                    if (p == p.next) break;
                    again = true;
                }
                else
                {
                    p = p.next;
                }

            } while (again || p != end);
            return end;
        }

        // main ear slicing loop which triangulates a polygon (given as a linked list)
        static void earcutLinked(EarcutNode ear, List<Int3> triangles, float minX, float minY, float invSize, int pass)
        {
            if (ear == null) return;

            bool hasSize = !Mathf.IsZero(invSize);

            if (pass == 0 && hasSize)
            {
                indexCurve(ear, minX, minY, invSize);
            }

            var stop = ear;

            while (ear.prev != ear.next)
            {
                var prev = ear.prev;
                var next = ear.next;

                if (hasSize ? isEarHashed(ear, minX, minY, invSize) : isEar(ear))
                {
                    // cut off the triangle
                    triangles.Add(new Int3(prev.i, ear.i, next.i));

                    removeNode(ear);

                    // skipping the next vertex leads to less sliver triangles
                    ear = next.next;
                    stop = next.next;

                    continue;
                }

                ear = next;

                // if we looped through the whole remaining polygon and can't find any more ears
                if (ear == stop)
                {
                    // try filtering points and slicing again
                    if (pass == 0)
                    {
                        earcutLinked(filterPoints(ear), triangles, minX, minY, invSize, 1);

                        // if this didn't work, try curing all small self-intersections locally
                    }
                    else if (pass == 1)
                    {
                        ear = cureLocalIntersections(filterPoints(ear), triangles);
                        earcutLinked(ear, triangles, minX, minY, invSize, 2);

                        // as a last resort, try splitting the remaining polygon into two
                    }
                    else if (pass == 2)
                    {
                        splitEarcut(ear, triangles, minX, minY, invSize);
                    }

                    break;
                }
            }
        }

        // check whether a polygon node forms a valid ear with adjacent nodes
        static bool isEar(EarcutNode ear)
        {
            var a = ear.prev;
            var b = ear;
            var c = ear.next;

            if (area(a, b, c) >= 0) return false; // reflex, can't be an ear

            // now make sure we don't have other points inside the potential ear
            var p = ear.next.next;

            while (p != ear.prev)
            {
                if (
                  pointInTriangle(a.x, a.y, b.x, b.y, c.x, c.y, p.x, p.y) &&
                  area(p.prev, p, p.next) >= 0
                )
                {
                    return false;
                }
                p = p.next;
            }

            return true;
        }

        static bool isEarHashed(EarcutNode ear, float minX, float minY, float invSize)
        {
            var a = ear.prev;
            var b = ear;
            var c = ear.next;

            if (area(a, b, c) >= 0) return false; // reflex, can't be an ear

            // triangle bbox; min & max are calculated like this for speed
            var minTX = a.x < b.x ? (a.x < c.x ? a.x : c.x) : b.x < c.x ? b.x : c.x;
            var minTY = a.y < b.y ? (a.y < c.y ? a.y : c.y) : b.y < c.y ? b.y : c.y;
            var maxTX = a.x > b.x ? (a.x > c.x ? a.x : c.x) : b.x > c.x ? b.x : c.x;
            var maxTY = a.y > b.y ? (a.y > c.y ? a.y : c.y) : b.y > c.y ? b.y : c.y;

            // z-order range for the current triangle bbox;
            var minZ = zOrder(minTX, minTY, minX, minY, invSize);
            var maxZ = zOrder(maxTX, maxTY, minX, minY, invSize);

            var p = ear.prevZ;
            var n = ear.nextZ;

            // look for points inside the triangle in both directions
            while (p != null && p.z >= minZ && n != null && n.z <= maxZ)
            {
                if (
                  p != ear.prev &&
                  p != ear.next &&
                  pointInTriangle(a.x, a.y, b.x, b.y, c.x, c.y, p.x, p.y) &&
                  area(p.prev, p, p.next) >= 0
                )
                {
                    return false;
                }
                p = p.prevZ;

                if (
                  n != ear.prev &&
                  n != ear.next &&
                  pointInTriangle(a.x, a.y, b.x, b.y, c.x, c.y, n.x, n.y) &&
                  area(n.prev, n, n.next) >= 0
                )
                {
                    return false;
                }
                n = n.nextZ;
            }

            // look for remaining points in decreasing z-order
            while (p != null && p.z >= minZ)
            {
                if (
                  p != ear.prev &&
                  p != ear.next &&
                  pointInTriangle(a.x, a.y, b.x, b.y, c.x, c.y, p.x, p.y) &&
                  area(p.prev, p, p.next) >= 0
                )
                    return false;
                p = p.prevZ;
            }

            // look for remaining points in increasing z-order
            while (n != null && n.z <= maxZ)
            {
                if (
                  n != ear.prev &&
                  n != ear.next &&
                  pointInTriangle(a.x, a.y, b.x, b.y, c.x, c.y, n.x, n.y) &&
                  area(n.prev, n, n.next) >= 0
                )
                {
                    return false;
                }
                n = n.nextZ;
            }

            return true;
        }

        // go through all polygon nodes and cure small local self-intersections
        static EarcutNode cureLocalIntersections(EarcutNode start, List<Int3> triangles)
        {
            var p = start;
            do
            {
                var a = p.prev;
                var b = p.next.next;

                if (
                  !equals(a, b) &&
                  intersects(a, p, p.next, b) &&
                  locallyInside(a, b) &&
                  locallyInside(b, a)
                )
                {
                    triangles.Add(new Int3(a.i, p.i, b.i));

                    // remove two nodes involved
                    removeNode(p);
                    removeNode(p.next);

                    p = start = b;
                }
                p = p.next;
            } while (p != start);

            return filterPoints(p);
        }

        // try splitting polygon into two and triangulate them independently
        static void splitEarcut(EarcutNode start, List<Int3> triangles, float minX, float minY, float invSize)
        {
            // look for a valid diagonal that divides the polygon into two
            var a = start;
            do
            {
                var b = a.next.next;
                while (b != a.prev)
                {
                    if (a.i != b.i && isValidDiagonal(a, b))
                    {
                        // split the polygon in two by the diagonal
                        var c = splitPolygon(a, b);

                        // filter colinear points around the cuts
                        a = filterPoints(a, a.next);
                        c = filterPoints(c, c.next);

                        // run earcut on each half
                        earcutLinked(a, triangles, minX, minY, invSize, 0);
                        earcutLinked(c, triangles, minX, minY, invSize, 0);
                        return;
                    }
                    b = b.next;
                }
                a = a.next;
            } while (a != start);
        }

        // link every hole into the outer loop, producing a single-ring polygon without holes
        static EarcutNode eliminateHoles(List<Vector2> data, List<int> holeIndices, EarcutNode outerNode)
        {
            List<EarcutNode> queue = new List<EarcutNode>();
            int len = holeIndices.Count;

            for (int i = 0; i < len; i++)
            {
                int start = holeIndices[i];
                int end = i < len - 1 ? holeIndices[i + 1] : data.Count;
                EarcutNode list = linkedList(data, start, end, false);
                if (list == list.next) list.steiner = true;
                queue.Add(getLeftmost(list));
            }

            queue.Sort(compareX);

            // process holes from left to right
            for (int i = 0; i < queue.Count; i++)
            {
                eliminateHole(queue[i], outerNode);
                outerNode = filterPoints(outerNode, outerNode.next);
            }

            return outerNode;
        }

        static int compareX(EarcutNode a, EarcutNode b)
        {
            float comp = a.x - b.x;
            return comp > 0 ? 1 : (comp < 0 ? -1 : 0);
        }

        // find a bridge between vertices that connects hole with an outer ring and and link it
        static void eliminateHole(EarcutNode hole, EarcutNode outerNode)
        {
            outerNode = findHoleBridge(hole, outerNode);
            if (outerNode != null)
            {
                var b = splitPolygon(outerNode, hole);

                // filter collinear points around the cuts
                filterPoints(outerNode, outerNode.next);
                filterPoints(b, b.next);
            }
        }

        // David Eberly's algorithm for finding a bridge between hole and outer polygon
        static EarcutNode findHoleBridge(EarcutNode hole, EarcutNode outerNode)
        {
            var p = outerNode;
            float hx = hole.x;
            float hy = hole.y;
            float qx = float.NegativeInfinity;
            EarcutNode m = null;

            // find a segment intersected by a ray from the hole's leftmost point to the left;
            // segment's endpoint with lesser x will be potential connection point
            do
            {
                if (hy <= p.y && hy >= p.next.y && p.next.y != p.y)
                {
                    var x = p.x + ((hy - p.y) * (p.next.x - p.x)) / (p.next.y - p.y);
                    if (x <= hx && x > qx)
                    {
                        qx = x;
                        if (x == hx)
                        {
                            if (hy == p.y) return p;
                            if (hy == p.next.y) return p.next;
                        }
                        m = p.x < p.next.x ? p : p.next;
                    }
                }
                p = p.next;
            } while (p != outerNode);

            if (m == null) return null;

            if (hx == qx) return m; // hole touches outer segment; pick leftmost endpoint

            // look for points inside the triangle of hole point, segment intersection and endpoint;
            // if there are no points found, we have a valid connection;
            // otherwise choose the point of the minimum angle with the ray as connection point

            var stop = m;
            float mx = m.x;
            float my = m.y;
            float tanMin = float.PositiveInfinity;
            float tan;

            p = m;

            do
            {
                if (
                  hx >= p.x &&
                  p.x >= mx &&
                  hx != p.x &&
                  pointInTriangle(
                    hy < my ? hx : qx,
                    hy,
                    mx,
                    my,
                    hy < my ? qx : hx,
                    hy,
                    p.x,
                    p.y
                  )
                )
                {
                    tan = Mathf.Abs(hy - p.y) / (hx - p.x); // tangential

                    if (
                      locallyInside(p, hole) &&
                      (tan < tanMin ||
                        (tan == tanMin &&
                          (p.x > m.x || (p.x == m.x && sectorContainsSector(m, p)))))
                    )
                    {
                        m = p;
                        tanMin = tan;
                    }
                }

                p = p.next;
            } while (p != stop);

            return m;
        }

        // whether sector in vertex m contains sector in vertex p in the same coordinates
        static bool sectorContainsSector(EarcutNode m, EarcutNode p)
        {
            return area(m.prev, m, p.prev) < 0 && area(p.next, m, m.next) < 0;
        }

        // interlink polygon nodes in z-order
        static void indexCurve(EarcutNode start, float minX, float minY, float invSize)
        {
            var p = start;
            do
            {
                if (p.z == null) p.z = zOrder(p.x, p.y, minX, minY, invSize);
                p.prevZ = p.prev;
                p.nextZ = p.next;
                p = p.next;
            } while (p != start);

            p.prevZ.nextZ = null;
            p.prevZ = null;

            sortLinked(p);
        }

        // Simon Tatham's linked list merge sort algorithm
        // http://www.chiark.greenend.org.uk/~sgtatham/algorithms/listsort.html
        static EarcutNode sortLinked(EarcutNode list)
        {
            int numMerges;
            int inSize = 1;

            do
            {
                EarcutNode p = list;
                list = null;
                EarcutNode tail = null;
                numMerges = 0;

                while (p != null)
                {
                    numMerges++;
                    var q = p;
                    int pSize = 0;
                    for (int i = 0; i < inSize; i++)
                    {
                        pSize++;
                        q = q.nextZ;
                        if (q == null) break;
                    }
                    int qSize = inSize;

                    while (pSize > 0 || (qSize > 0 && q != null))
                    {
                        EarcutNode e;
                        if (pSize != 0 && (qSize == 0 || q == null || p.z <= q.z))
                        {
                            e = p;
                            p = p.nextZ;
                            pSize--;
                        }
                        else
                        {
                            e = q;
                            q = q.nextZ;
                            qSize--;
                        }

                        if (tail != null) tail.nextZ = e;
                        else list = e;

                        e.prevZ = tail;
                        tail = e;
                    }

                    p = q;
                }

                tail.nextZ = null;
                inSize *= 2;
            } while (numMerges > 1);

            return list;
        }

        // z-order of a point given coords and inverse of the longer side of data bbox
        static int zOrder(float xCoord, float yCoord, float minX, float minY, float invSize)
        {
            // coords are transformed into non-negative 15-bit integer range
            int x = (int)(32767 * (xCoord - minX) * invSize);
            int y = (int)(32767 * (yCoord - minY) * invSize);

            x = (x | (x << 8)) & 0x00ff00ff;
            x = (x | (x << 4)) & 0x0f0f0f0f;
            x = (x | (x << 2)) & 0x33333333;
            x = (x | (x << 1)) & 0x55555555;

            y = (y | (y << 8)) & 0x00ff00ff;
            y = (y | (y << 4)) & 0x0f0f0f0f;
            y = (y | (y << 2)) & 0x33333333;
            y = (y | (y << 1)) & 0x55555555;

            return x | (y << 1);
        }

        // find the leftmost node of a polygon ring
        static EarcutNode getLeftmost(EarcutNode start)
        {
            var p = start;
            var leftmost = start;
            do
            {
                if (p.x < leftmost.x || (p.x == leftmost.x && p.y < leftmost.y))
                    leftmost = p;
                p = p.next;
            } while (p != start);

            return leftmost;
        }

        // check if a point lies within a convex triangle
        static bool pointInTriangle(float ax, float ay, float bx, float by, float cx, float cy, float px, float py)
        {
            return (
            (cx - px) * (ay - py) - (ax - px) * (cy - py) >= 0 &&
            (ax - px) * (by - py) - (bx - px) * (ay - py) >= 0 &&
            (bx - px) * (cy - py) - (cx - px) * (by - py) >= 0
          );
        }

        // check if a diagonal between two polygon nodes is valid (lies in polygon interior)
        static bool isValidDiagonal(EarcutNode a, EarcutNode b)
        {
            return (
              a.next.i != b.i &&
              a.prev.i != b.i &&
              !intersectsPolygon(a, b) && // dones't intersect other edges
              ((locallyInside(a, b) &&
              locallyInside(b, a) &&
              middleInside(a, b) && // locally visible
                (area(a.prev, a, b.prev) != 0 || area(a, b.prev, b) != 0)) || // does not create opposite-facing sectors
                (equals(a, b) &&
                  area(a.prev, a, a.next) > 0 &&
                  area(b.prev, b, b.next) > 0))
            ); // special zero-length case
        }

        // signed area of a triangle
        static float area(EarcutNode p, EarcutNode q, EarcutNode r)
        {
            return (q.y - p.y) * (r.x - q.x) - (q.x - p.x) * (r.y - q.y);
        }

        // check if two points are equal
        static bool equals(EarcutNode p1, EarcutNode p2)
        {
            return p1.x == p2.x && p1.y == p2.y;
        }

        // check if two segments intersect
        static bool intersects(EarcutNode p1, EarcutNode q1, EarcutNode p2, EarcutNode q2)
        {
            var o1 = Mathf.Sign(area(p1, q1, p2));
            var o2 = Mathf.Sign(area(p1, q1, q2));
            var o3 = Mathf.Sign(area(p2, q2, p1));
            var o4 = Mathf.Sign(area(p2, q2, q1));

            if (o1 != o2 && o3 != o4) return true; // general case

            if (o1 == 0 && onSegment(p1, p2, q1)) return true; // p1, q1 and p2 are collinear and p2 lies on p1q1
            if (o2 == 0 && onSegment(p1, q2, q1)) return true; // p1, q1 and q2 are collinear and q2 lies on p1q1
            if (o3 == 0 && onSegment(p2, p1, q2)) return true; // p2, q2 and p1 are collinear and p1 lies on p2q2
            if (o4 == 0 && onSegment(p2, q1, q2)) return true; // p2, q2 and q1 are collinear and q1 lies on p2q2

            return false;
        }

        // for collinear points p, q, r, check if point q lies on segment pr
        static bool onSegment(EarcutNode p, EarcutNode q, EarcutNode r)
        {
            return (
                q.x <= Mathf.Max(p.x, r.x) &&
                q.x >= Mathf.Min(p.x, r.x) &&
                q.y <= Mathf.Max(p.y, r.y) &&
                q.y >= Mathf.Min(p.y, r.y)
              );
        }

        // check if a polygon diagonal intersects any polygon segments
        static bool intersectsPolygon(EarcutNode a, EarcutNode b)
        {
            var p = a;
            do
            {
                if (
                  p.i != a.i &&
                  p.next.i != a.i &&
                  p.i != b.i &&
                  p.next.i != b.i &&
                  intersects(p, p.next, a, b)
                )
                    return true;
                p = p.next;
            } while (p != a);

            return false;
        }

        // check if a polygon diagonal is locally inside the polygon
        static bool locallyInside(EarcutNode a, EarcutNode b)
        {
            return area(a.prev, a, a.next) < 0
              ? area(a, b, a.next) >= 0 && area(a, a.prev, b) >= 0
              : area(a, b, a.prev) < 0 || area(a, a.next, b) < 0;
        }

        // check if the middle point of a polygon diagonal is inside the polygon
        static bool middleInside(EarcutNode a, EarcutNode b)
        {
            var p = a;
            bool inside = false;
            float px = (a.x + b.x) / 2;
            float py = (a.y + b.y) / 2;
            do
            {
                if (
                  p.y > py != p.next.y > py &&
                  p.next.y != p.y &&
                  px < ((p.next.x - p.x) * (py - p.y)) / (p.next.y - p.y) + p.x
                )
                {
                    inside = !inside;
                }
                p = p.next;
            } while (p != a);

            return inside;
        }

        // link two polygon vertices with a bridge; if the vertices belong to the same ring, it splits polygon into two;
        // if one belongs to the outer ring and another to a hole, it merges it into a single ring
        static EarcutNode splitPolygon(EarcutNode a, EarcutNode b)
        {
            var a2 = new EarcutNode(a.i, a.x, a.y);
            var b2 = new EarcutNode(b.i, b.x, b.y);
            var an = a.next;
            var bp = b.prev;

            a.next = b;
            b.prev = a;

            a2.next = an;
            an.prev = a2;

            b2.next = a2;
            a2.prev = b2;

            bp.next = b2;
            b2.prev = bp;

            return b2;
        }

        // create a node and optionally link it with previous one (in a circular doubly linked list)
        static EarcutNode insertNode(int i, float x, float y, EarcutNode last)
        {
            if (last == null)
            {
                var p = new EarcutNode(i, x, y);
                p.prev = p;
                p.next = p;
                return p;
            }
            else
            {
                var p = new EarcutNode(i, x, y);
                p.next = last.next;
                p.prev = last;
                last.next.prev = p;
                last.next = p;
                return p;
            }
        }

        static void removeNode(EarcutNode p)
        {
            p.next.prev = p.prev;
            p.prev.next = p.next;

            if (p.prevZ != null) p.prevZ.nextZ = p.nextZ;
            if (p.nextZ != null) p.nextZ.prevZ = p.prevZ;
        }

        static float signedArea(List<Vector2> data, int start, int end)
        {
            float sum = 0;
            for (int i = start, j = end - 1; i < end; i++)
            {
                sum += (data[j].X - data[i].X) * (data[i].Y + data[j].Y);
                j = i;
            }
            return sum;
        }

        public class EarcutNode
        {
            public int i;
            public float x;
            public float y;

            public EarcutNode prev;
            public EarcutNode next;
            public bool steiner;

            public int? z;
            public EarcutNode prevZ;
            public EarcutNode nextZ;

            public EarcutNode(int i, float x, float y)
            {

                // vertex index in coordinates array
                this.i = i;

                // vertex coordinates
                this.x = x;
                this.y = y;

                // previous and next vertex nodes in a polygon ring
                // make sure to set them!
                this.prev = null;
                this.next = null;

                // z-order curve value
                this.z = null;

                // previous and next nodes in z-order
                this.prevZ = null;
                this.nextZ = null;

                // indicates whether this is a steiner point
                this.steiner = false;
            }
        }
    }
}
