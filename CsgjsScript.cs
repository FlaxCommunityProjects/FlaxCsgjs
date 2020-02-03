using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FlaxEngine;

namespace FlaxCsgjs.Source
{
    //[ExecuteInEditMode]
    public class CsgjsScript : Script
    {
        private static readonly Color _selectedColor = Color.LightGreen;

        private CsgjsNodeType _cachedNodeType;
        private Transform _cachedTransform;
        private Vector3 _cachedCenter;
        private Vector3 _cachedSize;
        private MaterialBase _cachedMaterial;
        private Csgjs _localCsgNode;
        private Csgjs _combinedCsgNode;
        private StaticModel _virtualModelActor;
        private Model _virtualModel;

        public enum CsgjsActionType
        {
            Union,
            Subtract,
            Intersect
        }

        public enum CsgjsNodeType
        {
            Root,
            Cube,
            Sphere,
            Cylinder
        }

        [EditorOrder(0)]
        public CsgjsActionType ActionType;

        [EditorOrder(10)]
        public CsgjsNodeType NodeType;

        [EditorOrder(11)]
        [VisibleIf(nameof(IsModel))]
        public Vector3 Center;

        public bool Raycast(ref Ray mouseRay, out float distance, out CsgjsScript script)
        {
            Csgjs csgNode;
            if (IsRoot)
            {
                csgNode = _combinedCsgNode;
            }
            else if (IsModel)
            {
                csgNode = _localCsgNode;
            }
            else
            {
                throw new NotSupportedException();
            }

            script = null;
            distance = float.PositiveInfinity;
            for (int i = 0; i < csgNode.Polygons.Count; i++)
            {
                if (csgNode.Polygons[i].Intersects(ref mouseRay, out float intersectionDistance) && intersectionDistance <= distance)
                {
                    distance = intersectionDistance;
                    script = csgNode.Polygons[i].Shared.Actor.GetScript<CsgjsScript>();
                }
            }

            return script != null;
        }

        [EditorOrder(12)]
        [VisibleIf(nameof(IsModel))]
        public Vector3 Size = new Vector3(100);

        [EditorOrder(13)]
        [VisibleIf(nameof(IsModel))]
        public MaterialBase Material;

        public bool IsRoot => NodeType == CsgjsNodeType.Root;
        public bool IsModel => NodeType != CsgjsNodeType.Root;

        public override void OnEnable()
        {
            var parentScript = Actor.Parent?.GetScript<CsgjsScript>();
            if (parentScript && NodeType == CsgjsNodeType.Root)
            {
                NodeType = CsgjsNodeType.Cube;
            }

            if (IsRoot && !_virtualModel)
            {
                // Create dynamic model with a single LOD with one mesh
                _virtualModel = Content.CreateVirtualAsset<Model>();
                _virtualModel.SetupLODs(1);

                _virtualModelActor = Actor.GetOrAddChild<StaticModel>();
                _virtualModelActor.HideFlags = HideFlags.DontSave;
                _virtualModelActor.Model = _virtualModel;
            }
        }

        public override void OnUpdate()
        {
            Execute();
        }

        public override void OnDisable()
        {
            Destroy(ref _virtualModelActor);
            Destroy(ref _virtualModel);
            _localCsgNode = null;
            _combinedCsgNode = null;
        }

        public Csgjs GetCsgNode()
        {
            if (HasChanged())
            {
                // TODO: Make it possible to individually set the surface materials
                // For that, we'll probably have to serialize the materials + names? without serializing the surfaces?
                _localCsgNode = CreateCsgNode(out var surfaces);
            }

            return _localCsgNode;
        }

        private bool HasChanged()
        {
            return _localCsgNode == null ||
                _cachedNodeType != NodeType ||
                _cachedTransform != Actor.Transform ||
                _cachedSize != Size ||
                _cachedCenter != Center ||
                _cachedMaterial != Material;
        }

        public void Execute()
        {
            if (NodeType == CsgjsNodeType.Root)
            {
                var csgResult = DoCsg();
                _combinedCsgNode = csgResult;

                if (csgResult.Polygons.Count == 0)
                {
                    _virtualModelActor.Entries[0].Visible = false;
                }
                else
                {
                    // TODO: Remove this very bad solution. We currently have a FlaxEngine bug with model picking
                    _virtualModel.SetupLODs(1);

                    _virtualModelActor.Entries[0].Visible = true;
                    var mesh = _virtualModel.LODs[0].Meshes[0];
                    Triangulate(csgResult, mesh);
                }
            }
        }

        /*
        public override void OnDebugDraw()
        {
            _combinedCsgNode?.Polygons?.ForEach(p => p?.Vertices.ForEach(v =>
            {
                BoundingSphere sphere = new BoundingSphere(v.Position, 1);
                DebugDraw.DrawSphere(sphere, Color.Blue);
            }));
        }
        */

