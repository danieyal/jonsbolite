namespace JonsboLite;

/// <summary>The twelve measurements the panel can display. Order is display order.</summary>
public enum PanelField
{
    CpuTemp,
    CpuLoad,
    CpuPower,
    CpuClock,
    CpuVid,
    GpuTemp,
    GpuLoad,
    GpuPower,
    GpuClock,
    FanRpm,
    PumpRpm,
    MemLoad,
}

public static class PanelFields
{
    public static readonly PanelField[] All = Enum.GetValues<PanelField>();

    /// <summary>camelCase name used as the key in jonsbolite.json.</summary>
    public static string ConfigKey(this PanelField field)
    {
        string n = field.ToString();
        return char.ToLowerInvariant(n[0]) + n[1..];
    }

    public static bool TryParse(string key, out PanelField field) =>
        Enum.TryParse(key, ignoreCase: true, out field);
}
