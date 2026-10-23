using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;

namespace CoreBi.Desktop.Views;

public partial class GraphicsView : UserControl
{
    internal MainWindow? Owner { get; set; }
    public GraphicsView() => InitializeComponent();

}
