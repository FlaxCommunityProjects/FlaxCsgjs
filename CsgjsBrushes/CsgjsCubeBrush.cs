using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FlaxCsgjs.Source;
using FlaxEngine;

namespace FlaxCsgjs.Source
{
    public class CsgjsCubeBrush : CsgjsBrush
    {
        public CsgjsCubeBrush(CsgjsScript csgjsScript) : base(csgjsScript)
        {
            Surfaces = new[]
            {
                new CsgjsBrushSurface("Top", csgjsScript),
                new CsgjsBrushSurface("Bottom", csgjsScript),
                new CsgjsBrushSurface("Left", csgjsScript),
                new CsgjsBrushSurface("Right", csgjsScript),
                new CsgjsBrushSurface("Front", csgjsScript),
                new CsgjsBrushSurface("Back", csgjsScript)
            };
        }

        protected override Csgjs Create(out List<Csgjs.CsgSurfaceSharedData> surfaces)
        {
            return Csgjs.CreateCube(Center, Size, out surfaces);
        }

        public override void OnDebugDraw()
        {
            var box = OrientedBox;
            DebugDraw.DrawWireBox(box, SelectionColor, 0, false);
        }
    }
}
