using System.Runtime.InteropServices;

namespace JonsboLite;

/// <summary>
/// The one place that knows which OS this build targets. Everything else works against
/// <see cref="IPanelTransport"/> and <see cref="ISensorSource"/>.
///
/// The split is compile-time, not runtime: the net8.0-windows build contains no Linux code
/// and the net8.0 build contains no WinForms or LibreHardwareMonitor. Both are produced
/// from the same source tree, so a change that breaks one is caught by building the other.
/// </summary>
public static class Platform
{
    public static IPanelTransport CreateTransport()
    {
#if WINDOWS
        return new WindowsHidPanel();
#else
        if (OperatingSystem.IsLinux()) return new LinuxHidRawPanel();
        throw new PlatformNotSupportedException(Unsupported("panel transport"));
#endif
    }

    public static ISensorSource CreateSensorSource(Config config)
    {
#if WINDOWS
        return new LibreHardwareSensors(config);
#else
        if (OperatingSystem.IsLinux()) return new LinuxSysfsSensors(config);
        throw new PlatformNotSupportedException(Unsupported("sensor source"));
#endif
    }

    /// <summary>True when this build can show a notification-area icon.</summary>
    public static bool SupportsTray =>
#if WINDOWS
        true;
#else
        false;
#endif

    private static string Unsupported(string what) =>
        $"No {what} implemented for {RuntimeInformation.OSDescription}. " +
        "The panel protocol itself is OS-agnostic (JonsboLite.Core targets plain net8.0); " +
        "only discovery and sensor reading need a per-OS implementation.";
}
