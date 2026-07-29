using System.Text.Json;
using JonsboLite;

namespace JonsboLite.Core.Tests;

/// <summary>
/// jonsbolite.json is hand-edited, so "it parsed" says nothing about whether the values
/// are usable. Each case here corresponds to a way the file has been able to take the app
/// down or silently misbehave.
/// </summary>
public class ConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "jonsbolite-tests-" + Guid.NewGuid().ToString("N"));

    private string WriteConfig(string json)
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "jonsbolite.json");
        File.WriteAllText(path, json);
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Defaults_are_sane_without_a_file()
    {
        var c = Config.Load(Path.Combine(_dir, "does-not-exist.json"));
        Assert.Equal(TempUnit.Celsius, c.Unit);
        Assert.Equal(0, c.DisplayMode);
        Assert.Equal(200, c.IntervalMs);
        Assert.Empty(c.Sensors);
    }

    [Fact]
    public void Null_temp_unit_does_not_throw_when_read()
    {
        // Config.Unit used to call Trim() on this directly, so a null threw at the point
        // of use rather than at load.
        var c = Config.Load(WriteConfig("""{ "tempUnit": null }"""));
        Assert.Equal(TempUnit.Celsius, c.Unit);
        Assert.Equal("C", c.TempUnit);
    }

    [Fact]
    public void Null_sensor_map_becomes_an_empty_one()
    {
        var c = Config.Load(WriteConfig("""{ "sensors": null }"""));
        Assert.NotNull(c.Sensors);
        Assert.Empty(c.Sensors);
    }

    [Theory]
    [InlineData(0, Config.MinIntervalMs)]        // would have spun the send loop
    [InlineData(-100, Config.MinIntervalMs)]
    [InlineData(1_000_000, Config.MaxIntervalMs)]
    [InlineData(200, 200)]
    public void Interval_is_clamped_on_load(int written, int expected)
    {
        var c = Config.Load(WriteConfig($$"""{ "intervalMs": {{written}} }"""));
        Assert.Equal(expected, c.IntervalMs);
    }

    [Theory]
    [InlineData(99, 1)]
    [InlineData(-5, 0)]
    [InlineData(1, 1)]
    public void Display_mode_is_clamped_on_load(int written, int expected)
    {
        var c = Config.Load(WriteConfig($$"""{ "displayMode": {{written}} }"""));
        Assert.Equal(expected, c.DisplayMode);
    }

    /// <summary>
    /// System.Text.Json builds a fresh dictionary with the default comparer, discarding
    /// the case-insensitive one the property declares. Without rebuilding it, a config
    /// written with "PumpRpm" never matched the "pumpRpm" key the binder looks up — and
    /// failed silently, which is the worst way to fail.
    /// </summary>
    [Theory]
    [InlineData("pumpRpm")]
    [InlineData("PumpRpm")]
    [InlineData("PUMPRPM")]
    public void Sensor_pins_are_matched_regardless_of_key_case(string key)
    {
        var c = Config.Load(WriteConfig($$"""{ "sensors": { "{{key}}": "Some / Sensor" } }"""));
        Assert.True(c.Sensors.TryGetValue(PanelField.PumpRpm.ConfigKey(), out string? pin));
        Assert.Equal("Some / Sensor", pin);
    }

    [Theory]
    [InlineData("F", TempUnit.Fahrenheit)]
    [InlineData("f", TempUnit.Fahrenheit)]
    [InlineData("fahrenheit", TempUnit.Fahrenheit)]
    [InlineData("C", TempUnit.Celsius)]
    [InlineData("celsius", TempUnit.Celsius)]
    [InlineData("nonsense", TempUnit.Celsius)]
    [InlineData("  F  ", TempUnit.Fahrenheit)]
    public void Temp_unit_parsing_is_forgiving(string written, TempUnit expected)
    {
        var c = Config.Load(WriteConfig($$"""{ "tempUnit": {{JsonSerializer.Serialize(written)}} }"""));
        Assert.Equal(expected, c.Unit);
    }

    [Fact]
    public void Unreadable_json_falls_back_to_defaults_rather_than_throwing()
    {
        var c = Config.Load(WriteConfig("{ this is not json"));
        Assert.Equal(200, c.IntervalMs);
        Assert.Equal(TempUnit.Celsius, c.Unit);
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        string path = Path.Combine(_dir, "round-trip.json");
        Directory.CreateDirectory(_dir);

        var saved = new Config { TempUnit = "F", DisplayMode = 1, IntervalMs = 500 };
        saved.Sensors["pumpRpm"] = "Nuvoton / Pump Fan #1";
        saved.Save(path);

        var loaded = Config.Load(path);
        Assert.Equal(TempUnit.Fahrenheit, loaded.Unit);
        Assert.Equal(1, loaded.DisplayMode);
        Assert.Equal(500, loaded.IntervalMs);
        Assert.Equal("Nuvoton / Pump Fan #1", loaded.Sensors["pumpRpm"]);
        Assert.Equal(path, loaded.SourcePath);
    }

    [Fact]
    public void Save_creates_missing_directories()
    {
        // The per-user config directory does not exist until first write.
        string path = Path.Combine(_dir, "nested", "deeper", "jonsbolite.json");
        new Config().Save(path);
        Assert.True(File.Exists(path));
    }
}
