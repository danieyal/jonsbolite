using System.Text.Json;
using System.Text.Json.Serialization;

namespace JonsboLite;

/// <summary>
/// Optional jonsbolite.json sitting next to the executable. Everything has a working
/// default; the file only exists so sensor choices survive restarts.
/// </summary>
public sealed class Config
{
    /// <summary>"C" or "F".</summary>
    public string TempUnit { get; set; } = "C";

    /// <summary>0 or 1. Ocypus panels swap CPU temp/power, Ruituo panels swap CPU load/power.</summary>
    public int DisplayMode { get; set; }

    /// <summary>Frame cadence. The official app uses 200 ms.</summary>
    public int IntervalMs { get; set; } = 200;

    /// <summary>
    /// Pin a frame field to an exact sensor, as "Hardware / Sensor" or just "Sensor".
    /// Run with --list-sensors to see the available names.
    /// </summary>
    public Dictionary<string, string> Sensors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public TempUnit Unit => TempUnit.Trim().StartsWith("F", StringComparison.OrdinalIgnoreCase)
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

    public static string DefaultPath =>
        Path.Combine(AppContext.BaseDirectory, "jonsbolite.json");

    public static Config Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return new Config { SourcePath = path };
        try
        {
            Config c = JsonSerializer.Deserialize(File.ReadAllText(path), Json.Config) ?? new Config();
            c.SourcePath = path;
            return c;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"warning: could not read {path} ({ex.Message}) — using defaults");
            return new Config { SourcePath = path };
        }
    }

    public void Save(string? path = null)
    {
        path ??= SourcePath ?? DefaultPath;
        try
        {
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
