using Vortice.DXGI;

namespace CoreBi.Engine.Performance.Graphics;

internal static class DxgiAdapters
{
    /// <summary>Seleciona o adaptador DXGI físico pelo nome pedido ou pela maior VRAM dedicada; descarta os demais.</summary>
    public static IDXGIAdapter1 PickHardwareAdapter(IDXGIFactory1 factory, string preferredName, out string description)
    {
        var hardware = new List<IDXGIAdapter1>();
        for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
        {
            if ((adapter.Description1.Flags & AdapterFlags.Software) == 0) hardware.Add(adapter);
            else adapter.Dispose();
        }
        if (hardware.Count == 0)
            throw new InvalidOperationException("Nenhuma placa de vídeo compatível encontrada.");

        var pick = hardware.FirstOrDefault(a => preferredName.Length > 0 &&
                       a.Description1.Description.Contains(preferredName, StringComparison.OrdinalIgnoreCase))
                   ?? hardware.OrderByDescending(a => a.Description1.DedicatedVideoMemory).First();
        foreach (var adapter in hardware)
        {
            if (!ReferenceEquals(adapter, pick)) adapter.Dispose();
        }
        description = pick.Description1.Description;
        return pick;
    }
}
