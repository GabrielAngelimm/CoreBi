using CoreBi.Engine.Monitoring.Sensors;
using CoreBi.Engine.Performance;

using CoreBi.Engine.Persistence;
using System.Windows;

namespace CoreBi.Desktop.Services;

internal sealed class FeatureContext(Window owner, WorkloadCoordinator workloads, CancellationToken lifetime)
{
    private int _critical;
    public Window Owner { get; } = owner;
    public WorkloadCoordinator Workloads { get; } = workloads;
    public CancellationToken Lifetime { get; } = lifetime;
    public bool IsClosing => Lifetime.IsCancellationRequested;
    public bool HasCriticalOperation => Volatile.Read(ref _critical) != 0;
    public IDisposable BeginCriticalOperation() { Interlocked.Increment(ref _critical); return new Scope(this); }

    private sealed class Scope(FeatureContext context) : IDisposable
    {
        private FeatureContext? _context = context;
        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _context, null);
            if (current != null) Interlocked.Decrement(ref current._critical);
        }
    }
}

internal static class PermissionGuard
{
    public static bool RequireAdmin(Window owner)
    {
        if (SensorMonitor.IsElevated) return true;
        MessageBox.Show(owner, "Esta ação precisa do CoreBi aberto como administrador.", "CoreBi", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }
}
