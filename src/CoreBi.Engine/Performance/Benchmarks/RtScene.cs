using System.Numerics;
using System.Runtime.InteropServices;
using CoreBi.Engine.Performance.Graphics;

namespace CoreBi.Engine.Performance.Benchmarks;

/// <summary>Malhas, instâncias procedurais e câmera da cena de benchmark DXR 1.1.</summary>
internal static class RtScene
{
    public const int MeshGround = 0, MeshSphere = 1, MeshHeroSphere = 2, MeshTorus = 3, MeshColumn = 4, MeshBox = 5;
    public const int ObjectCount = 4000;
    public const float WorldSize = 220f;

    [StructLayout(LayoutKind.Sequential)] public struct Vertex { public Vector3 Pos; public Vector3 Normal; }
    [StructLayout(LayoutKind.Sequential)] public struct MeshInfo { public uint IndexOffset, VertexOffset, Pad0, Pad1; }
    [StructLayout(LayoutKind.Sequential)]
    public struct InstanceMaterial
    {
        public Vector3 Albedo;
        public float Metal;
        public float Rough;
        public float Emissive;
        public uint Mesh;
        public float Pad;
    }

    public readonly record struct MeshRange(uint IndexOffset, uint IndexCount, uint VertexOffset, uint VertexCount);
    public sealed record SceneObject(int Mesh, Vector3 Pos, Vector3 Scale, float Spin, Vector3 Axis, InstanceMaterial Material);

    public sealed class MeshSet
    {
        public List<Vertex> Vertices { get; } = new();
        public List<uint> Indices { get; } = new();
        public List<MeshRange> Meshes { get; } = new();
        private int _vStart, _iStart;

        public void Begin() { _vStart = Vertices.Count; _iStart = Indices.Count; }
        public void End() => Meshes.Add(new((uint)_iStart, (uint)(Indices.Count - _iStart), (uint)_vStart, (uint)(Vertices.Count - _vStart)));
        public uint Add(Vector3 pos, Vector3 normal)
        {
            Vertices.Add(new Vertex { Pos = pos, Normal = Vector3.Normalize(normal) });
            return (uint)(Vertices.Count - 1 - _vStart);
        }
        public void Tri(uint a, uint b, uint c) { Indices.Add(a); Indices.Add(b); Indices.Add(c); }
    }

    public static MeshSet BuildMeshes()
    {
        var meshes = new MeshSet();
        AddPlane(meshes, WorldSize + 60, 96);
        AddSphere(meshes, 32);
        AddSphere(meshes, 160); // esfera grande (~25 mil triângulos)
        AddTorus(meshes, 48, 24, 1f, 0.35f);
        AddColumn(meshes, 48);
        AddBox(meshes);
        return meshes;
    }

    /// <summary>Objetos espalhados com semente fixa para gerar a mesma cena em qualquer GPU.</summary>
    public static List<SceneObject> BuildObjects()
    {
        var rnd = new Random(20261015);
        float R() => (float)rnd.NextDouble();
        var list = new List<SceneObject>(ObjectCount)
        {
            new(MeshGround, Vector3.Zero, Vector3.One, 0, Vector3.UnitY,
                new InstanceMaterial { Albedo = new(0.55f, 0.55f, 0.58f), Metal = 0.15f, Rough = 0.35f, Mesh = MeshGround }),
        };
        for (int i = 0; i < 7; i++)
        {
            float a = i / 7f * MathF.Tau;
            var pos = new Vector3(MathF.Cos(a) * 16, 6, MathF.Sin(a) * 16);
            bool gold = i % 2 == 1;
            list.Add(new(MeshHeroSphere, pos, new Vector3(6), 0, Vector3.UnitY, new InstanceMaterial
            {
                Albedo = gold ? new(1.0f, 0.78f, 0.34f) : new(0.95f, 0.95f, 0.97f),
                Metal = 1,
                Rough = gold ? 0.12f : 0.0f,
                Mesh = MeshHeroSphere,
            }));
        }
        while (list.Count < ObjectCount)
        {
            var pos = new Vector3((R() - 0.5f) * WorldSize, 0, (R() - 0.5f) * WorldSize);
            if (new Vector2(pos.X, pos.Z).Length() < 26) continue;
            int mesh = R() switch { < 0.35f => MeshSphere, < 0.55f => MeshTorus, < 0.75f => MeshColumn, _ => MeshBox };
            float s = 0.6f + R() * 1.8f;
            Vector3 scale = mesh == MeshColumn ? new(s * 0.6f, s * (2 + R() * 3), s * 0.6f) : new(s);
            pos.Y = mesh switch { MeshSphere => s, MeshTorus => s * 0.35f + 0.6f, MeshBox => s, _ => 0 };
            float roll = R();
            var material = new InstanceMaterial
            {
                Albedo = Palette(R(), R()),
                Metal = roll < 0.3f ? 1 : 0,
                Rough = roll < 0.3f ? R() * 0.35f : 0.6f + R() * 0.4f,
                Emissive = roll > 0.97f ? 6 + R() * 10 : 0,
                Mesh = (uint)mesh,
            };
            float spin = mesh is MeshTorus or MeshBox && R() < 0.5f ? 0.3f + R() * 1.2f : 0;
            var axis = Vector3.Normalize(new Vector3(R() - 0.5f, 1, R() - 0.5f));
            list.Add(new(mesh, pos, scale, spin, axis, material));
        }
        return list;
    }

