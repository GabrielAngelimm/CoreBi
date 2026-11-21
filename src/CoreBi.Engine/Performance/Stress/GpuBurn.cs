using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using CoreBi.Engine.Performance.Graphics;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace CoreBi.Engine.Performance.Stress;

public sealed class GpuBurnSettings
{
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 720;
    public bool Fullscreen { get; init; }
    public int Msaa { get; init; } = 1;
    public int Shells { get; init; } = 96; // camadas de pelo (overdraw)
    public int Iterations { get; init; }   // carga extra de ALU no pixel shader
    public string AdapterName { get; init; } = "";
}

/// <summary>
/// Stress de GPU no estilo FurMark: um torus com pelos renderizado por shells instanciados, gerando overdraw massivo
/// e carga de shader sem limite de FPS numa janela Win32 própria.
/// </summary>
public sealed class GpuBurn : IDisposable
{
    private readonly object _sync = new();
    private Thread? _thread;
    private volatile bool _stop;
    private long _frames;
    private double _seconds;

    public bool Running { get { lock (_sync) return _thread?.IsAlive == true; } }
    public double Fps { get; private set; }
    public long Frames => Interlocked.Read(ref _frames);
    public double AverageFps => _seconds > 0 ? Frames / _seconds : 0;
    public string? Error { get; private set; }
    public string AdapterUsed { get; private set; } = "";

    public void Start(GpuBurnSettings s)
    {
        lock (_sync)
        {
            if (_thread?.IsAlive == true) return;
            _stop = false;
            _frames = 0;
            _seconds = 0;
            Fps = 0;
            Error = null;
            _thread = new Thread(() => Run(s)) { IsBackground = true, Name = "GPU burn" };
            _thread.Start();
        }
    }

    public void Stop()
    {
        Thread? thread;
        lock (_sync)
        {
            _stop = true;
            thread = _thread;
        }
        thread?.Join(3000);
    }

    public void Dispose() => Stop();

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex { public Vector3 Pos; public Vector3 Normal; public Vector2 Uv; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Constants
    {
        public Matrix4x4 World, ViewProj;
        public float Time, ShellCount, FurLength, Iterations;
    }

    private static readonly string Hlsl = ShaderSources.Load("gpu-burn");

    private void Run(GpuBurnSettings s)
    {
        ID3D11Texture2D? colorMs = null, depthTex = null;
        ID3D11RenderTargetView? rtv = null;
        ID3D11DepthStencilView? dsv = null;

        void ReleaseTargets()
        {
            rtv?.Dispose(); dsv?.Dispose(); depthTex?.Dispose(); colorMs?.Dispose();
            rtv = null; dsv = null; depthTex = null; colorMs = null;
        }

        try
        {
            using var window = RenderWindow.Create("CoreBi · GPU burn", s.Width, s.Height, s.Fullscreen);
            var (cw, ch) = window.GetClientSize();
            cw = Math.Max(1, cw);
            ch = Math.Max(1, ch);

            using var host = new D3D11OffscreenHost(window.Handle, cw, ch, s.AdapterName, inFlight: 6, requestTearing: true);
            using var scope = new DisposableScope();
            AdapterUsed = host.AdapterName;

            var device = host.Device;
            var ctx = host.Context;
            bool ShouldStop() => _stop || window.StopRequested;

            int msaa = s.Msaa;
            while (msaa > 1 && device.CheckMultisampleQualityLevels(Format.B8G8R8A8_UNorm, (uint)msaa) == 0) msaa /= 2;

            void CreateTargets(int tw, int th)
            {
                var cd = new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)tw, (uint)th, 1, 1, BindFlags.RenderTarget,
                    ResourceUsage.Default, CpuAccessFlags.None, (uint)msaa, 0, ResourceOptionFlags.None);
                colorMs = device.CreateTexture2D(in cd);
                rtv = device.CreateRenderTargetView(colorMs);
                var dd = new Texture2DDescription(Format.D32_Float, (uint)tw, (uint)th, 1, 1, BindFlags.DepthStencil,
                    ResourceUsage.Default, CpuAccessFlags.None, (uint)msaa, 0, ResourceOptionFlags.None);
                depthTex = device.CreateTexture2D(in dd);
                dsv = device.CreateDepthStencilView(depthTex);
            }
            CreateTargets(cw, ch);

            var vsCode = Compiler.Compile(Hlsl, "VS", "fur.hlsl", "vs_5_0", ShaderFlags.OptimizationLevel3);
            var psCode = Compiler.Compile(Hlsl, "PS", "fur.hlsl", "ps_5_0", ShaderFlags.OptimizationLevel3);
            var vs = scope.Track(device.CreateVertexShader(vsCode.Span));
            var ps = scope.Track(device.CreatePixelShader(psCode.Span));
            var layout = scope.Track(device.CreateInputLayout(new[]
            {
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 24, 0),
            }, vsCode.Span));

