using JonsboLite;

namespace JonsboLite.Core.Tests;

/// <summary>
/// The frame layout is the one thing in this project that was established empirically,
/// by watching a physical panel. These tests pin it down so a refactor cannot quietly
/// move a field: every offset here was confirmed against real hardware.
/// </summary>
public class FrameTests
{
    private static readonly DateTime Moment = new(2026, 7, 29, 14, 5, 9);   // a Wednesday

    private static byte[] Pack(Readings r, TempUnit unit = TempUnit.Celsius, int displayMode = 0)
    {
        var p = new byte[Frame.PayloadLength];
        Frame.Pack(p, r, unit, displayMode, Moment);
        return p;
    }

    [Fact]
    public void Payload_is_exactly_64_bytes()
    {
        Assert.Equal(64, Frame.PayloadLength);
        Assert.Equal(64, Pack(new Readings()).Length);
    }

    [Fact]
    public void Pack_rejects_a_wrongly_sized_buffer()
    {
        Assert.Throws<ArgumentException>(() =>
            Frame.Pack(new byte[63], new Readings(), TempUnit.Celsius, 0, Moment));
    }

    [Fact]
    public void Header_is_the_constant_the_vendor_app_sends()
    {
        byte[] p = Pack(new Readings());
        Assert.Equal(0x00, p[0]);
        Assert.Equal(0x01, p[1]);
        Assert.Equal(0x02, p[2]);
    }

    [Fact]
    public void Cpu_fields_land_on_their_documented_offsets()
    {
        byte[] p = Pack(new Readings
        {
            CpuTemp = 88.50, CpuLoad = 42, CpuPower = 65, CpuClock = 4550, CpuVid = 1.25,
        });

        Assert.Equal(88, p[3]);     // temp whole
        Assert.Equal(50, p[4]);     // temp hundredths
        Assert.Equal(0, p[5]);      // unit: Celsius
        Assert.Equal(42, p[6]);     // load %
        Assert.Equal(65, p[7]);     // watts % 100
        Assert.Equal(0, p[34]);     // watts / 100
        Assert.Equal(45, p[9]);     // MHz / 100
        Assert.Equal(50, p[10]);    // MHz % 100
        Assert.Equal(1, p[11]);     // VID whole
        Assert.Equal(25, p[12]);    // VID hundredths
    }

    [Fact]
    public void Gpu_and_cooling_fields_land_on_their_documented_offsets()
    {
        byte[] p = Pack(new Readings
        {
            GpuTemp = 77, GpuLoad = 55, GpuPower = 120, GpuClock = 2500,
            FanRpm = 1200, PumpRpm = 850, MemLoad = 61,
        });

        Assert.Equal(77, p[13]);
        Assert.Equal(0, p[15]);     // unit
        Assert.Equal(55, p[16]);
        Assert.Equal(20, p[17]);    // 120 W -> 1 * 100 + 20
        Assert.Equal(1, p[35]);
        Assert.Equal(25, p[19]);    // 2500 MHz
        Assert.Equal(0, p[20]);
        Assert.Equal(12, p[21]);    // 1200 rpm
        Assert.Equal(0, p[22]);
        Assert.Equal(8, p[23]);     // 850 rpm
        Assert.Equal(50, p[24]);
        Assert.Equal(61, p[33]);
    }

