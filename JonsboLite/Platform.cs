using System.Runtime.InteropServices;

namespace JonsboLite;

/// <summary>
/// The one place that knows which OS this build is running on. Everything else works
/// against <see cref="IPanelTransport"/> and <see cref="ISensorSource"/>.
///
/// Adding Linux means two new classes — a /dev/hidraw transport and a hwmon/procfs sensor
/// source — plus a branch here. The protocol, config and send loop stay exactly as they are.
/// </summary>
public static class Platform
{
    public static IPanelTransport CreateTransport()
    {
        if (OperatingSystem.IsWindows()) return new WindowsHidPanel();
        throw new PlatformNotSupportedException(Unsupported("panel transport"));
    }

    public static ISensorSource CreateSensorSource(Config config)
    {
        if (OperatingSystem.IsWindows()) return new LibreHardwareSensors(config);
        throw new PlatformNotSupportedException(Unsupported("sensor source"));
    }

    /// <summary>Short model label for the tray, without the device path.</summary>
    public static string ModelOf(IPanelTransport panel) =>
        panel is WindowsHidPanel w ? w.Model : panel.Description ?? "";

    private static string Unsupported(string what) =>
        $"No {what} implemented for {RuntimeInformation.OSDescription}. " +
        "The panel protocol itself is OS-agnostic (JonsboLite.Core targets plain net8.0); " +
        "only discovery and sensor reading need a per-OS implementation.";
}
