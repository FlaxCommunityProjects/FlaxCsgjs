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
        private CsgjsNodeType _cachedNodeType;
        private Transform _cachedTransform;
        private Vector3 _cachedCenter;
        private float _cachedRadius;
        private Csgjs _localCsgNode;
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

        [EditorOrder(12)]
        [VisibleIf(nameof(IsModel))]
        public float Radius = 100;

        public bool IsRoot => NodeType == CsgjsNodeType.Root;
        public bool IsModel => NodeType != CsgjsNodeType.Root;

        public override void OnEnable()
        {
            var parentScript = Actor.Parent?.GetScript<CsgjsScript>();
            if (parentScript)
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
        }

        public Csgjs GetCsgNode()
        {
            if (HasChanged())
            {
                _localCsgNode = CreateCsgNode();
            }

            return _localCsgNode;
        }

        private bool HasChanged()
        {
            return _cachedNodeType != NodeType ||
                _cachedTransform != Actor.Transform ||
                _cachedRadius != Radius ||
                _cachedCenter != Center;
        }

        private Csgjs _csgNode = null;

        public void Execute()
        {
            if (NodeType == CsgjsNodeType.Root)
            {
                var csgResult = DoCsg();
                _csgNode = csgResult;

                if (csgResult.Polygons.Count == 0)
                {
                    _virtualModelActor.Entries[0].Visible = false;
                }
                else
                {
                    _virtualModelActor.Entries[0].Visible = true;

                    var mesh = _virtualModel.LODs[0].Meshes[0];
                    Triangulate(csgResult, mesh);
                }
            }
        }

        /*
        public override void OnDebugDraw()
        {
            _csgNode?.Polygons?.ForEach(p => p?.Vertices.ForEach(v =>
            {
                BoundingSphere sphere = new BoundingSphere(v.Position, 1);
                DebugDraw.DrawSphere(sphere, Color.Blue);
            }));
        }*/


        private Csgjs CreateCsgNode()
        {
            _cachedNodeType = NodeType;
            _cachedTransform = Actor.Transform;
            _cachedRadius = Radius;
            _cachedCenter = Center;

            Csgjs csg = null;
            if (NodeType == CsgjsNodeType.Root)
            {
                csg = new Csgjs();
            }
            else if (NodeType == CsgjsNodeType.Cube)
            {
                csg = Csgjs.CreateCube(Center, Radius);
            }
            else if (NodeType == CsgjsNodeType.Sphere)
            {
                csg = Csgjs.CreateSphere(Center, Radius);
            }
            else if (NodeType == CsgjsNodeType.Cylinder)
            {
                csg = Csgjs.CreateCylinder(Center - Vector3.UnitY * 50, Center + Vector3.UnitY * 50, Radius);
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

        // TODO: more and better caching
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
            List<int> triangles = new List<int>();

            for (int i = 0; i < csgNode.Polygons.Count; i++)
            {
                int offset = vertices.Count;

                Vector3 origin = csgNode.Polygons[i].Vertices[0].Position;
                Vector3 normal = csgNode.Polygons[i].Plane.Normal;
                Vector3 pointOnPlane = csgNode.Polygons[i].Vertices[1].Position;

                vertices.AddRange(csgNode.Polygons[i].Vertices.Select(v => v.Position));
                normals.AddRange(csgNode.Polygons[i].Vertices.Select(v => v.Normal));


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

            mesh.UpdateMesh(vertices, triangles, normals);
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
