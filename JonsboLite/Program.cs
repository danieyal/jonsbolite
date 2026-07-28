using System.Diagnostics;
using System.Runtime.InteropServices;
#if WINDOWS
using System.Security.Principal;
#endif

namespace JonsboLite;

public static class Program
{
    private const string Usage = """
        JonsboLite — lightweight driver for JONSBO / Ocypus / Ruituo monitor panels
        (VID 0x5131, PID 0x2007 / 0x2009 / 0x200A)

          JonsboLite                     run the display loop
          JonsboLite --list-sensors      show every sensor name, for pinning in config
          JonsboLite --show-binding      show which sensor feeds each panel field
          JonsboLite --test              send a fixed test pattern instead of live data
          JonsboLite --write-config      write a jonsbolite.json with current settings

        Options
          --unit C|F           temperature unit                 (default from config, else C)
          --display-mode 0|1   panel-specific CPU field toggle  (default 0)
          --interval <ms>      frame cadence                    (default 200)
          --no-tray            no notification-area icon, Ctrl+C to stop  (Windows)
          --console            keep the console window visible in tray mode (Windows)
          --quiet              no per-second status line
          --config <path>      use a specific config file
        """;

    public static int Main(string[] args)
    {
        // Model names carry the vendor's Chinese labels; without this they arrive as "??"
        // whenever output is redirected to a file or a pipe.
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* not fatal */ }

        if (args.Contains("--help") || args.Contains("-h") || args.Contains("/?"))
        {
            Console.WriteLine(Usage);
            return 0;
        }

        string? configPath = ArgValue(args, "--config");
        Config config = Config.Load(configPath);

        if (ArgValue(args, "--unit") is { } unit) config.TempUnit = unit;
        if (ArgValue(args, "--display-mode") is { } dm && int.TryParse(dm, out int dmv)) config.DisplayMode = Math.Clamp(dmv, 0, 1);
        if (ArgValue(args, "--interval") is { } iv && int.TryParse(iv, out int ivv))
            config.IntervalMs = Math.Clamp(ivv, Config.MinIntervalMs, Config.MaxIntervalMs);

        if (args.Contains("--write-config"))
        {
            config.Save(configPath);
            Console.WriteLine($"wrote {configPath ?? Config.DefaultPath}");
            return 0;
        }

        if (args.Contains("--list-sensors")) return ListSensors(config);
        if (args.Contains("--show-binding")) return ShowBinding(config);

        return Run(config,
                   quiet: args.Contains("--quiet"),
                   test: args.Contains("--test"),
                   tray: !args.Contains("--no-tray"),
                   showConsole: args.Contains("--console"));
    }

    private static int Run(Config config, bool quiet, bool test, bool tray, bool showConsole)
    {
        WarnAboutPrivileges();

        var stop = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };
        // systemd stops services with SIGTERM, so honour it as well as Ctrl+C.
        using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
        {
            ctx.Cancel = true;
            stop.Set();
        });

        if (!tray || !Platform.SupportsTray)
        {
            SendLoop(config, quiet, test, stop, null);
            return 0;
        }

#if WINDOWS
        ApplicationConfiguration.Initialize();
        using var icon = new Tray(config);
        icon.ExitRequested += () => stop.Set();
        icon.SetConsoleVisible(showConsole);

        var worker = new Thread(() =>
        {
            try { SendLoop(config, quiet, test, stop, icon); }
            finally { icon.Post(Application.ExitThread); }
        })
        { IsBackground = true, Name = "JonsboLite send loop" };

        worker.Start();
        Application.Run();          // pumps the tray until ExitThread
        stop.Set();
        worker.Join(TimeSpan.FromSeconds(2));
