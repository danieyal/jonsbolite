namespace JonsboLite;

/// <summary>One sensor the machine exposes, as reported by <see cref="ISensorSource.Enumerate"/>.</summary>
/// <param name="Hardware">Device the sensor belongs to, e.g. "AMD Ryzen 7 8700F".</param>
/// <param name="Name">Sensor name within that device, e.g. "Core (Tctl/Tdie)".</param>
/// <param name="Kind">Quantity measured, e.g. "Temperature" — free text, backend specific.</param>
/// <param name="Value">Current value, or null if the backend could not read it.</param>
public readonly record struct SensorReading(string Hardware, string Name, string Kind, double? Value)
{
    /// <summary>The "Hardware / Name" form accepted by the config's sensor pins.</summary>
    public string QualifiedName => $"{Hardware} / {Name}";
}

/// <summary>
/// Where panel values come from. LibreHardwareMonitor on Windows; hwmon and procfs would
/// be the Linux equivalent. Backends are responsible for choosing which of the machine's
/// sensors feeds each <see cref="PanelField"/>, honouring any pins in the config.
/// </summary>
public interface ISensorSource : IDisposable
{
    /// <summary>Samples every field. Called at the frame cadence, so it must be cheap.</summary>
    Readings Read();

    /// <summary>
    /// Which sensor currently feeds a field, for diagnostics. Returns null when nothing
    /// suitable was found — a field left deliberately unbound rather than mis-bound.
    /// </summary>
    string? BoundSensorName(PanelField field);

    /// <summary>Every sensor the backend can see, for the config-pinning workflow.</summary>
    IEnumerable<SensorReading> Enumerate();
}
