using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CoreBi.Desktop.Features.Monitoring.Inventory;

namespace CoreBi.Desktop.Views;

public partial class GraphicsView : UserControl
{
    internal MainWindow? Owner { get; set; }
    private UniformGrid? _gaugeGrid;
    private ColumnsPanel? _specColumns;

    public GraphicsView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => FitColumns();
    }

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
    /// largura, para cada coluna ter ao menos uns 340 px (chave e valor sem quebrar).
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
            _specColumns.Columns = s >= 1460 ? 4 : s >= 1080 ? 3 : s >= 700 ? 2 : 1;
        }
    }

    private void GpuSensorsLink_Click(object sender, RoutedEventArgs e) => Owner?.OpenGpuSensors();
}