#endif
        return 0;
    }

    private static void SendLoop(Config config, bool quiet, bool test, ManualResetEventSlim stop, IStatusSink? tray)
    {
        using IPanelTransport panel = Platform.CreateTransport();
        using ISensorSource? sensors = test ? null : OpenSensors(config);

        var payload = new byte[Frame.PayloadLength];
        var status = Stopwatch.StartNew();
        bool wasConnected = false;
        long frames = 0;

        Console.WriteLine(test
            ? "sending TEST PATTERN (not live sensor data)"
            : "sending live sensor data");

        while (!stop.IsSet)
        {
            if (!panel.IsOpen)
            {
                if (!panel.Open())
                {
                    if (wasConnected) { Console.WriteLine("panel disconnected — waiting"); wasConnected = false; }
                    tray?.UpdateStatus(test ? TestPattern() : new Readings(), connected: false, model: "");
                    stop.Wait(2000);
                    continue;
                }
                Console.WriteLine($"connected: {panel.Description}");
                wasConnected = true;
            }

            Readings r = test ? TestPattern() : sensors!.Read();
            Frame.Pack(payload, r, config.Unit, config.DisplayMode, DateTime.Now);

            if (!panel.Send(payload))
            {
                // Send() already closed the handle. Back off so a panel that opens but
                // rejects every write can't spin this loop at full tilt.
                Console.WriteLine("write failed — reopening");
                stop.Wait(500);
                continue;
            }
            frames++;

            // Once a second is plenty for the human-facing surfaces; the panel itself
            // keeps getting the full 5 Hz.
            if (status.ElapsedMilliseconds >= 1000)
            {
                status.Restart();
                tray?.UpdateStatus(r, connected: true, model: panel.Model);
                if (!quiet)
                {
                    // Overwrite in place on a real console; append lines when piped to a file.
                    if (Console.IsOutputRedirected) Console.WriteLine(Frame.Describe(payload));
                    else Console.Write("\r" + Frame.Describe(payload) + "  ");
                }
            }

            stop.Wait(config.IntervalMs);
        }

        Console.WriteLine();
        Console.WriteLine($"stopped after {frames} frames");
    }

    /// <summary>
    /// Opens the sensor backend, or explains why it could not and exits. Every path that
    /// needs sensors goes through here — the diagnostic commands most of all, since an
    /// unelevated run is exactly when someone reaches for --show-binding, and a raw stack
    /// trace is the least useful thing to hand them at that moment.
    /// </summary>
    private static ISensorSource OpenSensors(Config config)
    {
        try
        {
            return Platform.CreateSensorSource(config);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"could not start sensor monitoring: {ex.Message}");
            Console.Error.WriteLine("try running elevated, or use --test to verify the panel on its own");
            Environment.Exit(1);
            throw;   // unreachable; satisfies definite assignment
        }
    }

    private static Readings TestPattern() => new()
    {
        CpuTemp = 88.50, CpuLoad = 42, CpuPower = 65, CpuClock = 4550, CpuVid = 1.25,
        GpuTemp = 77, GpuLoad = 55, GpuPower = 120, GpuClock = 2500,
        FanRpm = 1200, PumpRpm = 850, MemLoad = 61,
    };

    /// <summary>
    /// Samples twice with a gap. Rate-derived sensors — /proc/stat utilisation, RAPL
    /// energy — are meaningless until there are two readings to subtract, so a single
    /// sample reports zero and makes a working sensor look broken.
    /// </summary>
    private static Readings SampleTwice(ISensorSource sensors)
    {
        sensors.Read();
        Thread.Sleep(250);
        return sensors.Read();
    }

    private static int ListSensors(Config config)
    {
        WarnAboutPrivileges();
        using ISensorSource sensors = OpenSensors(config);
        SampleTwice(sensors);

        foreach (IGrouping<string, SensorReading> group in sensors.Enumerate().GroupBy(s => s.Hardware))
        {
            Console.WriteLine();
            Console.WriteLine(group.Key);
            foreach (SensorReading s in group.OrderBy(s => s.Kind).ThenBy(s => s.Name))
                Console.WriteLine($"   {s.Kind,-12} {s.Name,-42} {s.Value,10:F2}");
        }
        Console.WriteLine();
        Console.WriteLine("Pin any of these in jonsbolite.json, e.g.  \"sensors\": { \"pumpRpm\": \"Fan #3\" }");
        return 0;
    }

    private static int ShowBinding(Config config)
    {
        WarnAboutPrivileges();
        using ISensorSource sensors = OpenSensors(config);
        Readings r = SampleTwice(sensors);

        Console.WriteLine($"{"panel field",-12} {"value",10}   sensor");
        foreach (PanelField f in PanelFields.All)
            Console.WriteLine($"{f.ConfigKey(),-12} {r[f],10:F2}   {sensors.BoundSensorName(f) ?? "(none)"}");
        return 0;
    }

    private static void WarnAboutPrivileges()
    {
#if WINDOWS
        using var identity = WindowsIdentity.GetCurrent();
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return;
        Console.Error.WriteLine("warning: not running as administrator — CPU package temp/power and");
        Console.Error.WriteLine("         motherboard fan tachometers will read 0. See install-autostart.ps1.");
        Console.Error.WriteLine();
#else
        // Linux needs no privilege: hwmon and procfs are world-readable, and hidraw access
        // comes from a udev rule rather than from running as root.
#endif
    }

    private static string? ArgValue(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
