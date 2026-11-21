using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using CoreBi.Engine.Performance.Graphics;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.Dxc;
using Vortice.DXGI;

namespace CoreBi.Engine.Performance.Benchmarks;

/// <summary>Uma passada medida do teste de ray tracing.</summary>
public sealed record RtPass(double AvgFps, double Low1Fps, int Frames);

/// <summary>
/// Resultado do teste de ray tracing: nota pela mediana das passadas (resiste a uma passada ruim) e variação entre elas,
/// indicando se o resultado é confiável para comparação.
/// </summary>
public sealed record GpuRtResult(IReadOnlyList<RtPass> Passes, int Width, int Height, string Preset, string Adapter, double GigaRaysPerSec)
{
    public double MedianFps => FrameTimeStats.Median(Passes.Select(p => p.AvgFps));
    public double MedianLow1 => FrameTimeStats.Median(Passes.Select(p => p.Low1Fps));
    /// <summary>(maior − menor) ÷ mediana, em %.</summary>
    public double VariationPct => Passes.Count < 2 || MedianFps <= 0 ? 0 : (Passes.Max(p => p.AvgFps) - Passes.Min(p => p.AvgFps)) / MedianFps * 100;
    public bool Reliable => Passes.Count >= 3 && Passes.All(p => p.Frames > 0 && p.AvgFps > 0) && VariationPct < 3;
    /// <summary>Nota = FPS mediano × 10 (mesma escala da cena de jogo).</summary>
    public double Score => MedianFps * 10;
}

/// <summary>
/// Benchmark de ray tracing em hardware (DXR 1.1 / Direct3D 12): path tracer inline (RayQuery) em compute shader
/// sobre ~4 mil objetos com rebuild de TLAS por frame e medição via timestamps da GPU.
/// </summary>
public sealed class GpuRtBench
{
    public int Width { get; init; } = 1920;
    public int Height { get; init; } = 1080;
    public string Preset { get; init; } = "alta"; // normal | alta | extrema
    public TimeSpan PassDuration { get; init; } = TimeSpan.FromSeconds(30);
    public int Passes { get; init; } = 3;
    public TimeSpan Warmup { get; init; } = TimeSpan.FromSeconds(10);
    public string AdapterName { get; init; } = "";

    public double Progress { get; private set; }
    public double CurrentFps { get; private set; }
    public string Phase { get; private set; } = "";
    public string? Error { get; private set; }
    public bool Cancelled { get; private set; }

    private volatile bool _stop;

    public void Cancel() => _stop = true;

    private (uint Spp, uint Bounces) Quality => Preset switch
    {
        "normal" => (2u, 2u),
        "extrema" => (8u, 4u),
        _ => (4u, 3u),
    };

    /// <summary>Limite superior de raios por pixel (até 1 de visibilidade/rebatida + 1 sombra + 1 oclusão por rebatida).</summary>
    private double MaxRaysPerPixel => Quality.Spp * Quality.Bounces * 3.0;

