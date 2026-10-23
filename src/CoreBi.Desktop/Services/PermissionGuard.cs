using CoreBi.Engine.Monitoring.Sensors;
using System.Windows;

namespace CoreBi.Desktop.Services;

internal static class PermissionGuard
{
    public static bool RequireAdmin(Window owner)
    {
        if (SensorMonitor.IsElevated) return true;
        MessageBox.Show(owner, "Esta ação precisa do CoreBi aberto como administrador.", "CoreBi", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }
}
