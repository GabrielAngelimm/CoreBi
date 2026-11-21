using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CoreBi.Desktop.Features.Monitoring.Inventory;
using CoreBi.Desktop.Services;
using CoreBi.Engine.Infrastructure;
using CoreBi.Engine.Monitoring.Graphics;

namespace CoreBi.Desktop.Views;

public partial class GraphicsView : UserControl
{
    private FeatureContext? _context;
    private UniformGrid? _gaugeGrid;
    private ColumnsPanel? _specColumns;
    private GpuInfo? _selectedGpu;
    private GpuBoard? _gpuBoard;

    public GraphicsView()
    {
        InitializeComponent();
        Skeleton.Set(true, GpuPlateCard, GpuSpecCard); // até a leitura da placa de vídeo, logo depois de o CoreBi abrir
        SizeChanged += (_, _) => FitColumns();
        ((INotifyCollectionChanged)GpuSections.Items).CollectionChanged += (_, _) => FitColumns();
    }

    internal void Initialize(FeatureContext context) => _context = context;

    internal void PopulateGpus(IReadOnlyList<GpuInfo> gpus)
    {
        GpuChips.Children.Clear();
        for (int i = 0; i < gpus.Count && gpus.Count > 1; i++)
        {
            var gpu = gpus[i];
            var chip = new RadioButton
            {
                Content = HardwareNaming.ShortGpuName(gpu.Name),
                ToolTip = gpu.Name,
                GroupName = "Gpu",
                Style = (Style)FindResource("Chip"),
                IsChecked = i == 0,
            };
            chip.Checked += (_, _) => ShowGpu(gpu);
            GpuChips.Children.Add(chip);
        }

        if (gpus.Count > 0) ShowGpu(gpus[0]);
        else ShowNoGpu();
        Skeleton.Set(false, GpuPlateCard, GpuSpecCard);
    }

    internal void ShowGpu(GpuInfo gpu)
    {
        _selectedGpu = gpu;
        var spec = GpuSheet.Flatten(gpu);
        GpuVendor.Text = gpu.Vendor;
        GpuName.Text = HardwareNaming.ShortGpuName(gpu.Name);
        GpuName.ToolTip = gpu.Name;
        GpuSubtitle.Text = GpuSheet.Subtitle(spec);
        GpuReadouts.ItemsSource = GpuSheet.Readouts(gpu, spec);
        var facts = GpuSheet.Facts(gpu);
        GpuSections.ItemsSource = GpuSheet.Groups(gpu, spec, livePcie: facts.Any(f => f.Label == "PCIe em uso"));
        GpuExtras.ItemsSource = facts;
        GpuSensorsLink.Visibility = Visibility.Visible;
        _gpuBoard = new GpuBoard(gpu, spec);
        GpuGauges.ItemsSource = _gpuBoard.Gauges;
        ShowNoSensors(false);
        if (IsVisible) SampleGpuBoard();
    }

    internal void ShowNoGpu()
    {
        _selectedGpu = null;
        _gpuBoard = null;
        GpuVendor.Text = "";
        GpuName.Text = "Nenhuma placa de vídeo encontrada";
        GpuSubtitle.Text = "";
        GpuSensorsLink.Visibility = Visibility.Collapsed;
        ShowNoSensors(false);
    }

    internal void Tick()
    {
        // A página só consulta o driver quando está à vista: o CoreBi fica aberto em jogos.
        if (_selectedGpu == null || _gpuBoard == null || !IsVisible) return;
        SampleGpuBoard();
        GpuExtras.ItemsSource = GpuSheet.Facts(_selectedGpu);
    }

    private void SampleGpuBoard()
    {
        if (_context == null || _gpuBoard == null) return;
        _gpuBoard.Sample(_context.Sensors);
        ShowNoSensors(_gpuBoard.NoSensors);
    }

    internal void ShowNoSensors(bool none) => GpuNoSensors.Visibility = none ? Visibility.Visible : Visibility.Collapsed;

    private void GaugeGrid_Loaded(object sender, RoutedEventArgs e)
    {
        _gaugeGrid = (UniformGrid)sender;
        FitColumns();
    }

    private void SpecColumns_Loaded(object sender, RoutedEventArgs e)
    {
        _specColumns = (ColumnsPanel)sender;
        FitColumns();
    }

    private void GpuGauges_SizeChanged(object sender, SizeChangedEventArgs e) => FitColumns();

    /// <summary>
    /// Instrumentos numa fila só a partir de 1300 px (abaixo, quatro por linha); ficha técnica em 4, 3, 2 ou 1 coluna conforme a
    /// largura, para cada coluna ter ao menos uns 340 px (chave e valor sem quebrar), e nunca mais colunas que grupos (com duas no mínimo).
    /// </summary>
    private void FitColumns()
    {
        double w = GpuGauges.ActualWidth;
        if (_gaugeGrid != null && w > 0)
        {
            bool row = w >= 1300;
            _gaugeGrid.Rows = row ? 1 : 0;
            _gaugeGrid.Columns = row ? 0 : 4;
        }
        if (_specColumns != null && GpuSections.ActualWidth > 0)
        {
            double s = GpuSections.ActualWidth;
            int fit = s >= 1460 ? 4 : s >= 1080 ? 3 : s >= 700 ? 2 : 1;
            _specColumns.Columns = Math.Min(fit, Math.Max(2, GpuSections.Items.Count));
        }
    }

    private void GpuSensorsLink_Click(object sender, RoutedEventArgs e) =>
        _context?.Navigate(_selectedGpu != null ? $"PageSensors:gpu:{_selectedGpu.Name}" : "PageSensors:gpu");
}