        public override void OnDebugDrawSelected()
        {
            Vector3 halfSize = Size * 0.5f;
            if (NodeType == CsgjsNodeType.Root)
            {

            }
            else if (NodeType == CsgjsNodeType.Cube)
            {
                OrientedBoundingBox box = new OrientedBoundingBox(halfSize, Actor.LocalToWorldMatrix);
                DebugDraw.DrawWireBox(box, _selectedColor, 0, false);
            }
            else if (NodeType == CsgjsNodeType.Sphere)
            {
                Transform transform = Actor.Transform;
                Vector3 center = transform.TransformPoint(Center);

                Vector3 size = halfSize * transform.Scale;
                Vector2 sizeXY = new Vector2(size.X, size.Y);
                Vector2 sizeYZ = new Vector2(size.Y, size.Z);
                Vector2 sizeXZ = new Vector2(size.X, size.Z);

                DrawEllipse(center, transform.Orientation, sizeXZ, _selectedColor, 0, false);
                DrawEllipse(center, transform.Orientation * Quaternion.RotationZ(Mathf.PiOverTwo), sizeYZ, _selectedColor, 0, false);
                DrawEllipse(center, transform.Orientation * Quaternion.RotationX(Mathf.PiOverTwo), sizeXY, _selectedColor, 0, false);
            }
            else if (NodeType == CsgjsNodeType.Cylinder)
            {
                Transform transform = Actor.Transform;
                Vector3 top = transform.TransformPoint(Center + Vector3.UnitY * halfSize.Y);
                Vector3 bottom = transform.TransformPoint(Center - Vector3.UnitY * halfSize.Y);
                Vector2 ellipseSize = new Vector2(halfSize.X * transform.Scale.X, halfSize.Z * transform.Scale.Z);

                DrawEllipse(top, transform.Orientation, ellipseSize, _selectedColor, 0, false);
                DrawEllipse(bottom, transform.Orientation, ellipseSize, _selectedColor, 0, false);

                Vector3 axisX = Vector3.UnitX * ellipseSize.X * transform.Orientation;
                Vector3 axisZ = Vector3.UnitZ * ellipseSize.Y * transform.Orientation;
                DebugDraw.DrawLine(top + axisX, bottom + axisX, _selectedColor, 0, false);
                DebugDraw.DrawLine(top - axisX, bottom - axisX, _selectedColor, 0, false);
                DebugDraw.DrawLine(top + axisZ, bottom + axisZ, _selectedColor, 0, false);
                DebugDraw.DrawLine(top - axisZ, bottom - axisZ, _selectedColor, 0, false);
            }
        }

        private static void DrawEllipse(Vector3 center, Quaternion orientation, Vector2 size, Color color, float duration = 0, bool depthTest = true)
        {
            Vector3 axisX = Vector3.UnitX * size.X * orientation;
            Vector3 axisZ = Vector3.UnitZ * size.Y * orientation;

            Vector3 GetPoint(float slice)
            {
                var angle = slice * Mathf.Pi * 2;
                return center + axisX * Mathf.Cos(angle) + axisZ * Mathf.Sin(angle);
            }

            const float slices = 16;
            for (var i = 0; i < slices; i++)
            {
                float t0 = i / slices;
                float t1 = (i + 1) / slices;

                DebugDraw.DrawLine(GetPoint(t0), GetPoint(t1), color, duration, depthTest);
            }
        }


        private Csgjs CreateCsgNode(out List<Csgjs.CsgSurfaceSharedData> surfaces)
        {
            _cachedNodeType = NodeType;
            _cachedTransform = Actor.Transform;
            _cachedSize = Size;
            _cachedCenter = Center;
            _cachedMaterial = Material;

            Csgjs csg = null;
            if (NodeType == CsgjsNodeType.Root)
            {
                surfaces = null;
                csg = new Csgjs();
            }
            else if (NodeType == CsgjsNodeType.Cube)
            {
                csg = Csgjs.CreateCube(Center, Size, out surfaces);
            }
            else if (NodeType == CsgjsNodeType.Sphere)
            {
                csg = Csgjs.CreateSphere(Center, Size, out surfaces);
            }
            else if (NodeType == CsgjsNodeType.Cylinder)
            {
                csg = Csgjs.CreateCylinder(Center + Vector3.UnitY, Center - Vector3.UnitY, Size, out surfaces);
            }
            else
            {
                throw new NotSupportedException("Unknown " + nameof(NodeType));
            }

            if (surfaces != null)
            {
                for (int i = 0; i < surfaces.Count; i++)
                {
                    surfaces[i].Actor = Actor;
                    surfaces[i].Material = Material;
                }
            }

            Transform transform = Actor.Transform;
            csg.Polygons.ForEach(p =>
            {
                Plane plane = new Plane(p.Plane.Normal, p.Plane.W);
                plane = LocalToWorldPlane(ref transform, plane);

                p.Plane.Normal = plane.Normal;
                p.Plane.W = plane.D;

                // Vertices cannot be shared
                p.Vertices.ForEach(v =>
                {
                    v.Position = transform.TransformPoint(v.Position);
                    v.Normal = LocalToWorldNormal(ref transform, v.Normal);
                });
            });

            return csg;
        }

