using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using LibreHardwareMonitor.Hardware;
using CoreBi.Engine.Monitoring.Graphics;
using CoreBi.Engine.Monitoring.Sensors;

using System.ComponentModel;

using System.Windows.Data;

namespace CoreBi.Desktop;

public partial class MainWindow
{
    // Placa de vídeo
    internal List<GpuInfo> _gpus = new();
    internal GpuInfo? _gpu;
    private readonly WindowsGpuUsage _windowsGpuUsage = new();

    // ---------------- Placa de vídeo ----------------

    internal void ShowGpu(GpuInfo g)
    {
        _gpu = g;
        GpuName.Text = g.Name;
        GpuSubtitle.Text = g.Subtitle;
        GpuVendor.Text = g.Vendor;
        GpuSections.ItemsSource = g.Sections;

        // Sensores do LibreHardwareMonitor deste hardware, em ordem fixa (clock, temperatura, carga...).
        var live = new ListCollectionView(_monitor.Rows)
        {
            Filter = o => ((SensorRow)o).Hardware == g.Name
        };
        live.SortDescriptions.Add(new SortDescription(nameof(SensorRow.Order), ListSortDirection.Ascending));
        live.SortDescriptions.Add(new SortDescription(nameof(SensorRow.Name), ListSortDirection.Ascending));
        GpuLive.ItemsSource = live;
        GpuExtras.ItemsSource = g.LiveExtras();
    }

    internal void UpdateGpuLive()
    {
        foreach (var gpu in _gpus) gpu.WindowsLoad = _windowsGpuUsage.GetLoad(gpu.AdapterLuid);
        // Barra lateral: GPU principal (dedicada = primeira da lista).
        if (_gpus.Count > 0)
        {
            var load = MainGpuLoad();
            SideGpuTemp.Text = MainGpuTemp() is float t ? $"{t:0.#} °C" : "—";
            SideGpuLoad.Text = load is float l ? $"{l:0} %" : "—";
            SideGpuBar.ScaleX = Math.Clamp((load ?? 0) / 100.0, 0, 1);
        }
        if (_gpu != null && PageGpu.IsVisible) GpuExtras.ItemsSource = _gpu.LiveExtras();
    }

}
