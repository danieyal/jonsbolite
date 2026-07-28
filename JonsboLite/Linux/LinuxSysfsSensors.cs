using System.Globalization;
using System.Runtime.Versioning;

namespace JonsboLite;

/// <summary>
/// Linux sensor backend, reading the kernel's own interfaces — no library, no driver to
/// install, and no elevation: hwmon, procfs and sysfs are world-readable.
///
/// Sources are discovered once at startup and bound to panel fields by name preference,
/// same approach as the Windows backend. Linux hwmon labelling is far less consistent than
/// LibreHardwareMonitor's, so binding is deliberately conservative: fields that could be
/// filled by an unrelated sensor (a drive's temperature standing in for the CPU's) are
/// left unbound rather than guessed. Run --list-sensors and pin them in jonsbolite.json.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxSysfsSensors : ISensorSource
{
    private sealed record Source(string Hardware, string Name, string Kind, Func<double?> Read);

    private const string Temperature = "Temperature";
    private const string Fan = "Fan";
    private const string Voltage = "Voltage";
    private const string Power = "Power";
    private const string Clock = "Clock";
    private const string Load = "Load";

    private readonly Config _config;
    private readonly List<Source> _sources = new();
    private readonly Dictionary<PanelField, Source> _bound = new();

    public LinuxSysfsSensors(Config config)
    {
        _config = config;
        DiscoverHwmon();
        DiscoverDrm();
        DiscoverPowerCap();
        DiscoverCpu();
        DiscoverMemory();
        Bind();
    }

    public Readings Read()
    {
        var r = new Readings();
        foreach (PanelField field in PanelFields.All)
            r[field] = _bound.TryGetValue(field, out Source? s) ? s.Read() ?? 0 : 0;
        return r;
    }

    public string? BoundSensorName(PanelField field) =>
        _bound.TryGetValue(field, out Source? s) ? $"{s.Hardware} / {s.Name}" : null;

    public IEnumerable<SensorReading> Enumerate() =>
        _sources.Select(s => new SensorReading(s.Hardware, s.Name, s.Kind, s.Read()));

    // ---------------------------------------------------------------- discovery

    /// <summary>
    /// /sys/class/hwmon/hwmonN — the bulk of it. Every chip driver lands here: k10temp and
    /// coretemp for CPU package temperature, nct6683/nct6775/it87 for board voltages and
    /// fan tachometers, amdgpu and nouveau for the GPU, nvme and drivetemp for storage.
    /// </summary>
    private void DiscoverHwmon()
    {
        foreach (string dir in Dirs("/sys/class/hwmon"))
        {
            string chip = Text(Path.Combine(dir, "name")) ?? Path.GetFileName(dir);
            string hardware = $"{chip} ({Path.GetFileName(dir)})";

            foreach (string file in Files(dir))
            {
                string f = Path.GetFileName(file);

                // Most readings are "<key>_input"; amdgpu reports power as "power1_average".
                string? key = f.EndsWith("_input", StringComparison.Ordinal) ? f[..^6]
                            : f.EndsWith("_average", StringComparison.Ordinal) ? f[..^8]
                            : null;
                if (key is null) continue;

                (string kind, double scale) = Classify(key);
                if (kind.Length == 0) continue;

                // Prefer the chip's own label ("Tctl", "Vcore", "CPU Fan") and fall back to
                // the raw key, which stays pinnable either way.
                string label = Text(Path.Combine(dir, key + "_label")) ?? key;
                string path = file;
                _sources.Add(new Source(hardware, label, kind, () => Number(path) * scale));
            }
        }
    }

    /// <summary>hwmon units are fixed by the kernel ABI, so the scale is knowable from the key.</summary>
    private static (string Kind, double Scale) Classify(string key) =>
        key.StartsWith("temp", StringComparison.Ordinal) ? (Temperature, 1e-3) :   // millidegree C
        key.StartsWith("fan", StringComparison.Ordinal) ? (Fan, 1.0) :             // RPM
        key.StartsWith("in", StringComparison.Ordinal) ? (Voltage, 1e-3) :         // millivolt
        key.StartsWith("power", StringComparison.Ordinal) ? (Power, 1e-6) :        // microwatt
        key.StartsWith("freq", StringComparison.Ordinal) ? (Clock, 1e-6) :         // Hz -> MHz
        ("", 1.0);

    /// <summary>GPU utilisation. amdgpu exposes it directly; nouveau and nvidia do not.</summary>
    private void DiscoverDrm()
    {
        foreach (string card in Dirs("/sys/class/drm").Where(d => Path.GetFileName(d).StartsWith("card", StringComparison.Ordinal)))
        {
            string busy = Path.Combine(card, "device", "gpu_busy_percent");
            if (!File.Exists(busy)) continue;

            string driver = Uevent(Path.Combine(card, "device", "uevent"), "DRIVER") ?? "gpu";
            _sources.Add(new Source($"{driver} ({Path.GetFileName(card)})", "GPU Busy", Load, () => Number(busy)));
        }
    }

    /// <summary>
    /// RAPL, via the powercap class. Reports cumulative energy, so watts come from a delta.
    /// Intel exposes this reliably; AMD coverage depends on kernel version and the
    /// amd_energy/rapl drivers, so this is the field most likely to be missing.
    /// </summary>
    private void DiscoverPowerCap()
    {
        foreach (string zone in Dirs("/sys/class/powercap"))
        {
            string name = Text(Path.Combine(zone, "name")) ?? "";
            if (!name.StartsWith("package", StringComparison.OrdinalIgnoreCase)) continue;

            string energy = Path.Combine(zone, "energy_uj");
            if (!File.Exists(energy)) continue;

            double range = Number(Path.Combine(zone, "max_energy_range_uj")) ?? 0;
            var meter = new EnergyMeter(energy, range);
            _sources.Add(new Source($"RAPL ({Path.GetFileName(zone)})", name, Power, meter.Watts));
        }
    }

    private void DiscoverCpu()
    {
        _sources.Add(new Source("CPU", "CPU Total", Load, new ProcStatLoad().Percent));

        string[] freqs = Dirs("/sys/devices/system/cpu")
            .Where(d => Path.GetFileName(d).StartsWith("cpu", StringComparison.Ordinal))
            .Select(d => Path.Combine(d, "cpufreq", "scaling_cur_freq"))
            .Where(File.Exists)
            .ToArray();

        if (freqs.Length == 0) return;

        // scaling_cur_freq is kHz. Average across online cores is steadier than any single
        // core; max is offered too, since it is what shows boost behaviour.
        _sources.Add(new Source("CPU", "CPU Clock", Clock, () => Aggregate(freqs, static v => v.Average()) / 1000.0));
        _sources.Add(new Source("CPU", "CPU Clock Max", Clock, () => Aggregate(freqs, static v => v.Max()) / 1000.0));
    }

    private static double? Aggregate(string[] paths, Func<IEnumerable<double>, double> reduce)
    {
        List<double> values = paths.Select(Number).OfType<double>().ToList();
        return values.Count == 0 ? null : reduce(values);
    }

    private void DiscoverMemory()
    {
        _sources.Add(new Source("Memory", "Memory", Load, static () =>
        {
            double total = 0, available = 0;
            try
            {
                foreach (string line in File.ReadLines("/proc/meminfo"))
                {
                    if (line.StartsWith("MemTotal:", StringComparison.Ordinal)) total = MemValue(line);
                    else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal)) { available = MemValue(line); break; }
                }
            }
            catch { return null; }
            return total > 0 ? (total - available) / total * 100.0 : null;
        }));
    }

    private static double MemValue(string line)
    {
        // "MemTotal:       32797380 kB"
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
    }

    // ---------------------------------------------------------------- binding

    private void Bind()
    {
        // Ordered preferences, matched against "Hardware / Name" or just "Name". Naming the
        // chip driver ("k10temp") scopes a field to that device without needing the label.
        Bind(PanelField.CpuTemp, Temperature, "Tctl", "Tdie", "Package id 0", "k10temp", "zenpower", "coretemp");
        Bind(PanelField.CpuLoad, Load, fallback: true, "CPU Total");
        Bind(PanelField.CpuPower, Power, "package-0", "RAPL");
        Bind(PanelField.CpuClock, Clock, "CPU / CPU Clock");
        // Vcore, from the SuperIO chip. "in0" is the conventional slot but not guaranteed,
        // so unlabelled boards leave this unbound rather than reporting some other rail.
        Bind(PanelField.CpuVid, Voltage, "Vcore", "CPU Vcore", "in0");

        Bind(PanelField.GpuTemp, Temperature, "junction", "edge", "amdgpu", "nouveau");
        Bind(PanelField.GpuLoad, Load, "GPU Busy");
        Bind(PanelField.GpuPower, Power, "amdgpu", "PPT", "slowPPT");
        Bind(PanelField.GpuClock, Clock, "amdgpu", "sclk");

        Bind(PanelField.FanRpm, Fan, "CPU Fan", "CPU_FAN", "fan1");
        Bind(PanelField.PumpRpm, Fan, "Pump", "PUMP", "AIO", "Water");
        Bind(PanelField.MemLoad, Load, fallback: false, "Memory / Memory");
    }

    private void Bind(PanelField field, string kind, params string[] preferences) =>
        Bind(field, kind, fallback: false, preferences);

    private void Bind(PanelField field, string kind, bool fallback, params string[] preferences)
    {
        // An explicit pin in the config beats everything.
        if (_config.Sensors.TryGetValue(field.ConfigKey(), out string? pin) && !string.IsNullOrWhiteSpace(pin))
        {
            Source? exact = _sources.FirstOrDefault(s =>
                $"{s.Hardware} / {s.Name}".Equals(pin, StringComparison.OrdinalIgnoreCase) ||
                s.Name.Equals(pin, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) { _bound[field] = exact; return; }
            Console.Error.WriteLine($"warning: config pins {field.ConfigKey()} to \"{pin}\", which no sensor matches — falling back to auto");
        }

        List<Source> candidates = _sources.Where(s => s.Kind == kind).ToList();
        if (candidates.Count == 0) return;

        foreach (string want in preferences)
        {
            Source? hit = candidates.FirstOrDefault(s =>
                s.Name.Contains(want, StringComparison.OrdinalIgnoreCase) ||
                $"{s.Hardware} / {s.Name}".Contains(want, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) { _bound[field] = hit; return; }
        }

        if (fallback) _bound[field] = candidates[0];
    }

    // ---------------------------------------------------------------- sysfs helpers
    // Every read is best-effort: sysfs nodes disappear when hardware is removed, and some
    // return EIO when a rail is unavailable. A missing value must never take the loop down.

    private static IEnumerable<string> Dirs(string path)
    {
        try { return Directory.EnumerateDirectories(path).OrderBy(p => p, StringComparer.Ordinal).ToList(); }
        catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> Files(string path)
    {
        try { return Directory.EnumerateFiles(path).OrderBy(p => p, StringComparer.Ordinal).ToList(); }
        catch { return Array.Empty<string>(); }
    }

    private static string? Text(string path)
    {
        try { return File.ReadAllText(path).Trim() is { Length: > 0 } s ? s : null; }
        catch { return null; }
    }

    private static double? Number(string path) =>
        double.TryParse(Text(path), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;

    private static string? Uevent(string path, string key)
    {
        try
        {
            foreach (string line in File.ReadLines(path))
                if (line.StartsWith(key + "=", StringComparison.Ordinal))
                    return line[(key.Length + 1)..];
        }
        catch { /* not present */ }
        return null;
    }

    public void Dispose() { }

    /// <summary>
    /// Cumulative microjoules to average watts over the interval between calls.
    /// Primes its baseline at construction so the first Read of the app's life reports a
    /// real figure rather than zero.
    /// </summary>
    private sealed class EnergyMeter
    {
        private readonly string _path;
        private readonly double _rangeUj;
        private double _lastEnergy = double.NaN;
        private long _lastTicks;
        private double _watts;

        public EnergyMeter(string path, double rangeUj)
        {
            _path = path;
            _rangeUj = rangeUj;
            Watts();   // establish the baseline; discovery gives it time before the first real read
        }

        public double? Watts()
        {
            double? now = Number(_path);
            if (now is null) return null;
            long ticks = Environment.TickCount64;

            if (!double.IsNaN(_lastEnergy))
            {
                double dEnergy = now.Value - _lastEnergy;
                // The counter wraps at max_energy_range_uj rather than saturating.
                if (dEnergy < 0) dEnergy += _rangeUj > 0 ? _rangeUj : 0;
                double seconds = (ticks - _lastTicks) / 1000.0;
                if (seconds > 0 && dEnergy >= 0) _watts = dEnergy / 1e6 / seconds;
            }

            _lastEnergy = now.Value;
            _lastTicks = ticks;
            return _watts;
        }
    }

    /// <summary>
    /// CPU utilisation from /proc/stat, as a percentage of the interval between calls.
    /// Primes its baseline at construction, so the first Read reports a real figure.
    /// </summary>
    private sealed class ProcStatLoad
    {
        private double _lastIdle, _lastTotal;
        private double _percent;

        public ProcStatLoad() => Percent();

        public double? Percent()
        {
            string? line;
            try { line = File.ReadLines("/proc/stat").FirstOrDefault(l => l.StartsWith("cpu ", StringComparison.Ordinal)); }
            catch { return null; }
            if (line is null) return null;

            // cpu  user nice system idle iowait irq softirq steal guest guest_nice
            double[] f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                             .Skip(1)
                             .Select(v => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0)
                             .ToArray();
            if (f.Length < 5) return null;

            double idle = f[3] + f[4];              // idle + iowait
            double total = f.Sum();

            double dTotal = total - _lastTotal;
            double dIdle = idle - _lastIdle;
            if (_lastTotal > 0 && dTotal > 0) _percent = (1.0 - dIdle / dTotal) * 100.0;

            _lastIdle = idle;
            _lastTotal = total;
            return _percent;
        }
    }
}
