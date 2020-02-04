using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FlaxEngine;

namespace FlaxCsgjs.Source
{
    public static class CustomDebugDraw
    {
        // TODO: Remove this once the FlaxEngine supports it
        public static void DrawEllipse(Vector3 center, Quaternion orientation, Vector2 size, Color color, float duration = 0, bool depthTest = true)
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
    }
}
