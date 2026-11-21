using System.Numerics;
using System.Runtime.InteropServices;

namespace CoreBi.Engine.Performance.Benchmarks;

/// <summary>Geometria procedural e trajeto de câmera da cena de benchmark Direct3D 11.</summary>
internal static class GameScene
{
    public const int TerrainGrid = 512;
    public const float TerrainSize = 800f;
    public const float WaterY = 6f;
    public const int TreeCount = 40_000;

    [StructLayout(LayoutKind.Sequential)]
    public struct Vertex
    {
        public Vector3 Pos;
        public Vector3 Normal;
        public float Mat;
    }

    private static readonly Vector2[] CameraPath = BuildPath();

    private static float TerrainHeight(float x, float z)
    {
        float h = 0, amp = 1, freq = 0.005f;
        for (int o = 0; o < 6; o++)
        {
            h += amp * ValueNoise(x * freq + o * 17.3f, z * freq - o * 9.1f);
            amp *= 0.5f;
            freq *= 2.03f;
        }
        h = h * 55 + 10;
        float river = MathF.Sin(z * 0.007f) * 120;
        float valley = MathF.Min(1, MathF.Abs(x - river) / 90f);
        return h * (0.25f + 0.75f * valley * valley) - (1 - valley) * 4;
    }

    private static float ValueNoise(float x, float z)
    {
        int xi = (int)MathF.Floor(x), zi = (int)MathF.Floor(z);
        float fx = x - xi, fz = z - zi;
        float u = fx * fx * (3 - 2 * fx), v = fz * fz * (3 - 2 * fz);
        float a = Hash(xi, zi), b = Hash(xi + 1, zi), c = Hash(xi, zi + 1), d = Hash(xi + 1, zi + 1);
        return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
    }

    private static float Hash(int x, int z)
    {
        uint h = (uint)(x * 374761393 + z * 668265263);
        h = (h ^ (h >> 13)) * 1274126177u;
        return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
    }

    public static (Vertex[] Vertices, uint[] Indices) BuildTerrain()
    {
        var vertices = new Vertex[TerrainGrid * TerrainGrid];
        float step = TerrainSize / (TerrainGrid - 1), half = TerrainSize / 2;
        for (int z = 0; z < TerrainGrid; z++)
        {
            for (int x = 0; x < TerrainGrid; x++)
            {
                float wx = x * step - half, wz = z * step - half;
                float h = TerrainHeight(wx, wz);
                float hx = TerrainHeight(wx + 0.5f, wz) - TerrainHeight(wx - 0.5f, wz);
                float hz = TerrainHeight(wx, wz + 0.5f) - TerrainHeight(wx, wz - 0.5f);
                vertices[z * TerrainGrid + x] = new Vertex
                {
                    Pos = new(wx, h, wz),
                    Normal = Vector3.Normalize(new(-hx, 1, -hz)),
                    Mat = 0,
                };
            }
        }
        var indices = new uint[(TerrainGrid - 1) * (TerrainGrid - 1) * 6];
        int k = 0;
        for (int z = 0; z < TerrainGrid - 1; z++)
        {
            for (int x = 0; x < TerrainGrid - 1; x++)
            {
                uint a = (uint)(z * TerrainGrid + x), b = a + 1, c = a + TerrainGrid, d = c + 1;
                indices[k++] = a; indices[k++] = c; indices[k++] = b;
                indices[k++] = b; indices[k++] = c; indices[k++] = d;
            }
        }
        return (vertices, indices);
    }

    /// <summary>Árvore low-poly: tronco (Mat 1) + 3 cones de folhagem (Mat 2).</summary>
    public static (Vertex[] Vertices, uint[] Indices) BuildTree()
    {
        var vertices = new List<Vertex>();
        var indices = new List<uint>();

        void Cone(float baseY, float height, float r0, float r1, int seg, float mat)
        {
            uint start = (uint)vertices.Count;
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * MathF.Tau, c = MathF.Cos(a), s = MathF.Sin(a);
                var normal = Vector3.Normalize(new Vector3(c, (r0 - r1) / height, s));
                vertices.Add(new Vertex { Pos = new(c * r0, baseY, s * r0), Normal = normal, Mat = mat });
                vertices.Add(new Vertex { Pos = new(c * r1, baseY + height, s * r1), Normal = normal, Mat = mat });
            }
            for (int i = 0; i < seg; i++)
            {
                uint a = start + (uint)i * 2;
                indices.AddRange(new[] { a, a + 1, a + 2, a + 2, a + 1, a + 3 });
            }
        }

