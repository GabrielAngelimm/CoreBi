namespace CoreBi.Engine.Performance;

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