    /// <summary>Verifica se a placa tem ray tracing por hardware (DXR 1.1). Retorna null se tiver, senão o motivo.</summary>
    public static string? CheckSupport(string adapterName)
    {
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory4>();
            using var adapter = DxgiAdapters.PickHardwareAdapter(factory, adapterName, out _);
            using var device = D3D12.D3D12CreateDevice<ID3D12Device5>(adapter, FeatureLevel.Level_12_0);
            return device.Options5.RaytracingTier >= RaytracingTier.Tier1_1 ? null
                 : "Esta placa não tem ray tracing por hardware (DXR 1.1). Precisa de RTX 20 ou mais nova, Radeon RX 6000 ou mais nova, ou Intel Arc.";
        }
        catch (Exception ex) { return $"Direct3D 12 indisponível: {ex.Message}"; }
    }

    /// <summary>D3D12_RAYTRACING_INSTANCE_DESC (64 bytes): matriz 3x4, id(24)+máscara(8), contribuição(24)+flags(8), BLAS.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct InstanceDesc
    {
        public float M00, M01, M02, M03, M10, M11, M12, M13, M20, M21, M22, M23;
        public uint IdMask, ContribFlags;
        public ulong Blas;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameCB
    {
        public Matrix4x4 InvViewProj;
        public Vector3 CamPos; public float Time;
        public Vector3 SunDir; public uint FrameIndex;
        public uint Width, Height, Spp, Bounces;
    }

    private static readonly string Hlsl = ShaderSources.Load("gpu-rt");

    public GpuRtResult? Run()
    {
        var oldPriority = Thread.CurrentThread.Priority;
        Thread.CurrentThread.Priority = ThreadPriority.Highest;

        ID3D12CommandQueue? queue = null;
        ID3D12Fence? fence = null;
        ulong fenceValue = 0;
        using var fenceEvent = new AutoResetEvent(false);

        void WaitGpu(ulong target)
        {
            if (fence!.CompletedValue >= target) return;
            fence.SetEventOnCompletion(target, fenceEvent.SafeWaitHandle.DangerousGetHandle());
            fenceEvent.WaitOne();
        }

        try
        {
            using var window = RenderWindow.CreateFitted("Benchmark · Ray tracing", Width, Height);
            using var scope = new DisposableScope();
            bool ShouldStop() => _stop || window.StopRequested;

            try
            {
                var factory = scope.Track(DXGI.CreateDXGIFactory1<IDXGIFactory4>());
                var adapter = scope.Track(DxgiAdapters.PickHardwareAdapter(factory, AdapterName, out string adapterName));
                var device = scope.Track(D3D12.D3D12CreateDevice<ID3D12Device5>(adapter, FeatureLevel.Level_12_0));
                if (device.Options5.RaytracingTier < RaytracingTier.Tier1_1)
                    throw new NotSupportedException("Esta placa não tem ray tracing por hardware (DXR 1.1).");

                queue = scope.Track(device.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct)));
                fence = scope.Track(device.CreateFence(0, FenceFlags.None));
                queue.GetTimestampFrequency(out ulong tsFreq);

                using var baseSwap = factory.CreateSwapChainForHwnd(queue, window.Handle,
                    new SwapChainDescription1((uint)Width, (uint)Height, Format.R8G8B8A8_UNorm, false, Usage.RenderTargetOutput, 2,
                        Scaling.Stretch, SwapEffect.FlipDiscard, AlphaMode.Ignore, SwapChainFlags.None));
                var swap = scope.Track(baseSwap.QueryInterface<IDXGISwapChain3>());
                factory.MakeWindowAssociation(window.Handle, WindowAssociationFlags.IgnoreAltEnter);
                var backBuffers = new[] { scope.Track(swap.GetBuffer<ID3D12Resource>(0)), scope.Track(swap.GetBuffer<ID3D12Resource>(1)) };

                using var compiled = DxcCompiler.Compile(DxcShaderStage.Compute, Hlsl, "Main",
                    new DxcCompilerOptions { ShaderModel = new DxcShaderModel(6, 5), OptimizationLevel = 3 });
                var bytecode = compiled.GetObjectBytecodeMemory().ToArray();
                if (bytecode.Length == 0)
                    throw new InvalidOperationException("Falha ao compilar o shader: " + compiled.GetErrors());
                var rootSig = scope.Track(device.CreateRootSignature(bytecode));
                var pso = scope.Track(device.CreateComputePipelineState<ID3D12PipelineState>(new ComputePipelineStateDescription
                {
                    RootSignature = rootSig,
                    ComputeShader = bytecode,
                }));

                const int inFlight = 4;
                var allocs = new ID3D12CommandAllocator[inFlight];
                for (int i = 0; i < inFlight; i++)
                    allocs[i] = scope.Track(device.CreateCommandAllocator(CommandListType.Direct));
                var cl = scope.Track(device.CreateCommandList<ID3D12GraphicsCommandList4>(0, CommandListType.Direct, allocs[0], null));

                ID3D12Resource CreateBuffer(DisposableScope owner, ulong size, HeapType heap, ResourceStates state, ResourceFlags flags = ResourceFlags.None) =>
                    owner.Track(device.CreateCommittedResource(heap, ResourceDescription.Buffer(Math.Max(size, 256), flags), state, null));

                ID3D12Resource vb, ib, meshInfo, mats;
                ID3D12Resource[] blas;
                var objects = RtScene.BuildObjects();

                // Buffers de upload e scratch da montagem inicial são liberados assim que a GPU conclui o setup.
                using (var setupScope = new DisposableScope())
                {
                    ID3D12Resource CopyToGpu<T>(T[] data) where T : unmanaged
                    {
                        ulong size = (ulong)(data.Length * Marshal.SizeOf<T>());
                        var upload = CreateBuffer(setupScope, size, HeapType.Upload, ResourceStates.GenericRead);
                        unsafe
                        {
                            void* p;
                            upload.Map(0, null, &p);
                            fixed (T* src = data) System.Buffer.MemoryCopy(src, p, (long)size, (long)size);
                            upload.Unmap(0);
                        }
                        var gpu = CreateBuffer(scope, size, HeapType.Default, ResourceStates.Common);
                        cl.CopyBufferRegion(gpu, 0, upload, 0, size);
                        cl.ResourceBarrierTransition(gpu, ResourceStates.CopyDest, ResourceStates.NonPixelShaderResource);
                        return gpu;
                    }

                    var meshes = RtScene.BuildMeshes();
                    vb = CopyToGpu(meshes.Vertices.ToArray());
                    ib = CopyToGpu(meshes.Indices.ToArray());
                    meshInfo = CopyToGpu(meshes.Meshes.Select(m => new RtScene.MeshInfo { IndexOffset = m.IndexOffset, VertexOffset = m.VertexOffset }).ToArray());
                    mats = CopyToGpu(objects.Select(o => o.Material).ToArray());

                    blas = new ID3D12Resource[meshes.Meshes.Count];
                    ulong vtxStride = (ulong)Marshal.SizeOf<RtScene.Vertex>();
                    for (int m = 0; m < meshes.Meshes.Count; m++)
                    {
                        var mr = meshes.Meshes[m];
                        var geo = new RaytracingGeometryDescription(new RaytracingGeometryTrianglesDescription(
                            new GpuVirtualAddressAndStride(vb.GPUVirtualAddress + mr.VertexOffset * vtxStride, vtxStride),
                            Format.R32G32B32_Float, mr.VertexCount, 0, ib.GPUVirtualAddress + mr.IndexOffset * 4UL, Format.R32_UInt, mr.IndexCount),
                            RaytracingGeometryFlags.Opaque);
                        var inputs = new BuildRaytracingAccelerationStructureInputs
                        {
                            Type = RaytracingAccelerationStructureType.BottomLevel,
                            Flags = RaytracingAccelerationStructureBuildFlags.PreferFastTrace,
                            Layout = ElementsLayout.Array,
                            DescriptorsCount = 1,
                            GeometryDescriptions = new[] { geo },
                        };
                        var prebuild = device.GetRaytracingAccelerationStructurePrebuildInfo(inputs);
                        var scratch = CreateBuffer(setupScope, prebuild.ScratchDataSizeInBytes, HeapType.Default, ResourceStates.Common, ResourceFlags.AllowUnorderedAccess);
                        blas[m] = CreateBuffer(scope, prebuild.ResultDataMaxSizeInBytes, HeapType.Default, ResourceStates.RaytracingAccelerationStructure, ResourceFlags.AllowUnorderedAccess);
                        cl.BuildRaytracingAccelerationStructure(new BuildRaytracingAccelerationStructureDescription(blas[m].GPUVirtualAddress, inputs, 0, scratch.GPUVirtualAddress));
                        cl.ResourceBarrierUnorderedAccessView(blas[m]);
                    }

                    cl.Close();
                    queue.ExecuteCommandList(cl);
                    queue.Signal(fence, ++fenceValue);
                    WaitGpu(fenceValue);
                }

                var tlasInputs = new BuildRaytracingAccelerationStructureInputs
                {
                    Type = RaytracingAccelerationStructureType.TopLevel,
                    Flags = RaytracingAccelerationStructureBuildFlags.PreferFastBuild,
                    Layout = ElementsLayout.Array,
                    DescriptorsCount = (uint)objects.Count,
                };
                var tlasInfo = device.GetRaytracingAccelerationStructurePrebuildInfo(tlasInputs);
                var tlas = CreateBuffer(scope, tlasInfo.ResultDataMaxSizeInBytes, HeapType.Default, ResourceStates.RaytracingAccelerationStructure, ResourceFlags.AllowUnorderedAccess);
                var tlasScratch = CreateBuffer(scope, tlasInfo.ScratchDataSizeInBytes, HeapType.Default, ResourceStates.Common, ResourceFlags.AllowUnorderedAccess);

                int instSize = Marshal.SizeOf<InstanceDesc>();
                var instUpload = new ID3D12Resource[inFlight];
                var instPtr = new IntPtr[inFlight];
                var cbUpload = new ID3D12Resource[inFlight];
                var cbPtr = new IntPtr[inFlight];
                for (int i = 0; i < inFlight; i++)
                {
                    instUpload[i] = CreateBuffer(scope, (ulong)(objects.Count * instSize), HeapType.Upload, ResourceStates.GenericRead);
                    unsafe { void* p; instUpload[i].Map(0, null, &p); instPtr[i] = (IntPtr)p; }
                    cbUpload[i] = CreateBuffer(scope, 256, HeapType.Upload, ResourceStates.GenericRead);
                    unsafe { void* p; cbUpload[i].Map(0, null, &p); cbPtr[i] = (IntPtr)p; }
                }

                var output = scope.Track(device.CreateCommittedResource(HeapType.Default,
                    ResourceDescription.Texture2D(Format.R8G8B8A8_UNorm, (uint)Width, (uint)Height, 1, 1, 1, 0, ResourceFlags.AllowUnorderedAccess),
                    ResourceStates.UnorderedAccess, null));
                var heap = scope.Track(device.CreateDescriptorHeap(new DescriptorHeapDescription(
                    DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, 1, DescriptorHeapFlags.ShaderVisible)));
                device.CreateUnorderedAccessView(output, null, null, heap.GetCPUDescriptorHandleForHeapStart());

                var tsHeap = scope.Track(device.CreateQueryHeap<ID3D12QueryHeap>(new QueryHeapDescription(QueryHeapType.Timestamp, inFlight)));
                var tsReadback = CreateBuffer(scope, 8UL * inFlight, HeapType.Readback, ResourceStates.CopyDest);

                var (spp, bounces) = Quality;
                var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, Width / (float)Height, 0.1f, 2000);
                var sunDir = Vector3.Normalize(new Vector3(-0.4f, -0.75f, -0.35f));
                var frameFence = new ulong[inFlight];
                var passes = new List<RtPass>();
                var inst = new InstanceDesc[objects.Count];
                uint frameIndex = 0;
                bool hasWarmup = Warmup > TimeSpan.Zero;
                int totalPhases = Passes + (hasWarmup ? 1 : 0);

                for (int phase = 0; phase < totalPhases && !ShouldStop(); phase++)
                {
                    bool warm = hasWarmup && phase == 0;
                    var duration = warm ? Warmup : PassDuration;
                    Phase = warm ? "Aquecendo" : $"Passada {phase - (hasWarmup ? 1 : 0) + 1} de {Passes}";
                    var frameTimes = new List<double>(20000);
                    var clock = Stopwatch.StartNew();
                    double lastPresent = 0, lastTitle = 0;
                    int titleFrames = 0;
                    ulong lastTs = 0;
                    long frame = 0;

                    while (!ShouldStop() && clock.Elapsed < duration)
                    {
                        window.PumpMessages();
                        if (ShouldStop()) break;

                        int slot = (int)(frame % inFlight);
                        if (frame >= inFlight)
                        {
                            WaitGpu(frameFence[slot]);
                            ulong ts;
                            unsafe
                            {
                                void* p;
                                var range = new Vortice.Direct3D12.Range((nuint)(slot * 8), (nuint)(slot * 8 + 8));
                                tsReadback.Map(0, range, &p);
                                ts = *(ulong*)((byte*)p + slot * 8);
                                tsReadback.Unmap(0, new Vortice.Direct3D12.Range(0, 0));
                            }
                            if (lastTs != 0 && ts > lastTs) frameTimes.Add((ts - lastTs) * 1000.0 / tsFreq);
                            lastTs = ts;
                            titleFrames++;
                        }

                        // Câmera e objetos pelo tempo da passada: a mesma cena no mesmo instante em qualquer PC.
                        float t = (float)(clock.Elapsed.TotalSeconds / duration.TotalSeconds);
                        float secs = (float)clock.Elapsed.TotalSeconds;
                        var (eye, target) = RtScene.CameraAt(t);
                        var vp = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY) * proj;
                        Matrix4x4.Invert(vp, out var inv);
                        var cbData = new FrameCB
                        {
                            InvViewProj = inv,
                            CamPos = eye,
                            Time = secs,
                            SunDir = sunDir,
                            FrameIndex = frameIndex++,
                            Width = (uint)Width,
                            Height = (uint)Height,
                            Spp = spp,
                            Bounces = bounces,
                        };
                        Marshal.StructureToPtr(cbData, cbPtr[slot], false);
                        for (int i = 0; i < objects.Count; i++)
                        {
                            var o = objects[i];
                            var m = RtScene.ObjectMatrix(o, secs);
                            inst[i] = new InstanceDesc
                            {
                                M00 = m.M11,
                                M01 = m.M21,
                                M02 = m.M31,
                                M03 = m.M41,
                                M10 = m.M12,
                                M11 = m.M22,
                                M12 = m.M32,
                                M13 = m.M42,
                                M20 = m.M13,
                                M21 = m.M23,
                                M22 = m.M33,
                                M23 = m.M43,
                                IdMask = (uint)i | 0xFF000000u,
                                ContribFlags = 0x05000000u, // CULL_DISABLE | FORCE_OPAQUE
                                Blas = blas[o.Mesh].GPUVirtualAddress,
                            };
                        }
                        unsafe
                        {
                            fixed (InstanceDesc* src = inst)
                                System.Buffer.MemoryCopy(src, (void*)instPtr[slot], (long)inst.Length * instSize, (long)inst.Length * instSize);
                        }

                        allocs[slot].Reset();
                        cl.Reset(allocs[slot], pso);
                        tlasInputs.InstanceDescriptions = instUpload[slot].GPUVirtualAddress;
                        cl.BuildRaytracingAccelerationStructure(new BuildRaytracingAccelerationStructureDescription(tlas.GPUVirtualAddress, tlasInputs, 0, tlasScratch.GPUVirtualAddress));
                        cl.ResourceBarrierUnorderedAccessView(tlas);

                        cl.SetComputeRootSignature(rootSig);
                        cl.SetDescriptorHeaps(heap);
                        cl.SetComputeRootConstantBufferView(0, cbUpload[slot].GPUVirtualAddress);
                        cl.SetComputeRootShaderResourceView(1, tlas.GPUVirtualAddress);
                        cl.SetComputeRootShaderResourceView(2, vb.GPUVirtualAddress);
                        cl.SetComputeRootShaderResourceView(3, ib.GPUVirtualAddress);
                        cl.SetComputeRootShaderResourceView(4, meshInfo.GPUVirtualAddress);
                        cl.SetComputeRootShaderResourceView(5, mats.GPUVirtualAddress);
                        cl.SetComputeRootDescriptorTable(6, heap.GetGPUDescriptorHandleForHeapStart());
                        cl.Dispatch((uint)((Width + 7) / 8), (uint)((Height + 7) / 8), 1);
                        cl.ResourceBarrierUnorderedAccessView(output);

                        double now = clock.Elapsed.TotalSeconds;
                        // Mostra na janela a ~30 Hz (cópia barata); o resto dos frames só é calculado.
                        bool present = now - lastPresent >= 1 / 30.0;
                        if (present)
                        {
                            var bb = backBuffers[swap.CurrentBackBufferIndex];
                            cl.ResourceBarrierTransition(output, ResourceStates.UnorderedAccess, ResourceStates.CopySource);
                            cl.ResourceBarrierTransition(bb, ResourceStates.Present, ResourceStates.CopyDest);
                            cl.CopyResource(bb, output);
                            cl.ResourceBarrierTransition(bb, ResourceStates.CopyDest, ResourceStates.Present);
                            cl.ResourceBarrierTransition(output, ResourceStates.CopySource, ResourceStates.UnorderedAccess);
                        }
                        cl.EndQuery(tsHeap, QueryType.Timestamp, (uint)slot);
                        cl.ResolveQueryData(tsHeap, QueryType.Timestamp, (uint)slot, 1, tsReadback, (ulong)(slot * 8));
                        cl.Close();
                        queue.ExecuteCommandList(cl);
                        if (present) { swap.Present(0, PresentFlags.None); lastPresent = now; }
                        queue.Signal(fence, ++fenceValue);
                        frameFence[slot] = fenceValue;
                        frame++;

                        if (now - lastTitle >= 0.5)
                        {
                            CurrentFps = titleFrames / (now - lastTitle);
                            titleFrames = 0;
                            lastTitle = now;
                            Progress = (phase + now / duration.TotalSeconds) / totalPhases;
                            window.SetTitle($"Benchmark · Ray tracing · {Width}x{Height} · {Preset} · {Phase} · {CurrentFps:0} FPS");
                        }
                    }

                    WaitGpu(fenceValue);
                    if (ShouldStop()) break;
                    if (warm) continue;

                    double sum = frameTimes.Sum() / 1000.0;
                    passes.Add(new RtPass(
                        sum > 0 ? frameTimes.Count / sum : 0,
                        FrameTimeStats.Low1Fps(frameTimes),
                        frameTimes.Count));
                }

                if (ShouldStop()) { Cancelled = true; return null; }
                var result = new GpuRtResult(passes, Width, Height, Preset, adapterName, 0);
                return result with { GigaRaysPerSec = result.MedianFps * Width * Height * MaxRaysPerPixel / 1e9 };
            }
            finally
            {
                try
                {
                    if (queue != null && fence != null)
                    {
                        queue.Signal(fence, ++fenceValue);
                        WaitGpu(fenceValue);
                    }
                }
                catch { }
            }
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