        Cone(0, 2.0f, 0.25f, 0.2f, 8, 1);
        Cone(1.4f, 2.6f, 2.0f, 0.05f, 12, 2);
        Cone(2.8f, 2.3f, 1.6f, 0.05f, 12, 2);
        Cone(4.0f, 2.0f, 1.1f, 0.02f, 12, 2);
        return (vertices.ToArray(), indices.ToArray());
    }

    public static Vector4[] PlaceTrees()
    {
        var list = new List<Vector4>(TreeCount);
        uint seed = 2024;
        while (list.Count < TreeCount)
        {
            float x = (Xorshift32.NextFloat(ref seed) - 0.5f) * TerrainSize * 0.98f;
            float z = (Xorshift32.NextFloat(ref seed) - 0.5f) * TerrainSize * 0.98f;
            float h = TerrainHeight(x, z);
            float slope = MathF.Abs(TerrainHeight(x + 1, z) - TerrainHeight(x - 1, z))
                        + MathF.Abs(TerrainHeight(x, z + 1) - TerrainHeight(x, z - 1));
            if (h < WaterY + 1.5f || h > 48 || slope > 1.4f) continue;
            list.Add(new Vector4(x, h - 0.2f, z, 0.7f + Xorshift32.NextFloat(ref seed) * 0.9f));
        }
        return list.ToArray();
    }

    public static (Vertex[] Vertices, uint[] Indices) BuildWater()
    {
        float half = TerrainSize / 2;
        var vertices = new[]
        {
            new Vertex { Pos = new(-half, WaterY, -half) },
            new Vertex { Pos = new(half, WaterY, -half) },
            new Vertex { Pos = new(-half, WaterY, half) },
            new Vertex { Pos = new(half, WaterY, half) },
        };
        return (vertices, new uint[] { 0, 2, 1, 1, 2, 3 });
    }

    public static (Vector3 Eye, Vector3 Target) CameraAt(float t)
    {
        var pos = PathAt(t);
        var ahead = PathAt(t + 0.035f);
        return (new Vector3(pos.X, CameraHeight(pos), pos.Y),
                new Vector3(ahead.X, CameraHeight(ahead) - 22, ahead.Y));
    }

    private static Vector2[] BuildPath()
    {
        var points = new List<Vector2>();
        for (int i = 0; i <= 6; i++) { float z = -330 + i * 110; points.Add(new(MathF.Sin(z * 0.007f) * 120 + 45, z)); }
        for (int i = 6; i >= 0; i--) { float z = -330 + i * 110; points.Add(new(MathF.Sin(z * 0.007f) * 120 - 75, z)); }
        return points.ToArray();
    }

    private static Vector2 PathAt(float t)
    {
        int n = CameraPath.Length;
        float f = (t % 1 + 1) % 1 * n;
        int i = (int)f;
        float u = f - i;
        Vector2 p0 = CameraPath[(i - 1 + n) % n], p1 = CameraPath[i], p2 = CameraPath[(i + 1) % n], p3 = CameraPath[(i + 2) % n];
        return 0.5f * (2 * p1 + (-p0 + p2) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u * u + (-p0 + 3 * p1 - 3 * p2 + p3) * u * u * u);
    }

    private static float CameraHeight(Vector2 p)
    {
        float h = WaterY;
        for (int dz = -2; dz <= 2; dz++)
            for (int dx = -2; dx <= 2; dx++)
                h = MathF.Max(h, TerrainHeight(p.X + dx * 10, p.Y + dz * 10));
        return h + 26;
    }
}
