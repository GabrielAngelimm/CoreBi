using CoreBi.Engine.Infrastructure;
using CoreBi.Engine.Monitoring.Graphics;

namespace CoreBi.Desktop;

public partial class MainWindow
{
    internal List<GpuInfo> _gpus = new();
    internal GpuInfo? _gpu;
    private GpuBoard? _gpuBoard;
    private readonly GpuMonitor _gpuMonitor = new();

    // ---------------- Placa de vídeo ----------------

    /// <summary>
    /// Monta a página para uma placa: identificação, ficha técnica (sem linhas vazias nem dados repetidos entre grupos) e os
    /// instrumentos, que começam vazios e só leem o driver com a página à vista (UpdateGpuLive).
    /// </summary>
    internal void ShowGpu(GpuInfo g)
    {
        _gpu = g;
        var spec = GpuSheet.Flatten(g);
        GpuVendor.Text = g.Vendor;
        GpuName.Text = HardwareNaming.ShortGpuName(g.Name);
        GpuName.ToolTip = g.Name;
        GpuSubtitle.Text = GpuSheet.Subtitle(spec);
        PageGpu.GpuReadouts.ItemsSource = GpuSheet.Readouts(g, spec);
        var facts = GpuSheet.Facts(g);
        GpuSections.ItemsSource = GpuSheet.Groups(g, spec, livePcie: facts.Any(f => f.Label == "PCIe em uso"));
        GpuExtras.ItemsSource = facts;
        _gpuBoard = new GpuBoard(g, spec);
        PageGpu.GpuGauges.ItemsSource = _gpuBoard.Gauges;
        PageGpu.ShowNoSensors(false);
        if (PageGpu.IsVisible) SampleGpuBoard();
    }

    private void SampleGpuBoard()
    {
        _gpuBoard!.Sample(_monitor);
        PageGpu.ShowNoSensors(_gpuBoard.NoSensors);
    }

    /// <summary>Nenhuma placa encontrada pelo Windows: a página diz isso em vez de ficar lendo para sempre.</summary>
    internal void ShowNoGpu()
    {
        GpuVendor.Text = "";
        GpuName.Text = "Nenhuma placa de vídeo encontrada";
        GpuSubtitle.Text = "";
        PageGpu.GpuSensorsLink.Visibility = System.Windows.Visibility.Collapsed;
        PageGpu.ShowNoSensors(false);
    }

    /// <summary>Sensores da placa à vista, em Sensores já filtrado nela.</summary>
    internal void OpenGpuSensors() => GoToPage(_gpu != null ? $"PageSensors:gpu:{_gpu.Name}" : "PageSensors:gpu");

    internal void UpdateGpuLive()
    {
        _gpuMonitor.UpdateLoads(_gpus);
        // Barra lateral: GPU principal (dedicada = primeira da lista).
        if (_gpus.Count > 0)
        {
            var load = MainGpuLoad();
            SideGpuTemp.Text = MainGpuTemp() is float t ? $"{t:0.#} °C" : "—";
            SideGpuLoad.Text = load is float l ? $"{l:0} %" : "—";
            Ui.Glide(SideGpuBar, (load ?? 0) / 100.0, LiveMotion);
        }
        // A página só consulta o driver quando está à vista: o CoreBi fica aberto em jogos.
        if (_gpu != null && _gpuBoard != null && PageGpu.IsVisible)
        {
            SampleGpuBoard();
            GpuExtras.ItemsSource = GpuSheet.Facts(_gpu);
        }
    }
}
