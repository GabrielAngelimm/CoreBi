using System.Runtime.InteropServices;

namespace CoreBi.Engine.Infrastructure;

internal sealed class ComScope : IDisposable
{
    private readonly List<object> _objects = new();

    public T Track<T>(T value)
    {
        if (value != null && Marshal.IsComObject(value)) _objects.Add(value);
        return value;
    }

    public void Dispose()
    {
        for (int i = _objects.Count - 1; i >= 0; i--)
        {
            try { Marshal.FinalReleaseComObject(_objects[i]); }
            catch (Exception ex) { DiagnosticLog.Write("com.release", ex); }
        }
        _objects.Clear();
    }
}
