using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace CoreBi.Engine.Performance.Graphics;

/// <summary>
/// Dispositivo Direct3D 11, swapchain FlipDiscard e fila de fences de evento para renderização contínua fora da tela
/// (sem ficar preso ao V-Sync do monitor, copiando para o back buffer só na cadência de exibição).
/// </summary>
internal sealed class D3D11OffscreenHost : IDisposable
{
    private readonly DisposableScope _scope = new();
    private readonly ID3D11Query[] _fences;
    private ID3D11Texture2D? _backBuffer;

    public ID3D11Device Device { get; }
    public ID3D11DeviceContext Context { get; }
    public IDXGISwapChain1 SwapChain { get; }
    public ID3D11Texture2D BackBuffer => _backBuffer!;
    public string AdapterName { get; }
    public bool AllowTearing { get; }
    public SwapChainFlags SwapFlags => AllowTearing ? SwapChainFlags.AllowTearing : SwapChainFlags.None;
    public int InFlight { get; }

    public D3D11OffscreenHost(IntPtr hwnd, int width, int height, string preferredAdapter, int inFlight, bool requestTearing)
    {
        InFlight = inFlight;
        try
        {
            var factory = _scope.Track(DXGI.CreateDXGIFactory1<IDXGIFactory2>());
            var adapter = _scope.Track(DxgiAdapters.PickHardwareAdapter(factory, preferredAdapter, out string adapterName));
            AdapterName = adapterName;

            D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
                out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
            Device = _scope.Track(device);
            Context = _scope.Track(context);

            if (requestTearing)
            {
                using var f5 = factory.QueryInterfaceOrNull<IDXGIFactory5>();
                AllowTearing = f5?.PresentAllowTearing ?? false;
            }

            SwapChain = _scope.Track(factory.CreateSwapChainForHwnd(Device, hwnd,
                new SwapChainDescription1((uint)width, (uint)height, Format.B8G8R8A8_UNorm, false, Usage.RenderTargetOutput, 2,
                    Scaling.Stretch, SwapEffect.FlipDiscard, AlphaMode.Ignore, SwapFlags)));
            factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter);
            _backBuffer = SwapChain.GetBuffer<ID3D11Texture2D>(0);

            _fences = new ID3D11Query[inFlight];
            for (int i = 0; i < inFlight; i++)
                _fences[i] = _scope.Track(Device.CreateQuery(QueryType.Event, QueryFlags.None));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public ID3D11Query FenceFor(long frame) => _fences[frame % InFlight];

    public void WaitFence(long frame, Func<bool> shouldStop, bool spinWait)
    {
        if (frame < InFlight) return;
        var fence = FenceFor(frame);
        while (!Context.IsDataAvailable(fence) && !shouldStop())
        {
            if (spinWait) Thread.SpinWait(20);
            else Thread.Yield();
        }
    }

    public void ResizeSwapChain(int width, int height)
    {
        _backBuffer?.Dispose();
        _backBuffer = null;
        SwapChain.ResizeBuffers(2, (uint)width, (uint)height, Format.B8G8R8A8_UNorm, SwapFlags).CheckError();
        _backBuffer = SwapChain.GetBuffer<ID3D11Texture2D>(0);
    }

    public void Dispose()
    {
        _backBuffer?.Dispose();
        _backBuffer = null;
        _scope.Dispose();
    }
}