            BuildTorus(out var vertices, out var indices);
            var vb = scope.Track(device.CreateBuffer(vertices, BindFlags.VertexBuffer));
            var ib = scope.Track(device.CreateBuffer(indices, BindFlags.IndexBuffer));
            var cb = scope.Track(device.CreateBuffer((uint)Marshal.SizeOf<Constants>(), BindFlags.ConstantBuffer,
                ResourceUsage.Dynamic, CpuAccessFlags.Write));
            var raster = scope.Track(device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid)
            {
                MultisampleEnable = msaa > 1,
            }));
            // Blending nas camadas soma carga de ROP e banda de memória, como na pelagem translúcida do FurMark.
            var blend = scope.Track(device.CreateBlendState(new BlendDescription(Blend.SourceAlpha, Blend.InverseSourceAlpha)));

            long frame = 0, lastFrames = 0;
            var clock = Stopwatch.StartNew();
            double lastT = 0, lastPresent = 0;

            while (!ShouldStop())
            {
                window.PumpMessages();
                if (ShouldStop()) break;

                var (nw, nh) = window.GetClientSize();
                if (nw <= 0 || nh <= 0) { Thread.Sleep(50); continue; } // minimizada
                if (nw != cw || nh != ch)
                {
                    cw = nw; ch = nh;
                    ctx.OMSetRenderTargets((ID3D11RenderTargetView)null!, null);
                    ReleaseTargets();
                    host.ResizeSwapChain(cw, ch);
                    CreateTargets(cw, ch);
                }

                host.WaitFence(frame, ShouldStop, spinWait: false);

                float t = (float)clock.Elapsed.TotalSeconds;
                var world = Matrix4x4.CreateRotationY(t * 0.6f) * Matrix4x4.CreateRotationX(0.55f + 0.25f * MathF.Sin(t * 0.35f));
                var view = Matrix4x4.CreateLookAt(new Vector3(0, 0, -3.5f), Vector3.Zero, Vector3.UnitY);
                var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4, cw / (float)ch, 0.1f, 50f);
                var c = new Constants
                {
                    World = world,
                    ViewProj = view * proj,
                    Time = t,
                    ShellCount = s.Shells,
                    FurLength = 0.2f,
                    Iterations = s.Iterations,
                };
                var map = ctx.Map(cb, MapMode.WriteDiscard);
                Marshal.StructureToPtr(c, map.DataPointer, false);
                ctx.Unmap(cb);

                ctx.OMSetRenderTargets(rtv!, dsv);
                ctx.ClearRenderTargetView(rtv!, new Color4(0, 0, 0, 1));
                ctx.ClearDepthStencilView(dsv!, DepthStencilClearFlags.Depth, 1f, 0);
                ctx.RSSetViewport(0, 0, cw, ch);
                ctx.RSSetState(raster);
                ctx.OMSetBlendState(blend);
                ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                ctx.IASetInputLayout(layout);
                ctx.IASetVertexBuffer(0, vb, (uint)Marshal.SizeOf<Vertex>());
                ctx.IASetIndexBuffer(ib, Format.R32_UInt, 0);
                ctx.VSSetShader(vs);
                ctx.PSSetShader(ps);
                ctx.VSSetConstantBuffer(0, cb);
                ctx.PSSetConstantBuffer(0, cb);
                ctx.DrawIndexedInstanced((uint)indices.Length, (uint)s.Shells, 0, 0, 0);
                ctx.End(host.FenceFor(frame));
                frame++;
                Interlocked.Increment(ref _frames);

                double now = clock.Elapsed.TotalSeconds;
                if (now - lastPresent >= 1 / 60.0)
                {
                    if (msaa > 1) ctx.ResolveSubresource(host.BackBuffer, 0, colorMs!, 0, Format.B8G8R8A8_UNorm);
                    else ctx.CopyResource(host.BackBuffer, colorMs!);
                    host.SwapChain.Present(0, host.AllowTearing ? PresentFlags.AllowTearing : PresentFlags.None);
                    lastPresent = now;
                }
                _seconds = now;
                if (now - lastT >= 0.5)
                {
                    long f = Frames;
                    Fps = (f - lastFrames) / (now - lastT);
                    lastT = now;
                    lastFrames = f;
                    window.SetTitle($"CoreBi · GPU burn · {Fps:0} FPS · {cw}x{ch}" +
                                    (msaa > 1 ? $" · MSAA {msaa}x" : "") + $" · {AdapterUsed}");
                }
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            ReleaseTargets();
            Fps = 0;
        }
    }

    private static void BuildTorus(out Vertex[] vertices, out uint[] indices)
    {
        const int seg = 160, sides = 64;
        var vList = new List<Vertex>((seg + 1) * (sides + 1));
        var iList = new List<uint>(seg * sides * 6);
        MeshPrimitives.BuildTorus(seg, sides, 1f, 0.42f,
            (pos, nrm, uv) => vList.Add(new Vertex { Pos = pos, Normal = nrm, Uv = uv }),
            (a, b, c) => { iList.Add(a); iList.Add(b); iList.Add(c); });
        vertices = vList.ToArray();
        indices = iList.ToArray();
    }
}
