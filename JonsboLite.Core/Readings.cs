namespace JonsboLite;

public enum TempUnit { Celsius = 0, Fahrenheit = 1 }

/// <summary>
/// One snapshot of everything the panel can display. Temperatures are always Celsius
/// here; conversion happens at pack time so the unit is decided in exactly one place.
/// </summary>
public sealed class Readings
{
    private readonly double[] _values = new double[PanelFields.All.Length];

    public double this[PanelField field]
    {
        get => _values[(int)field];
        set => _values[(int)field] = value;
    }

    public double CpuTemp { get => this[PanelField.CpuTemp]; set => this[PanelField.CpuTemp] = value; }
    public double CpuLoad { get => this[PanelField.CpuLoad]; set => this[PanelField.CpuLoad] = value; }
    public double CpuPower { get => this[PanelField.CpuPower]; set => this[PanelField.CpuPower] = value; }
    public double CpuClock { get => this[PanelField.CpuClock]; set => this[PanelField.CpuClock] = value; }
    public double CpuVid { get => this[PanelField.CpuVid]; set => this[PanelField.CpuVid] = value; }
    public double GpuTemp { get => this[PanelField.GpuTemp]; set => this[PanelField.GpuTemp] = value; }
    public double GpuLoad { get => this[PanelField.GpuLoad]; set => this[PanelField.GpuLoad] = value; }
    public double GpuPower { get => this[PanelField.GpuPower]; set => this[PanelField.GpuPower] = value; }
    public double GpuClock { get => this[PanelField.GpuClock]; set => this[PanelField.GpuClock] = value; }
    public double FanRpm { get => this[PanelField.FanRpm]; set => this[PanelField.FanRpm] = value; }
    public double PumpRpm { get => this[PanelField.PumpRpm]; set => this[PanelField.PumpRpm] = value; }
    public double MemLoad { get => this[PanelField.MemLoad]; set => this[PanelField.MemLoad] = value; }
}