    public static Matrix4x4 ObjectMatrix(SceneObject obj, float timeSeconds) =>
        Matrix4x4.CreateScale(obj.Scale) *
        (obj.Spin != 0 ? Matrix4x4.CreateFromAxisAngle(obj.Axis, timeSeconds * obj.Spin) : Matrix4x4.Identity) *
        Matrix4x4.CreateTranslation(obj.Pos);

    public static (Vector3 Eye, Vector3 Target) CameraAt(float t)
    {
        float a = t * MathF.Tau * 0.75f + 0.4f;
        float r = 40 + 26 * MathF.Sin(t * MathF.PI);
        var eye = new Vector3(MathF.Cos(a) * r, 12 + 10 * (0.5f + 0.5f * MathF.Sin(t * MathF.Tau * 1.5f)), MathF.Sin(a) * r);
        var target = new Vector3(MathF.Cos(a + 0.6f) * 6, 3, MathF.Sin(a + 0.6f) * 6);
        return (eye, target);
    }

    private static readonly Vector3[] Colors =
    {
        new(0.85f, 0.18f, 0.12f), new(0.95f, 0.55f, 0.08f), new(0.12f, 0.45f, 0.85f), new(0.1f, 0.65f, 0.4f),
        new(0.9f, 0.85f, 0.75f), new(0.45f, 0.2f, 0.7f), new(0.08f, 0.08f, 0.1f), new(0.75f, 0.75f, 0.78f),
    };

    private static Vector3 Palette(float pick, float shade) =>
        Colors[(int)(pick * Colors.Length) % Colors.Length] * (0.55f + shade * 0.45f);

    private static void AddPlane(MeshSet b, float size, int n)
    {
        b.Begin();
        for (int z = 0; z <= n; z++)
            for (int x = 0; x <= n; x++)
                b.Add(new((x / (float)n - 0.5f) * size, 0, (z / (float)n - 0.5f) * size), Vector3.UnitY);
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                uint a = (uint)(z * (n + 1) + x), c = a + (uint)(n + 1);
                b.Tri(a, c, a + 1); b.Tri(a + 1, c, c + 1);
            }
        b.End();
    }

    private static void AddSphere(MeshSet b, int seg)
    {
        b.Begin();
        int rings = seg / 2;
        for (int r = 0; r <= rings; r++)
            for (int s = 0; s <= seg; s++)
            {
                float th = MathF.PI * r / rings, ph = 2 * MathF.PI * s / seg;
                var p = new Vector3(MathF.Sin(th) * MathF.Cos(ph), MathF.Cos(th), MathF.Sin(th) * MathF.Sin(ph));
                b.Add(p, p);
            }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < seg; s++)
            {
                uint a = (uint)(r * (seg + 1) + s), c = a + (uint)(seg + 1);
                b.Tri(a, a + 1, c); b.Tri(a + 1, c + 1, c);
            }
        b.End();
    }

    private static void AddTorus(MeshSet b, int seg, int side, float major, float minor)
    {
        b.Begin();
        MeshPrimitives.BuildTorus(seg, side, major, minor, (pos, nrm, _) => b.Add(pos, nrm), b.Tri);
        b.End();
    }

    private static void AddColumn(MeshSet b, int seg)
    {
        b.Begin();
        for (int i = 0; i <= seg; i++)
        {
            float a = 2 * MathF.PI * i / seg;
            float rr = 1 + 0.06f * MathF.Cos(a * 12); // caneluras para normais variadas
            var n = new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
            b.Add(new Vector3(MathF.Cos(a) * rr, 0, MathF.Sin(a) * rr), n);
            b.Add(new Vector3(MathF.Cos(a) * rr, 1, MathF.Sin(a) * rr), n);
        }
        for (int i = 0; i < seg; i++) { uint a = (uint)(i * 2); b.Tri(a, a + 1, a + 2); b.Tri(a + 1, a + 3, a + 2); }
        uint top = b.Add(new Vector3(0, 1, 0), Vector3.UnitY);
        for (int i = 0; i < seg; i++)
        {
            float a0 = 2 * MathF.PI * i / seg, a1 = 2 * MathF.PI * (i + 1) / seg;
            uint p0 = b.Add(new Vector3(MathF.Cos(a0), 1, MathF.Sin(a0)), Vector3.UnitY);
            uint p1 = b.Add(new Vector3(MathF.Cos(a1), 1, MathF.Sin(a1)), Vector3.UnitY);
            b.Tri(top, p1, p0);
        }
        b.End();
    }

    private static void AddBox(MeshSet b)
    {
        b.Begin();
        Vector3[] normals = { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ };
        foreach (var f in normals)
        {
            var u = MathF.Abs(f.Y) > 0.5f ? Vector3.UnitX : Vector3.UnitY;
            var v = Vector3.Cross(f, u);
            uint a = b.Add(f - u - v, f), c = b.Add(f + u - v, f), d = b.Add(f + u + v, f), e = b.Add(f - u + v, f);
            b.Tri(a, c, d); b.Tri(a, d, e);
        }
        b.End();
    }
}
