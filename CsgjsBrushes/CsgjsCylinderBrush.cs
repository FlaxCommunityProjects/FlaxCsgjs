using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FlaxCsgjs.Source;
using FlaxEngine;

namespace FlaxCsgjs.Source
{
    public class CsgjsCylinderBrush : CsgjsBrush
    {
        public CsgjsCylinderBrush(CsgjsScript csgjsScript) : base(csgjsScript)
        {
            Surfaces = new[]
            {
                new CsgjsBrushSurface("Top", csgjsScript),
                new CsgjsBrushSurface("Side", csgjsScript),
                new CsgjsBrushSurface("Bottom", csgjsScript)
            };
        }

        public override void OnDebugDraw()
        {
            Transform transform = CsgjsScript.Actor.Transform;
            Vector3 halfSize = HalfSize;

            Vector3 top = transform.TransformPoint(Center + Vector3.UnitY * halfSize.Y);
            Vector3 bottom = transform.TransformPoint(Center - Vector3.UnitY * halfSize.Y);
            Vector2 ellipseSize = new Vector2(halfSize.X * transform.Scale.X, halfSize.Z * transform.Scale.Z);

            CustomDebugDraw.DrawEllipse(top, transform.Orientation, ellipseSize, SelectionColor, 0, false);
            CustomDebugDraw.DrawEllipse(bottom, transform.Orientation, ellipseSize, SelectionColor, 0, false);

            Vector3 axisX = Vector3.UnitX * ellipseSize.X * transform.Orientation;
            Vector3 axisZ = Vector3.UnitZ * ellipseSize.Y * transform.Orientation;
            DebugDraw.DrawLine(top + axisX, bottom + axisX, SelectionColor, 0, false);
            DebugDraw.DrawLine(top - axisX, bottom - axisX, SelectionColor, 0, false);
            DebugDraw.DrawLine(top + axisZ, bottom + axisZ, SelectionColor, 0, false);
            DebugDraw.DrawLine(top - axisZ, bottom - axisZ, SelectionColor, 0, false);
        }

        protected override Csgjs Create(out List<Csgjs.CsgSurfaceSharedData> surfaces)
        {
            return Csgjs.CreateCylinder(Center + Vector3.UnitY, Center - Vector3.UnitY, Size, out surfaces);
        }
    }
}
