using JonsboLite;

namespace JonsboLite.Core.Tests;

public class PanelFieldTests
{
    /// <summary>
    /// ConfigKey is the contract with jonsbolite.json — renaming an enum member silently
    /// invalidates every config file in the wild, so the exact strings are pinned here.
    /// </summary>
    [Theory]
    [InlineData(PanelField.CpuTemp, "cpuTemp")]
    [InlineData(PanelField.CpuLoad, "cpuLoad")]
    [InlineData(PanelField.CpuPower, "cpuPower")]
    [InlineData(PanelField.CpuClock, "cpuClock")]
    [InlineData(PanelField.CpuVid, "cpuVid")]
    [InlineData(PanelField.GpuTemp, "gpuTemp")]
    [InlineData(PanelField.GpuLoad, "gpuLoad")]
    [InlineData(PanelField.GpuPower, "gpuPower")]
    [InlineData(PanelField.GpuClock, "gpuClock")]
    [InlineData(PanelField.FanRpm, "fanRpm")]
    [InlineData(PanelField.PumpRpm, "pumpRpm")]
    [InlineData(PanelField.MemLoad, "memLoad")]
    public void ConfigKey_is_stable_camelCase(PanelField field, string expected)
        => Assert.Equal(expected, field.ConfigKey());

    [Fact]
    public void All_covers_every_member_exactly_once()
    {
        Assert.Equal(12, PanelFields.All.Length);
        Assert.Equal(PanelFields.All.Length, PanelFields.All.Distinct().Count());
    }

    [Theory]
    [InlineData("pumpRpm")]
    [InlineData("PumpRpm")]
    [InlineData("PUMPRPM")]
    public void TryParse_accepts_any_casing(string key)
    {
        Assert.True(PanelFields.TryParse(key, out PanelField f));
        Assert.Equal(PanelField.PumpRpm, f);
    }

    [Fact]
    public void TryParse_rejects_an_unknown_key()
        => Assert.False(PanelFields.TryParse("caseFanRpm", out _));

    [Fact]
    public void Readings_indexer_and_named_properties_address_the_same_slot()
    {
        var r = new Readings { CpuTemp = 51.5 };
        Assert.Equal(51.5, r[PanelField.CpuTemp]);

        r[PanelField.PumpRpm] = 2376;
        Assert.Equal(2376, r.PumpRpm);
    }

    [Fact]
    public void Readings_start_at_zero_for_every_field()
    {
        var r = new Readings();
        foreach (PanelField f in PanelFields.All) Assert.Equal(0, r[f]);
    }
}
