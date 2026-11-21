namespace CoreBi.Engine.Performance;

/// <summary>Identificadores canônicos das operações exclusivas gerenciadas pelo <see cref="WorkloadCoordinator"/>.</summary>
public static class WorkloadOperations
{
    public const string CpuRender = "CPU render";
    public const string CpuSimulation = "CPU simulation";
    public const string GpuRaster = "GPU raster";
    public const string GpuRayTracing = "GPU ray tracing";
    public const string RamAndLoading = "RAM and loading";
    public const string DiskIo = "Disk IO";
    public const string Stress = "Stress";
    public const string Network = "Network test";
    public const string DriverDownload = "Driver download";
    public const string WindowsUpdate = "Windows Update";
    public const string DriverBackup = "Driver backup";
    public const string InstallApps = "Install apps";
}

/// <summary>One owner for every benchmark/stress operation, including asynchronous preparation.</summary>
public sealed class WorkloadCoordinator
{
    private readonly object _sync = new();
    private string? _active;
    public string? ActiveOperation { get { lock (_sync) return _active; } }
    public bool IsBusy => ActiveOperation != null;

    public IDisposable? TryBegin(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        lock (_sync)
        {
            if (_active != null) return null;
            _active = operation;
            return new Lease(this);
        }
    }

    private sealed class Lease(WorkloadCoordinator owner) : IDisposable
    {
        private WorkloadCoordinator? _owner = owner;
        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner == null) return;
            lock (owner._sync) owner._active = null;
        }
    }
}
