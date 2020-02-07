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
        private Csgjs _combinedCsg;
        private StaticModel _virtualModelActor;
        private Model _virtualModel;
        [Serialize]
        private CsgjsNodeType _nodeType;

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
        public CsgjsActionType ActionType { get; set; }

        [EditorOrder(10)]
        [NoSerialize]
        public CsgjsNodeType NodeType
        {
            get { return _nodeType; }
            set
            {
                _nodeType = value;

                // TODO: Refactor this
                if (_nodeType == CsgjsNodeType.Root)
                {

                }
                else if (_nodeType == CsgjsNodeType.Cube)
                {
                    Brush = new CsgjsCubeBrush(this);
                }
                else if (_nodeType == CsgjsNodeType.Sphere)
                {
                    Brush = new CsgjsSphereBrush(this);
                }
                else if (_nodeType == CsgjsNodeType.Cylinder)
                {
                    Brush = new CsgjsCylinderBrush(this);
                }
            }
        }

        [EditorOrder(20)]
        [Serialize]
        [EditorDisplay("Brush", EditorDisplayAttribute.InlineStyle)]
        [ExpandGroups]
        public CsgjsBrush Brush { get; set; }

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
            _combinedCsg = null;
        }

        public void Execute()
        {
            if (NodeType == CsgjsNodeType.Root)
            {
                var csgResult = DoCsg();
                _combinedCsg = csgResult;

                if (csgResult.Polygons.Count == 0)
                {
                    for (int i = 0; i < _virtualModelActor.Entries.Length; i++)
                    {
                        _virtualModelActor.Entries[i].Visible = false;
                    }
                }
                else
                {
                    for (int i = 0; i < _virtualModelActor.Entries.Length; i++)
                    {
                        _virtualModelActor.Entries[i].Visible = true;
                    }
                    Triangulate(csgResult, _virtualModel);
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
            Brush?.OnDebugDraw();
        }

        public bool Raycast(ref Ray mouseRay, out float distance, out CsgjsScript script)
        {
            Csgjs csgNode;
            if (IsRoot)
            {
                csgNode = _combinedCsg;
            }
            else if (IsModel)
            {
                csgNode = Brush.GetCsg();
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
                    script = (csgNode.Polygons[i].Shared.SurfaceData as CsgjsBrush.CsgjsBrushSurface)?.Script;
                }
            }

            return script != null;
        }

        public Csgjs DoCsg()
        {
            Csgjs csgResult = Brush?.GetCsg() ?? new Csgjs();

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

        public static void Triangulate(Csgjs csgNode, Model model)
        {
            HashSet<MaterialBase> materials = new HashSet<MaterialBase>();
            for (int i = 0; i < csgNode.Polygons.Count; i++)
            {
                if (csgNode.Polygons[i].Shared.SurfaceData is CsgjsBrush.CsgjsBrushSurface surface)
                {
                    materials.Add(surface.Material);
                }
                else
                {
                    materials.Add(null);
                }
            }

            // TODO: This could be better?
            model.SetupLODs(materials.Count);
            model.SetupMaterialSlots(materials.Count);

            int meshIndex = 0;
            foreach (var material in materials)
            {
                List<Vector3> vertices = new List<Vector3>();
                List<Vector3> normals = new List<Vector3>();
                List<Vector2> uvs = new List<Vector2>();
                List<int> triangles = new List<int>();

                for (int i = 0; i < csgNode.Polygons.Count; i++)
                {
                    var polygon = csgNode.Polygons[i];
                    var polygonMaterial = (csgNode.Polygons[i].Shared.SurfaceData as CsgjsBrush.CsgjsBrushSurface)?.Material;
                    if (material != polygonMaterial)
                    {
                        continue;
                    }

                    int offset = vertices.Count;

                    Vector3 origin = polygon.Vertices[0].Position;
                    Vector3 normal = polygon.Plane.Normal;
                    Vector3 pointOnPlane = polygon.Vertices[1].Position;

                    vertices.AddRange(polygon.Vertices.Select(v => v.Position));
                    normals.AddRange(polygon.Vertices.Select(v => v.Normal));
                    uvs.AddRange(polygon.Vertices.Select(v => v.Uv));


                    List<Vector2> verts = polygon.Vertices
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

                model.LODs[0].Meshes[meshIndex].UpdateMesh(vertices, triangles, normals, null, uvs);
                model.LODs[0].Meshes[meshIndex].MaterialSlotIndex = meshIndex;
                model.MaterialSlots[meshIndex].Material = material;
                meshIndex++;
            }
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
    }
}
