using LibreHardwareMonitor.Hardware;

namespace CoreBi.Engine.Monitoring.Sensors;

internal interface IHardwareSource
{
    IEnumerable<IHardware> Hardware { get; }
    void Open();
    void Close();
}

internal sealed class LibreHardwareSource : IHardwareSource
{
    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMemoryEnabled = true,
        IsMotherboardEnabled = true,
        IsStorageEnabled = true,
        IsNetworkEnabled = true,
        IsControllerEnabled = true
    };
    public IEnumerable<IHardware> Hardware => _computer.Hardware;
    public void Open() => _computer.Open();
    public void Close() => _computer.Close();
}
