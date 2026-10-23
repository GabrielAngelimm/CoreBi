using CoreBi.Engine.Performance;

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