        public Csgjs DoCsg()
        {
            Csgjs csgResult = GetCsgNode();

            for (int i = 0; i < Actor.ChildrenCount; i++)
            {
                var childScript = Actor.Children[i].GetScript<CsgjsScript>();
                if (childScript && childScript.Enabled && childScript.Actor.IsActiveInHierarchy && childScript.IsModel)
                {
                    var childCsgNode = childScript.DoCsg();

                    // Do csg between myself and the child
                    if (childScript.ActionType == CsgjsActionType.Union)
                    {
                        csgResult = csgResult.Union(childCsgNode);
                    }
                    else if (childScript.ActionType == CsgjsActionType.Subtract)
                    {
                        csgResult = csgResult.Subtract(childCsgNode);
                    }
                    else if (childScript.ActionType == CsgjsActionType.Intersect)
                    {
                        csgResult = csgResult.Intersect(childCsgNode);
                    }
                }
            }

            return csgResult;
        }

        public static void Triangulate(Csgjs csgNode, Mesh mesh)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> triangles = new List<int>();

            for (int i = 0; i < csgNode.Polygons.Count; i++)
            {
                int offset = vertices.Count;

                Vector3 origin = csgNode.Polygons[i].Vertices[0].Position;
                Vector3 normal = csgNode.Polygons[i].Plane.Normal;
                Vector3 pointOnPlane = csgNode.Polygons[i].Vertices[1].Position;

                vertices.AddRange(csgNode.Polygons[i].Vertices.Select(v => v.Position));
                normals.AddRange(csgNode.Polygons[i].Vertices.Select(v => v.Normal));
                uvs.AddRange(csgNode.Polygons[i].Vertices.Select(v => v.Uv));


                List<Vector2> verts = csgNode.Polygons[i].Vertices
                    .Select(v => ToLocalPosition(v.Position, origin, normal, pointOnPlane))
                    .ToList();

                List<Int3> tris = Earcutjs.Earcut(verts);
                for (int j = 0; j < tris.Count; j++)
                {
                    triangles.Add(tris[j].X + offset);
                    // Flip the winding order
                    triangles.Add(tris[j].Z + offset);
                    triangles.Add(tris[j].Y + offset);
                }
            }

            mesh.UpdateMesh(vertices, triangles, normals, null, uvs);
        }

        // Taken from EdgeLoopPiece.cs
        public static Vector3 ToGlobalPosition(Vector2 localPosition, Vector3 origin, Vector3 normal, Vector3 pointOnPlane)
        {
            // Direction X and Y vectors
            Vector3 directionX = pointOnPlane - origin;
            directionX.Normalize();
            // Perpendicular to directionX
            Vector3.Cross(ref directionX, ref normal, out Vector3 directionY);

            return origin + localPosition.X * directionX + localPosition.Y * directionY;
        }

        public static Vector2 ToLocalPosition(Vector3 globalPosition, Vector3 origin, Vector3 normal, Vector3 pointOnPlane)
        {
            // Direction X and Y vectors
            Vector3 directionX = pointOnPlane - origin;
            directionX.Normalize();
            // Perpendicular to directionX
            Vector3.Cross(ref directionX, ref normal, out Vector3 directionY);

            // Project it onto the direction vectors
            Vector3 localPosition = globalPosition - origin;
            return new Vector2(
                Vector3.Dot(ref localPosition, ref directionX),
                Vector3.Dot(ref localPosition, ref directionY)
            );
        }

        // Taken form HalfMeshInstance.cs
        public static Vector3 LocalToWorldNormal(ref Transform transform, Vector3 normal)
        {
            Vector3 invScale = transform.Scale;
            if (invScale.X != 0.0f) invScale.X = 1.0f / invScale.X;
            if (invScale.Y != 0.0f) invScale.Y = 1.0f / invScale.Y;
            if (invScale.Z != 0.0f) invScale.Z = 1.0f / invScale.Z;

            Vector3 result = Vector3.Transform(normal, transform.Orientation);
            Vector3.Multiply(ref result, ref invScale, out result);
            result.Normalize();

            return result;
        }

        public static Plane LocalToWorldPlane(ref Transform transform, Plane plane)
        {
            Vector3 point = plane.Normal * plane.D;
            Plane transformedPlane = new Plane(transform.LocalToWorld(point), LocalToWorldNormal(ref transform, plane.Normal));
            transformedPlane.D *= -1f;
            return transformedPlane;
        }
    }
}
