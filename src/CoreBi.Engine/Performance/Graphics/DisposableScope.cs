namespace CoreBi.Engine.Performance.Graphics;

/// <summary>Coleção de recursos nativos liberados em ordem inversa à criação (COM/Direct3D).</summary>
internal sealed class DisposableScope : IDisposable
{
    private readonly List<IDisposable> _items = new();

    public T Track<T>(T resource) where T : IDisposable
    {
        _items.Add(resource);
        return resource;
    }

    public void Dispose()
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            try { _items[i].Dispose(); }
            catch { }
        }
        _items.Clear();
    }
}
