using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using CoreBi.Engine.Performance.Graphics;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace CoreBi.Engine.Performance.Benchmarks;

public sealed record GpuGameResult(double AvgFps, double Low1Fps, int Frames, double Seconds, int Width, int Height, string Adapter)
{
    /// <summary>Nota = FPS médio × 10.</summary>
    public double Score => AvgFps * 10;
}

/// <summary>
/// Cena de jogo para benchmark de GPU: vale com terreno por ruído fractal, 40 mil árvores por instancing,
/// shadow map 4096² com PCF, água procedural, neblina, céu e pós-processamento HDR. Renderiza fora da tela
/// com fila de frames para não ficar preso ao V-Sync do monitor.
/// </summary>
public sealed class GpuGameBench
{
    public int Width { get; init; } = 1920;
    public int Height { get; init; } = 1080;
    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(60);
    public string AdapterName { get; init; } = "";

    public double Progress { get; private set; }
    public double CurrentFps { get; private set; }
    public string? Error { get; private set; }
    public bool Cancelled { get; private set; }

    private volatile bool _stop;

    public void Cancel() => _stop = true;

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameCB
    {
        public Matrix4x4 ViewProj, LightViewProj, InvViewProj;
        public Vector3 CamPos; public float Time;
        public Vector3 SunDir; public float Pad;
    }

    private static readonly string Hlsl = ShaderSources.Load("gpu-game");

