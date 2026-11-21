using System.Numerics;

namespace CoreBi.Engine.Performance.Graphics;

internal static class MeshPrimitives
{
    public static void BuildTorus(int segments, int sides, float majorRadius, float minorRadius,
                                  Action<Vector3, Vector3, Vector2> addVertex,
                                  Action<uint, uint, uint> addTriangle)
    {
        for (int i = 0; i <= segments; i++)
        {
            float u = MathF.Tau * i / segments; // mesma ordem de operações da cena de ray tracing original (nota comparável)
            float cu = MathF.Cos(u), su = MathF.Sin(u);
            var center = new Vector3(cu * majorRadius, 0, su * majorRadius);
            for (int j = 0; j <= sides; j++)
            {
                float v = MathF.Tau * j / sides;
                var normal = new Vector3(cu * MathF.Cos(v), MathF.Sin(v), su * MathF.Cos(v));
                addVertex(center + normal * minorRadius, normal, new Vector2(i / (float)segments, j / (float)sides));
            }
        }
        uint stride = (uint)(sides + 1);
        for (int i = 0; i < segments; i++)
        {
            for (int j = 0; j < sides; j++)
            {
                uint a = (uint)i * stride + (uint)j;
                uint b = a + stride;
                addTriangle(a, b, a + 1);
                addTriangle(a + 1, b, b + 1);
            }
        }
    }
}
