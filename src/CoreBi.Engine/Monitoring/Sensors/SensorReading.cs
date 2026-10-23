using LibreHardwareMonitor.Hardware;



namespace CoreBi.Engine.Monitoring.Sensors;

/// <summary>Immutable value copy for background collection and reports.</summary>
public sealed record SensorReading(string Id, string Hardware, string Name, SensorType Type, HardwareType HardwareType, float? RawValue,
    string Category = "", string Value = "", string Min = "", string Max = "");