    [Fact]
    public void Clock_and_calendar_match_the_supplied_moment()
    {
        byte[] p = Pack(new Readings());

        Assert.Equal(2, p[25]);     // leading digit of 2026
        Assert.Equal(26, p[26]);
        Assert.Equal(7, p[27]);
        Assert.Equal(29, p[28]);
        Assert.Equal(14, p[29]);
        Assert.Equal(5, p[30]);
        Assert.Equal(9, p[31]);
        Assert.Equal(3, p[32]);     // Wednesday, 0 = Sunday
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Display_mode_is_written_to_both_vendor_specific_bytes(int mode)
    {
        byte[] p = Pack(new Readings(), displayMode: mode);
        Assert.Equal(mode, p[40]);
        Assert.Equal(mode, p[41]);
    }

    [Fact]
    public void Reserved_bytes_stay_zero()
    {
        byte[] p = Pack(new Readings
        {
            CpuTemp = 60, GpuTemp = 60, CpuPower = 99, GpuPower = 99,
            FanRpm = 1500, PumpRpm = 1500, MemLoad = 50, CpuClock = 3000, GpuClock = 2000,
        }, displayMode: 1);

        foreach (int i in new[] { 36, 37, 38, 39 })
            Assert.Equal(0, p[i]);
        for (int i = 42; i < Frame.PayloadLength; i++)
            Assert.Equal(0, p[i]);
    }

    [Fact]
    public void Fahrenheit_converts_the_value_and_sets_both_unit_bytes()
    {
        byte[] p = Pack(new Readings { CpuTemp = 100, GpuTemp = 0 }, TempUnit.Fahrenheit);

        Assert.Equal(1, p[5]);
        Assert.Equal(1, p[15]);
        Assert.Equal(212, p[3]);    // 100 C == 212 F
        Assert.Equal(32, p[13]);    // 0 C == 32 F
    }

    /// <summary>
    /// The vendor app clamps Celsius to 999 and then truncates to a byte, so a 130 °C
    /// reading in Fahrenheit mode (266 °F) is transmitted as 10 — an overheating CPU shown
    /// as a cold one. Capping at 255 makes it obviously wrong instead.
    /// </summary>
    [Theory]
    [InlineData(110, 230)]      // 230 F, still representable
    [InlineData(130, 255)]      // 266 F, would have wrapped to 10
    [InlineData(199, 255)]      // 390 F, would have wrapped to 134
    public void Temperature_saturates_instead_of_wrapping(double celsius, byte expected)
    {
        byte[] p = Pack(new Readings { CpuTemp = celsius }, TempUnit.Fahrenheit);
        Assert.Equal(expected, p[3]);
    }

    [Fact]
    public void Out_of_range_and_nonsense_inputs_are_clamped_not_wrapped()
    {
        byte[] p = Pack(new Readings
        {
            CpuTemp = double.NaN,       // a sensor that failed to read
            CpuLoad = 900,              // impossible percentage
            CpuClock = 999_999,         // far beyond the 9999 cap
            FanRpm = -50,               // negative
            CpuPower = 99_999,
        });

        Assert.Equal(0, p[3]);          // NaN floors to the minimum
        Assert.Equal(100, p[6]);        // percentages cap at 100
        Assert.Equal(99, p[9]);         // 9999 / 100
        Assert.Equal(99, p[10]);        // 9999 % 100
        Assert.Equal(0, p[21]);         // negative floors to zero
        Assert.Equal(0, p[22]);
        Assert.Equal(99, p[34]);        // power caps at 9999 too
    }

    /// <summary>Every hi/lo pair must stay inside a byte for any input the API allows.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(9999)]
    [InlineData(25_600)]
    [InlineData(1_000_000)]
    public void Split_pairs_never_exceed_a_byte(double value)
    {
        byte[] p = Pack(new Readings
        {
            CpuClock = value, GpuClock = value, FanRpm = value, PumpRpm = value,
            CpuPower = value, GpuPower = value,
        });

        foreach (int i in new[] { 9, 10, 19, 20, 21, 22, 23, 24, 34, 35, 7, 17 })
            Assert.InRange(p[i], 0, 99);
    }

    [Fact]
    public void Describe_reports_what_was_packed()
    {
        byte[] p = Pack(new Readings
        {
            CpuTemp = 51.25, CpuLoad = 7, CpuPower = 38.5, CpuClock = 4099,
            GpuTemp = 41, FanRpm = 1369, PumpRpm = 2376, MemLoad = 63,
        });

        string s = Frame.Describe(p);
        Assert.Contains("CPU 51.25C", s);
        Assert.Contains("4099MHz", s);
        Assert.Contains("FAN 1369rpm", s);
        Assert.Contains("PUMP 2376rpm", s);
        Assert.Contains("MEM 63%", s);
    }

    [Theory]
    [InlineData(0, TempUnit.Celsius, 0)]
    [InlineData(100, TempUnit.Celsius, 100)]
    [InlineData(0, TempUnit.Fahrenheit, 32)]
    [InlineData(100, TempUnit.Fahrenheit, 212)]
    [InlineData(-40, TempUnit.Fahrenheit, -40)]
    public void ConvertTemp_matches_the_standard_formula(double c, TempUnit unit, double expected)
        => Assert.Equal(expected, Frame.ConvertTemp(c, unit), 6);
}
