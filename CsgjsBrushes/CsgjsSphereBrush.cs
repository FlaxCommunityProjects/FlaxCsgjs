using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FlaxEngine;

namespace FlaxCsgjs.Source
{
    public class CsgjsSphereBrush : CsgjsBrush
    {
        public CsgjsSphereBrush(CsgjsScript csgjsScript) : base(csgjsScript)
        {
            Surfaces = new[]
            {
                new CsgjsBrushSurface("Sphere", csgjsScript)
            };
        }

        public override void OnDebugDraw()
        {
            Transform transform = CsgjsScript.Actor.Transform;
            Vector3 halfSize = HalfSize;

            Vector3 center = transform.TransformPoint(Center);

            Vector3 size = halfSize * transform.Scale;
            Vector2 sizeXY = new Vector2(size.X, size.Y);
            Vector2 sizeYZ = new Vector2(size.Y, size.Z);
            Vector2 sizeXZ = new Vector2(size.X, size.Z);

            CustomDebugDraw.DrawEllipse(center, transform.Orientation, sizeXZ, SelectionColor, 0, false);
            CustomDebugDraw.DrawEllipse(center, transform.Orientation * Quaternion.RotationZ(Mathf.PiOverTwo), sizeYZ, SelectionColor, 0, false);
            CustomDebugDraw.DrawEllipse(center, transform.Orientation * Quaternion.RotationX(Mathf.PiOverTwo), sizeXY, SelectionColor, 0, false);
        }

        protected override Csgjs Create(out List<Csgjs.CsgSurfaceSharedData> surfaces)
        {
            return Csgjs.CreateSphere(Center, Size, out surfaces);
        }
    }
}
