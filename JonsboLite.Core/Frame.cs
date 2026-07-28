namespace JonsboLite;

/// <summary>
/// The wire protocol, in full. See reverse-engineering/PROTOCOL.md.
///
/// Nothing here is bit-packed: every byte carries a decimal quantity, and wide values are
/// split across two or three bytes by /100 and %100. This type is deliberately free of any
/// OS dependency — it is the same 64 bytes on every platform.
/// </summary>
public static class Frame
{
    /// <summary>Payload size the panel expects. Transports add any report-ID framing themselves.</summary>
    public const int PayloadLength = 64;

    /// <summary>Header the official app sends in every frame.</summary>
    private static ReadOnlySpan<byte> Header => new byte[] { 0x00, 0x01, 0x02 };

    public static void Pack(Span<byte> p, Readings r, TempUnit unit, int displayMode, DateTime now)
    {
        if (p.Length != PayloadLength)
            throw new ArgumentException($"payload must be {PayloadLength} bytes", nameof(p));
        p.Clear();
        Header.CopyTo(p);

        byte unitByte = (byte)unit;

        // --- CPU ---
        (p[3], p[4]) = Fixed2(ConvertTemp(r.CpuTemp, unit), 999);
        p[5] = unitByte;
        p[6] = Percent(r.CpuLoad);
        (p[34], p[7], p[8]) = Fixed3(r.CpuPower);
        (p[9], p[10]) = Split100(r.CpuClock);
        (p[11], p[12]) = Fixed2(r.CpuVid, 9999);

        // --- GPU ---
        (p[13], p[14]) = Fixed2(ConvertTemp(r.GpuTemp, unit), 999);
        p[15] = unitByte;
        p[16] = Percent(r.GpuLoad);
        (p[35], p[17], p[18]) = Fixed3(r.GpuPower);
        (p[19], p[20]) = Split100(r.GpuClock);

        // --- cooling ---
        (p[21], p[22]) = Split100(r.FanRpm);
        (p[23], p[24]) = Split100(r.PumpRpm);

        // --- clock/calendar ---
        p[25] = (byte)(now.Year / 1000);      // leading digit, "2" of 2026
        p[26] = (byte)(now.Year % 100);
        p[27] = (byte)now.Month;
        p[28] = (byte)now.Day;
        p[29] = (byte)now.Hour;
        p[30] = (byte)now.Minute;
        p[31] = (byte)now.Second;
        p[32] = (byte)now.DayOfWeek;          // 0 = Sunday, same as JS getDay()

        p[33] = Percent(r.MemLoad);

        // 36..39 unused
        p[40] = (byte)displayMode;            // Ocypus: 0 = CPU temp, 1 = CPU power
        p[41] = (byte)displayMode;            // Ruituo: 0 = CPU load, 1 = CPU power
        // 42..63 unused
    }

    public static double ConvertTemp(double celsius, TempUnit unit) =>
        unit == TempUnit.Fahrenheit ? celsius * 9.0 / 5.0 + 32.0 : celsius;

    private static byte Percent(double v) => (byte)Clamp(v, 0, 100);

    /// <summary>Integer part + hundredths, e.g. 88.5 -> (88, 50).</summary>
    private static (byte, byte) Fixed2(double v, double max)
    {
        v = Clamp(v, 0, max);
        double whole = Math.Floor(v);
        return ((byte)whole, (byte)Clamp(Math.Floor((v - whole) * 100), 0, 99));
    }

    /// <summary>Hundreds + remainder + hundredths, e.g. 234.5 -> (2, 34, 50).</summary>
    private static (byte, byte, byte) Fixed3(double v)
    {
        v = Clamp(v, 0, 9999);
        int whole = (int)Math.Floor(v);
        return ((byte)(whole / 100), (byte)(whole % 100), (byte)Clamp(Math.Floor((v - whole) * 100), 0, 99));
    }

    /// <summary>High/low pair for values up to 9999, e.g. 4550 MHz -> (45, 50).</summary>
    private static (byte, byte) Split100(double v)
    {
        int whole = (int)Clamp(v, 0, 9999);
        return ((byte)(whole / 100), (byte)(whole % 100));
    }

    private static double Clamp(double v, double lo, double hi) =>
        double.IsNaN(v) || v < lo ? lo : v > hi ? hi : v;

    /// <summary>Decodes a payload back into readings — the inverse of Pack, for diagnostics.</summary>
    public static string Describe(ReadOnlySpan<byte> p)
    {
        string u = p[5] == 1 ? "F" : "C";
        return $"CPU {p[3]}.{p[4]:00}{u} {p[6]}% {p[34] * 100 + p[7]}.{p[8]:00}W {p[9] * 100 + p[10]}MHz {p[11]}.{p[12]:00}V | " +
               $"GPU {p[13]}.{p[14]:00}{u} {p[16]}% {p[35] * 100 + p[17]}.{p[18]:00}W {p[19] * 100 + p[20]}MHz | " +
               $"FAN {p[21] * 100 + p[22]}rpm PUMP {p[23] * 100 + p[24]}rpm | MEM {p[33]}%";
    }
}
