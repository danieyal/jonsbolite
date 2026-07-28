using System.Text.Json;
using System.Text.Json.Serialization;

namespace JonsboLite;

/// <summary>
/// Optional jonsbolite.json. Everything has a working default; the file only exists so
/// sensor choices and unit survive restarts.
///
/// Two locations, in order: next to the executable, then the per-user config directory.
/// A file beside the executable wins, which keeps a portable copy self-contained — unzip
/// it anywhere and its settings travel with it. An installed copy lives under Program
/// Files or /usr/bin, which the user cannot write, so it falls through to the user path.
/// </summary>
public sealed class Config
{
    /// <summary>"C" or "F".</summary>
    public string TempUnit { get; set; } = "C";

    /// <summary>0 or 1. Ocypus panels swap CPU temp/power, Ruituo panels swap CPU load/power.</summary>
    public int DisplayMode { get; set; }

    /// <summary>Bounds for <see cref="IntervalMs"/>, shared with the CLI so both agree.</summary>
    public const int MinIntervalMs = 20;
    public const int MaxIntervalMs = 5000;

    /// <summary>Frame cadence. The official app uses 200 ms.</summary>
    public int IntervalMs { get; set; } = 200;

    /// <summary>
    /// Pin a frame field to an exact sensor, as "Hardware / Sensor" or just "Sensor".
    /// Run with --list-sensors to see the available names.
    /// </summary>
    public Dictionary<string, string> Sensors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public TempUnit Unit => TempUnit?.Trim().StartsWith("F", StringComparison.OrdinalIgnoreCase) == true
        ? JonsboLite.TempUnit.Fahrenheit
        : JonsboLite.TempUnit.Celsius;

    /// <summary>Where this config came from, so tray edits save back to the same file.</summary>
    [JsonIgnore]
    public string? SourcePath { get; set; }

    // Source-generated metadata rather than reflection, so the core stays trim- and
    // AOT-safe. That matters for the small self-contained daemon a Linux port would want.
    private static readonly ConfigJsonContext Json = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    });

    private const string FileName = "jonsbolite.json";

    /// <summary>Beside the executable. Used when present, so portable copies stay portable.</summary>
    public static string PortablePath => Path.Combine(AppContext.BaseDirectory, FileName);

    /// <summary>%APPDATA%\JonsboLite on Windows, $XDG_CONFIG_HOME/jonsbolite on Linux.</summary>
    public static string UserPath
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                    "JonsboLite", FileName);

            string baseDir = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
                ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(baseDir, "jonsbolite", FileName);
        }
    }

    /// <summary>Where an unqualified load/save goes: portable file if it exists, else per-user.</summary>
    public static string DefaultPath => File.Exists(PortablePath) ? PortablePath : UserPath;

    public static Config Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return new Config { SourcePath = path };
        try
        {
            Config c = JsonSerializer.Deserialize(File.ReadAllText(path), Json.Config) ?? new Config();
            c.Normalize();
            c.SourcePath = path;
            return c;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"warning: could not read {path} ({ex.Message}) — using defaults");
            return new Config { SourcePath = path };
        }
    }

    /// <summary>
    /// Brings a deserialized instance back into a range the rest of the app can rely on.
    /// The file is hand-edited, and JSON that parses fine can still be nonsense: a null
    /// <c>tempUnit</c> or <c>sensors</c> would throw at the point of use, and an
    /// <c>intervalMs</c> of 0 would turn the send loop into a spin. The CLI clamps the
    /// same values, but only for options actually passed on the command line.
    /// </summary>
    private void Normalize()
    {
        TempUnit = Unit == JonsboLite.TempUnit.Fahrenheit ? "F" : "C";
        DisplayMode = Math.Clamp(DisplayMode, 0, 1);
        IntervalMs = Math.Clamp(IntervalMs, MinIntervalMs, MaxIntervalMs);

        // Rebuilt rather than null-checked: System.Text.Json hands back a fresh dictionary
        // with the default comparer, discarding the case-insensitive one declared above.
        // Without this, a config written with "PumpRpm" would never match the "pumpRpm"
        // key the binder looks up, and would fail silently.
        Sensors = Sensors is { Count: > 0 }
            ? new Dictionary<string, string>(Sensors, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public void Save(string? path = null)
    {
        path ??= SourcePath ?? DefaultPath;
        try
        {
            if (Path.GetDirectoryName(path) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Json.Config));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"warning: could not write {path} ({ex.Message})");
        }
    }
}

[JsonSerializable(typeof(Config))]
internal partial class ConfigJsonContext : JsonSerializerContext;
