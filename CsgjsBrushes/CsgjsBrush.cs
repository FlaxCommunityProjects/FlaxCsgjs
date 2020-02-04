using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FlaxCsgjs.Source;
using FlaxEngine;

namespace FlaxCsgjs.Source
{
    public abstract class CsgjsBrush
    {
        public class CsgjsBrushSurface
        {
            public CsgjsBrushSurface(string name, CsgjsScript script)
            {
                Name = name;
                Script = script;
            }

            [HideInEditor]
            public CsgjsScript Script { get; }

            [ShowInEditor]
            [Serialize]
            [EditorOrder(1)]
            public string Name { get; }

            [EditorOrder(2)]
            public MaterialBase Material { get; set; }
        }

        public static readonly Color SelectionColor = Color.LightGreen;

        private Vector3 _size = new Vector3(100, 100, 100);
        private Vector3 _center;
        private Transform _transform;
        private bool _hasChanged = true;
        private Csgjs _csg;

        public CsgjsBrush(CsgjsScript csgjsScript)
        {
            CsgjsScript = csgjsScript;
        }

        [DefaultValue(typeof(Vector3), "100,100,100")]
        [EditorOrder(10)]
        [Tooltip("CSG brush size")]
        public Vector3 Size
        {
            get { return _size; }
            set { _size = value; _hasChanged = true; }
        }

        [DefaultValue(typeof(Vector3), "0,0,0")]
        [EditorOrder(11)]
        [Tooltip("CSG brush center location (in local space)")]
        public Vector3 Center
        {
            get { return _center; }
            set { _center = value; _hasChanged = true; }
        }

        [EditorDisplay("Surfaces", EditorDisplayAttribute.InlineStyle)]
        [EditorOrder(100)]
        [MemberCollection(CanReorderItems = false, NotNullItems = true, ReadOnly = true)]
        [Serialize]
        [ExpandGroups]
        public CsgjsBrushSurface[] Surfaces { get; protected set; }

        [HideInEditor]
        public CsgjsScript CsgjsScript { get; }

        [HideInEditor]
        public Vector3 HalfSize => Size * 0.5f;

        [HideInEditor]
        public bool HasChanged => _hasChanged || _transform != CsgjsScript.Actor.Transform;

        [HideInEditor]
        public OrientedBoundingBox OrientedBox => new OrientedBoundingBox(HalfSize, Matrix.Translation(Center) * CsgjsScript.Actor.LocalToWorldMatrix);

        public abstract void OnDebugDraw();

        protected abstract Csgjs Create(out List<Csgjs.CsgSurfaceSharedData> surfaces);

        public Csgjs GetCsg()
        {
            if (HasChanged)
            {
                _hasChanged = false;
                _transform = CsgjsScript.Actor.Transform;
                _csg = Create(out var surfaces);

                if (surfaces != null)
                {
                    for (int i = 0; i < surfaces.Count; i++)
                    {
                        for (int j = 0; j < Surfaces.Length; j++)
                        {
                            if (surfaces[i].Name == Surfaces[j].Name)
                            {
                                surfaces[i].SurfaceData = Surfaces[j];
                                break;
                            }
                        }
                    }
                }

                _csg.Polygons.ForEach(p =>
                {
                    Plane plane = new Plane(p.Plane.Normal, p.Plane.W);
                    plane = LocalToWorldPlane(ref _transform, plane);

                    p.Plane.Normal = plane.Normal;
                    p.Plane.W = plane.D;

                    // Vertices cannot be shared
                    p.Vertices.ForEach(v =>
                    {
                        v.Position = _transform.TransformPoint(v.Position);
                        v.Normal = LocalToWorldNormal(ref _transform, v.Normal);
                    });
                });
            }

            return _csg;
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
