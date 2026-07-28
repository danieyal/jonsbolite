using System.Runtime.Versioning;
using LibreHardwareMonitor.Hardware;

namespace JonsboLite;

/// <summary>
/// Replaces the original's SystemInfos.exe + cpuidsdk64.dll + HWiNFO64.dll + Hardware.Info.dll
/// stack (and the 64 KB shared-memory JSON hop) with one in-process LibreHardwareMonitor.
///
/// Each frame field is bound to a sensor once, on first update, using an ordered list of
/// name preferences. A config file can pin any field to an exact sensor name, which is the
/// headless equivalent of the official app's sensor picker.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Sensors : IDisposable
{
    private readonly Computer _computer;
    private readonly UpdateVisitor _visitor = new();
    private readonly Config _config;
    private readonly Dictionary<string, ISensor> _bound = new();
    private bool _mapped;

    public Sensors(Config config)
    {
        _config = config;
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsStorageEnabled = false,
            IsNetworkEnabled = false,
            IsPsuEnabled = false,
            IsBatteryEnabled = false,
        };
        _computer.Open();
    }

    public Readings Read()
    {
        _computer.Accept(_visitor);
        if (!_mapped) { Bind(); _mapped = true; }

        return new Readings
        {
            CpuTemp = Value("cpuTemp"),
            CpuLoad = Value("cpuLoad"),
            CpuPower = Value("cpuPower"),
            CpuClock = Value("cpuClock"),
            CpuVid = Value("cpuVid"),
            GpuTemp = Value("gpuTemp"),
            GpuLoad = Value("gpuLoad"),
            GpuPower = Value("gpuPower"),
            GpuClock = Value("gpuClock"),
            FanRpm = Value("fanRpm"),
            PumpRpm = Value("pumpRpm"),
            MemLoad = Value("memLoad"),
        };
    }

    private double Value(string field) =>
        _bound.TryGetValue(field, out ISensor? s) ? s.Value ?? 0 : 0;

    public string BoundSensorName(string field) =>
        _bound.TryGetValue(field, out ISensor? s) ? $"{s.Hardware.Name} / {s.Name}" : "(none)";

    public static readonly string[] Fields =
    {
        "cpuTemp", "cpuLoad", "cpuPower", "cpuClock", "cpuVid",
        "gpuTemp", "gpuLoad", "gpuPower", "gpuClock",
        "fanRpm", "pumpRpm", "memLoad",
    };

    private void Bind()
    {
        var all = AllSensors().ToList();

        bool IsCpu(ISensor s) => s.Hardware.HardwareType == HardwareType.Cpu;
        bool IsGpu(ISensor s) => s.Hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
        bool IsMem(ISensor s) => s.Hardware.HardwareType == HardwareType.Memory;
        bool IsBoard(ISensor s) => s.Hardware.HardwareType is HardwareType.Motherboard or HardwareType.SuperIO
                                   or HardwareType.Cooler or HardwareType.EmbeddedController;

        // Preference lists are ordered: first substring that matches a sensor name wins.
        Bind("cpuTemp", all, IsCpu, SensorType.Temperature, "Core (Tctl/Tdie)", "CPU Package", "Package", "Core Average", "CPU Core");
        Bind("cpuLoad", all, IsCpu, SensorType.Load, "CPU Total", "Total");
        Bind("cpuPower", all, IsCpu, SensorType.Power, "CPU Package", "Package");
        Bind("cpuClock", all, IsCpu, SensorType.Clock, "Core #1", "CPU Core");
        // CPU core voltage. The per-core "Core #N VID" sensors are unpopulated on several
        // Zen APUs (they pin at 0.08 V) and "Core (SVI2 TFN)" can misreport high, so prefer
        // the SuperIO-measured Vcore rail when the board exposes one. No blind fallback:
        // the remaining board voltages are things like "+12V" and "AVSB".
        Bind("cpuVid", all, s => IsCpu(s) || IsBoard(s), SensorType.Voltage,
             "Vcore", "CPU Core", "Core (SVI2", "VID");

        Bind("gpuTemp", all, IsGpu, SensorType.Temperature, "GPU Core", "GPU Hot Spot", "Core");
        Bind("gpuLoad", all, IsGpu, SensorType.Load, "GPU Core", "D3D 3D", "Core");
        Bind("gpuPower", all, IsGpu, SensorType.Power, "GPU Package", "GPU Power", "Package", "GPU PPT");
        Bind("gpuClock", all, IsGpu, SensorType.Clock, "GPU Core", "Core");

        Bind("fanRpm", all, IsBoard, SensorType.Fan, "CPU Fan", "CPU", "Fan #1", "Fan");
        Bind("pumpRpm", all, IsBoard, SensorType.Fan, "Pump", "AIO", "Water");
        // LHM exposes two Memory devices both carrying a sensor called "Memory":
        // "Total Memory" (physical RAM) and "Virtual Memory" (commit charge). The panel
        // wants physical, so match the qualified name first.
        Bind("memLoad", all, IsMem, SensorType.Load, "Total Memory / Memory", "Memory");
    }

    private void Bind(string field, List<ISensor> all, Func<ISensor, bool> scope, SensorType type, params string[] preferences)
    {
        // An explicit override in the config beats everything, matched on "Hardware / Sensor" or just "Sensor".
        if (_config.Sensors.TryGetValue(field, out string? pin) && !string.IsNullOrWhiteSpace(pin))
        {
            ISensor? exact = all.FirstOrDefault(s =>
                $"{s.Hardware.Name} / {s.Name}".Equals(pin, StringComparison.OrdinalIgnoreCase) ||
                s.Name.Equals(pin, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) { _bound[field] = exact; return; }
            Console.Error.WriteLine($"warning: config pins {field} to \"{pin}\", which no sensor matches — falling back to auto");
        }

        List<ISensor> candidates = all.Where(s => s.SensorType == type && scope(s)).ToList();
        if (candidates.Count == 0) return;

        // A preference may name the sensor ("GPU Core") or qualify it with the hardware
        // ("Total Memory / Memory") when the sensor name alone is ambiguous.
        foreach (string want in preferences)
        {
            ISensor? hit = candidates.FirstOrDefault(s =>
                s.Name.Contains(want, StringComparison.OrdinalIgnoreCase) ||
                $"{s.Hardware.Name} / {s.Name}".Contains(want, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) { _bound[field] = hit; return; }
        }

        // Some fields have no safe fallback: an arbitrary pick would show a case fan as pump
        // speed, or a +12V rail as CPU voltage. Better to report nothing than something wrong.
        if (field is "pumpRpm" or "cpuVid") return;
        _bound[field] = candidates[0];
    }

    public IEnumerable<ISensor> AllSensors()
    {
        foreach (IHardware hw in _computer.Hardware)
        {
            foreach (ISensor s in hw.Sensors) yield return s;
            foreach (IHardware sub in hw.SubHardware)
                foreach (ISensor s in sub.Sensors) yield return s;
        }
    }

    public void Dispose() => _computer.Close();

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (IHardware sub in hardware.SubHardware) sub.Accept(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}