    public GpuGameResult? Run()
    {
        var oldPriority = Thread.CurrentThread.Priority;
        Thread.CurrentThread.Priority = ThreadPriority.Highest; // medição de frametime precisa de pontualidade
        try
        {
            using var window = RenderWindow.CreateFitted("Benchmark · Cena de jogo", Width, Height);
            using var host = new D3D11OffscreenHost(window.Handle, Width, Height, AdapterName, inFlight: 8, requestTearing: false);
            using var scope = new DisposableScope();

            var device = host.Device;
            var ctx = host.Context;
            bool ShouldStop() => _stop || window.StopRequested;

            Texture2DDescription Tex(Format f, int w, int h, BindFlags b) =>
                new(f, (uint)w, (uint)h, 1, 1, b, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None);

            var hdrDesc = Tex(Format.R16G16B16A16_Float, Width, Height, BindFlags.RenderTarget | BindFlags.ShaderResource);
            var hdr = scope.Track(device.CreateTexture2D(in hdrDesc));
            var hdrRtv = scope.Track(device.CreateRenderTargetView(hdr));
            var hdrSrv = scope.Track(device.CreateShaderResourceView(hdr));

            var depthDesc = Tex(Format.D32_Float, Width, Height, BindFlags.DepthStencil);
            var depth = scope.Track(device.CreateTexture2D(in depthDesc));
            var dsv = scope.Track(device.CreateDepthStencilView(depth));

            var ldrDesc = Tex(Format.B8G8R8A8_UNorm, Width, Height, BindFlags.RenderTarget);
            var ldr = scope.Track(device.CreateTexture2D(in ldrDesc));
            var ldrRtv = scope.Track(device.CreateRenderTargetView(ldr));

            const int shadowRes = 4096;
            var smDesc = Tex(Format.R32_Typeless, shadowRes, shadowRes, BindFlags.DepthStencil | BindFlags.ShaderResource);
            var shadowTex = scope.Track(device.CreateTexture2D(in smDesc));
            var shadowDsv = scope.Track(device.CreateDepthStencilView(shadowTex, new DepthStencilViewDescription(shadowTex, DepthStencilViewDimension.Texture2D, Format.D32_Float)));
            var shadowSrv = scope.Track(device.CreateShaderResourceView(shadowTex, new ShaderResourceViewDescription(shadowTex, ShaderResourceViewDimension.Texture2D, Format.R32_Float)));

            static ReadOnlyMemory<byte> Compile(string entry, string profile) =>
                Compiler.Compile(Hlsl, entry, "scene.hlsl", profile, ShaderFlags.OptimizationLevel3);

            var terrainVsCode = Compile("TerrainVS", "vs_5_0");
            var terrainVs = scope.Track(device.CreateVertexShader(terrainVsCode.Span));
            var treeVs = scope.Track(device.CreateVertexShader(Compile("TreeVS", "vs_5_0").Span));
            var waterVs = scope.Track(device.CreateVertexShader(Compile("WaterVS", "vs_5_0").Span));
            var terrainShadowVs = scope.Track(device.CreateVertexShader(Compile("TerrainShadowVS", "vs_5_0").Span));
            var treeShadowVs = scope.Track(device.CreateVertexShader(Compile("TreeShadowVS", "vs_5_0").Span));
            var fullscreenVs = scope.Track(device.CreateVertexShader(Compile("FullscreenVS", "vs_5_0").Span));
            var terrainPs = scope.Track(device.CreatePixelShader(Compile("TerrainPS", "ps_5_0").Span));
            var treePs = scope.Track(device.CreatePixelShader(Compile("TreePS", "ps_5_0").Span));
            var waterPs = scope.Track(device.CreatePixelShader(Compile("WaterPS", "ps_5_0").Span));
            var skyPs = scope.Track(device.CreatePixelShader(Compile("SkyPS", "ps_5_0").Span));
            var postPs = scope.Track(device.CreatePixelShader(Compile("PostPS", "ps_5_0").Span));

            var layout = scope.Track(device.CreateInputLayout(new[]
            {
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32_Float, 24, 0),
            }, terrainVsCode.Span));

            var (tv, ti) = GameScene.BuildTerrain();
            var terrainVb = scope.Track(device.CreateBuffer(tv, BindFlags.VertexBuffer));
            var terrainIb = scope.Track(device.CreateBuffer(ti, BindFlags.IndexBuffer));

            var (treeV, treeI) = GameScene.BuildTree();
            var treeVb = scope.Track(device.CreateBuffer(treeV, BindFlags.VertexBuffer));
            var treeIb = scope.Track(device.CreateBuffer(treeI, BindFlags.IndexBuffer));

            var trees = GameScene.PlaceTrees();
            var treeBuf = scope.Track(device.CreateBuffer(trees, BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None,
                ResourceOptionFlags.BufferStructured, 0, 16));
            var treeSrv = scope.Track(device.CreateShaderResourceView(treeBuf));

            var (waterV, waterI) = GameScene.BuildWater();
            var waterVb = scope.Track(device.CreateBuffer(waterV, BindFlags.VertexBuffer));
            var waterIb = scope.Track(device.CreateBuffer(waterI, BindFlags.IndexBuffer));

            var cb = scope.Track(device.CreateBuffer((uint)Marshal.SizeOf<FrameCB>(), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
            var rsMain = scope.Track(device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid)));
            var rsShadow = scope.Track(device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid)
            {
                DepthBias = 800,
                SlopeScaledDepthBias = 2.0f,
                DepthClipEnable = true,
            }));
            var noDepth = scope.Track(device.CreateDepthStencilState(DepthStencilDescription.None));
            var cmpSampler = scope.Track(device.CreateSamplerState(new SamplerDescription
            {
                Filter = Filter.ComparisonMinMagLinearMipPoint,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp,
                ComparisonFunc = ComparisonFunction.LessEqual,
                MaxLOD = float.MaxValue,
            }));
            var linSampler = scope.Track(device.CreateSamplerState(SamplerDescription.LinearClamp));

            var sunDir = Vector3.Normalize(new Vector3(-0.45f, -0.55f, -0.35f));
            var lightVP = Matrix4x4.CreateLookAt(-sunDir * 900, Vector3.Zero, Vector3.UnitY)
                        * Matrix4x4.CreateOrthographic(900, 900, 1, 1900);
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, Width / (float)Height, 0.5f, 2000);
            uint vStride = (uint)Marshal.SizeOf<GameScene.Vertex>();

            var frameTimes = new List<double>(20000);
            var clock = Stopwatch.StartNew();
            double lastDone = -1, lastPresent = 0, lastTitle = 0;
            int titleFrames = 0, frames = 0;
            long frame = 0;

            while (!ShouldStop() && clock.Elapsed < Duration)
            {
                window.PumpMessages();
                if (ShouldStop()) break;

                var fence = host.FenceFor(frame);
                if (frame >= host.InFlight)
                {
                    // Espera ativa (sem ceder o núcleo): ceder a vez pode custar um tick de 15,6 ms do Windows
                    // e registraria uma travada que a GPU não teve.
                    host.WaitFence(frame, ShouldStop, spinWait: true);
                    double done = clock.Elapsed.TotalMilliseconds;
                    if (lastDone >= 0) frameTimes.Add(done - lastDone);
                    lastDone = done;
                    frames++;
                    titleFrames++;
                }

                float t = (float)(clock.Elapsed.TotalSeconds / Duration.TotalSeconds);
                var (eye, target) = GameScene.CameraAt(t);
                var vp = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY) * proj;
                Matrix4x4.Invert(vp, out var inv);
                var data = new FrameCB
                {
                    ViewProj = vp,
                    LightViewProj = lightVP,
                    InvViewProj = inv,
                    CamPos = eye,
                    Time = (float)clock.Elapsed.TotalSeconds,
                    SunDir = sunDir,
                };
                var map = ctx.Map(cb, MapMode.WriteDiscard);
                Marshal.StructureToPtr(data, map.DataPointer, false);
                ctx.Unmap(cb);

                ctx.VSSetConstantBuffer(0, cb);
                ctx.PSSetConstantBuffer(0, cb);
                ctx.IASetInputLayout(layout);
                ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                ctx.VSSetShaderResource(1, treeSrv);

                // 1) Shadow map
                ctx.PSSetShaderResource(0, null!);
                ctx.OMSetRenderTargets((ID3D11RenderTargetView)null!, shadowDsv);
                ctx.ClearDepthStencilView(shadowDsv, DepthStencilClearFlags.Depth, 1, 0);
                ctx.RSSetViewport(0, 0, shadowRes, shadowRes);
                ctx.RSSetState(rsShadow);
                ctx.OMSetDepthStencilState(null);
                ctx.PSSetShader(null);
                ctx.VSSetShader(terrainShadowVs);
                ctx.IASetVertexBuffer(0, terrainVb, vStride);
                ctx.IASetIndexBuffer(terrainIb, Format.R32_UInt, 0);
                ctx.DrawIndexed((uint)ti.Length, 0, 0);
                ctx.VSSetShader(treeShadowVs);
                ctx.IASetVertexBuffer(0, treeVb, vStride);
                ctx.IASetIndexBuffer(treeIb, Format.R32_UInt, 0);
                ctx.DrawIndexedInstanced((uint)treeI.Length, (uint)trees.Length, 0, 0, 0);

                // 2) Cena HDR: céu, terreno, árvores e água
                ctx.OMSetRenderTargets(hdrRtv, dsv);
                ctx.ClearDepthStencilView(dsv, DepthStencilClearFlags.Depth, 1, 0);
                ctx.RSSetViewport(0, 0, Width, Height);
                ctx.RSSetState(rsMain);
                ctx.PSSetShaderResource(0, shadowSrv);
                ctx.PSSetSampler(0, cmpSampler);
                ctx.PSSetSampler(1, linSampler);

                ctx.OMSetDepthStencilState(noDepth);
                ctx.VSSetShader(fullscreenVs);
                ctx.PSSetShader(skyPs);
                ctx.Draw(3, 0);
                ctx.OMSetDepthStencilState(null);

                ctx.VSSetShader(terrainVs);
                ctx.PSSetShader(terrainPs);
                ctx.IASetVertexBuffer(0, terrainVb, vStride);
                ctx.IASetIndexBuffer(terrainIb, Format.R32_UInt, 0);
                ctx.DrawIndexed((uint)ti.Length, 0, 0);

                ctx.VSSetShader(treeVs);
                ctx.PSSetShader(treePs);
                ctx.IASetVertexBuffer(0, treeVb, vStride);
                ctx.IASetIndexBuffer(treeIb, Format.R32_UInt, 0);
                ctx.DrawIndexedInstanced((uint)treeI.Length, (uint)trees.Length, 0, 0, 0);

                ctx.VSSetShader(waterVs);
                ctx.PSSetShader(waterPs);
                ctx.IASetVertexBuffer(0, waterVb, vStride);
                ctx.IASetIndexBuffer(waterIb, Format.R32_UInt, 0);
                ctx.DrawIndexed((uint)waterI.Length, 0, 0);

                // 3) Pós-processamento HDR → LDR
                ctx.OMSetRenderTargets(ldrRtv);
                ctx.OMSetDepthStencilState(noDepth);
                ctx.PSSetShaderResource(0, null!);
                ctx.PSSetShaderResource(2, hdrSrv);
                ctx.VSSetShader(fullscreenVs);
                ctx.PSSetShader(postPs);
                ctx.Draw(3, 0);
                ctx.PSSetShaderResource(2, null!);

                ctx.End(fence);
                ctx.Flush(); // envia imediatamente à GPU para evitar buracos ociosos na fila do driver
                frame++;

                double now = clock.Elapsed.TotalSeconds;
                if (now - lastPresent >= 1 / 60.0)
                {
                    // DoNotWait: se a tela ainda não liberou o buffer, pula esta apresentação em vez de travar a fila da GPU.
                    ctx.CopyResource(host.BackBuffer, ldr);
                    if (host.SwapChain.Present(0, PresentFlags.DoNotWait).Success) lastPresent = now;
                }
                if (now - lastTitle >= 0.5)
                {
                    CurrentFps = titleFrames / (now - lastTitle);
                    titleFrames = 0;
                    lastTitle = now;
                    Progress = now / Duration.TotalSeconds;
                    window.SetTitle($"Benchmark · Cena de jogo · {Width}x{Height} · {CurrentFps:0} FPS · {Progress:P0}");
                }
            }

            if (ShouldStop()) { Cancelled = true; return null; }
            double secs = frameTimes.Sum() / 1000.0;
            return new GpuGameResult(
                secs > 0 ? frameTimes.Count / secs : 0,
                FrameTimeStats.Low1Fps(frameTimes),
                frames,
                clock.Elapsed.TotalSeconds,
                Width,
                Height,
                host.AdapterName);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return null;
        }
        finally
        {
            Thread.CurrentThread.Priority = oldPriority;
        }
    }
}
