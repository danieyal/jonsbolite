using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JonsboLite;

/// <summary>
/// Notification-area presence: the icon itself renders the current CPU temperature, the
/// tooltip carries a full reading, and the menu exposes the settings that matter at
/// runtime (unit, display mode, console visibility).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Tray : IStatusSink, IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Config _config;
    private readonly Control _marshal = new();     // handle owner, for BeginInvoke from the send loop
    private readonly ToolStripMenuItem _status;
    private readonly ToolStripMenuItem _celsius, _fahrenheit;
    private readonly ToolStripMenuItem _mode0, _mode1;
    private readonly ToolStripMenuItem _console;

    private IntPtr _iconHandle = IntPtr.Zero;
    private int _lastDrawnTemp = int.MinValue;
    private bool _lastDrawnConnected;
    private bool _consoleVisible = true;

    public event Action? ExitRequested;

    public Tray(Config config)
    {
        _config = config;
        _ = _marshal.Handle;   // force handle creation on the UI thread so Invoke works later

        _status = new ToolStripMenuItem("connecting…") { Enabled = false };

        _celsius = new ToolStripMenuItem("Celsius (°C)", null, (_, _) => SetUnit("C"));
        _fahrenheit = new ToolStripMenuItem("Fahrenheit (°F)", null, (_, _) => SetUnit("F"));

        _mode0 = new ToolStripMenuItem("Mode 0 — temperature / load", null, (_, _) => SetMode(0));
        _mode1 = new ToolStripMenuItem("Mode 1 — power draw", null, (_, _) => SetMode(1));

        _console = new ToolStripMenuItem("Show console window", null, (_, _) => ToggleConsole());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_status);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Temperature unit") { DropDownItems = { _celsius, _fahrenheit } });
        menu.Items.Add(new ToolStripMenuItem("Panel display mode") { DropDownItems = { _mode0, _mode1 } });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_console);
        menu.Items.Add(new ToolStripMenuItem("Open config file", null, (_, _) => OpenConfig()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitRequested?.Invoke()));

        _icon = new NotifyIcon
        {
            Text = "JonsboLite",
            ContextMenuStrip = menu,
            Visible = true,
        };
        SetIcon(0, connected: false);
        RefreshChecks();
    }

    /// <summary>Marshals onto the UI thread. Safe to call from the send loop.</summary>
    public void Post(Action action)
    {
        if (_marshal.IsDisposed) return;
        try
        {
            if (_marshal.InvokeRequired) _marshal.BeginInvoke(action);
            else action();
        }
        catch (ObjectDisposedException) { /* shutting down */ }
        catch (InvalidOperationException) { /* handle gone */ }
    }

    public void UpdateStatus(Readings r, bool connected, string model)
    {
        Post(() =>
        {
            string u = _config.Unit == TempUnit.Fahrenheit ? "°F" : "°C";
            double cpu = Show(r.CpuTemp), gpu = Show(r.GpuTemp);

            _status.Text = connected ? $"Connected — {model}" : "Panel not found";

            // NotifyIcon tooltips are capped well below 127 chars on some shells; keep it tight.
            _icon.Text = connected
                ? $"CPU {cpu:F0}{u} {r.CpuLoad:F0}%  GPU {gpu:F0}{u} {r.GpuLoad:F0}%\nFAN {r.FanRpm:F0}  MEM {r.MemLoad:F0}%"
                : "JonsboLite — panel not found";

            SetIcon(cpu, connected);
        });
    }

    private double Show(double celsius) => Frame.ConvertTemp(celsius, _config.Unit);

    private void SetUnit(string unit)
    {
        _config.TempUnit = unit;
        _config.Save();
        RefreshChecks();
        _lastDrawnTemp = int.MinValue;   // force a redraw in the new unit
    }

    private void SetMode(int mode)
    {
        _config.DisplayMode = mode;
        _config.Save();
        RefreshChecks();
    }

    private void RefreshChecks()
    {
        _celsius.Checked = _config.Unit == TempUnit.Celsius;
        _fahrenheit.Checked = _config.Unit == TempUnit.Fahrenheit;
        _mode0.Checked = _config.DisplayMode == 0;
        _mode1.Checked = _config.DisplayMode == 1;
    }

    private void OpenConfig()
    {
        string path = _config.SourcePath ?? Config.DefaultPath;
        if (!File.Exists(path)) _config.Save(path);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open {path}\n\n{ex.Message}", "JonsboLite",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// Only touches the console window when this process is the sole owner of it.
    /// Launched from a terminal we share the caller's console, and hiding that would
    /// take the user's shell window down with us.
    /// </summary>
    public void SetConsoleVisible(bool visible)
    {
        IntPtr hwnd = Native.GetConsoleWindow();
        if (hwnd == IntPtr.Zero || !OwnsConsole())
        {
            _console.Visible = false;
            return;
        }
        Native.ShowWindow(hwnd, visible ? Native.SW_SHOW : Native.SW_HIDE);
        _consoleVisible = visible;
        _console.Text = visible ? "Hide console window" : "Show console window";
    }

    private static bool OwnsConsole()
    {
        var pids = new uint[4];
        uint count = Native.GetConsoleProcessList(pids, (uint)pids.Length);
        return count == 1;   // just us -> we created it, so it is ours to hide
    }

    private void ToggleConsole() => SetConsoleVisible(!_consoleVisible);

    /// <summary>
    /// Draws the CPU temperature straight into the tray icon. Redrawn only when the
    /// rounded value changes, since each render allocates a GDI icon handle.
    /// </summary>
    private void SetIcon(double temp, bool connected)
    {
        int rounded = (int)Math.Round(temp);
        if (connected == _lastDrawnConnected && rounded == _lastDrawnTemp) return;
        _lastDrawnTemp = rounded;
        _lastDrawnConnected = connected;

        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.Clear(Color.Transparent);

            string text = !connected ? "–" : rounded is < 0 or > 999 ? "!" : rounded.ToString();

            using var family = new FontFamily("Segoe UI");
            using var path = new GraphicsPath();
            // Lay the glyphs out unconstrained at a nominal size, then scale the resulting
            // path to fit. Fitting a rectangle instead would wrap "52" onto a second line
            // and clip it, and any fixed em size is a guess about font metrics and DPI.
            path.AddString(text, family, (int)FontStyle.Bold, 32f, PointF.Empty, StringFormat.GenericTypographic);

            RectangleF b = path.GetBounds();
            if (b.Width > 0.01f && b.Height > 0.01f)
            {
                const float margin = 2.5f;            // room for the outline stroke
                float target = size - margin * 2;

                // Scale against a fixed two-digit reference so 9 and 52 render at the same
                // size — sizing the glyph to its own bounds makes the icon visibly grow and
                // shrink as the reading changes. Only wider strings (3 digits, common in
                // Fahrenheit) shrink further to fit.
                float scale = Math.Min(ReferenceScale(family, target), Math.Min(target / b.Width, target / b.Height));

                using var m = new Matrix();
                m.Translate(size / 2f, size / 2f);
                m.Scale(scale, scale);
                m.Translate(-(b.X + b.Width / 2f), -(b.Y + b.Height / 2f));
                path.Transform(m);
            }

            // Dark outline keeps the glyph legible on both light and dark taskbars.
            using var outline = new Pen(Color.FromArgb(210, 0, 0, 0), 3.5f) { LineJoin = LineJoin.Round };
            g.DrawPath(outline, path);
            using var fill = new SolidBrush(HeatColor(temp, connected));
            g.FillPath(fill, path);
        }

        IntPtr fresh = bmp.GetHicon();
        _icon.Icon = Icon.FromHandle(fresh);
        if (_iconHandle != IntPtr.Zero) Native.DestroyIcon(_iconHandle);  // FromHandle does not take ownership
        _iconHandle = fresh;
    }

    /// <summary>Scale that makes a two-digit reading fill the icon; the size everything under
    /// three digits is drawn at.</summary>
    private static float ReferenceScale(FontFamily family, float target)
    {
        using var probe = new GraphicsPath();
        probe.AddString("88", family, (int)FontStyle.Bold, 32f, PointF.Empty, StringFormat.GenericTypographic);
        RectangleF r = probe.GetBounds();
        return r.Width < 0.01f || r.Height < 0.01f ? 1f : Math.Min(target / r.Width, target / r.Height);
    }

    private Color HeatColor(double shown, bool connected)
    {
        if (!connected) return Color.FromArgb(150, 150, 150);
        // Thresholds are in Celsius; convert back if the display is in Fahrenheit.
        double c = _config.Unit == TempUnit.Fahrenheit ? (shown - 32) * 5.0 / 9.0 : shown;
        if (c >= 85) return Color.FromArgb(255, 88, 88);
        if (c >= 70) return Color.FromArgb(255, 176, 46);
        return Color.FromArgb(120, 224, 143);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        if (_iconHandle != IntPtr.Zero) { Native.DestroyIcon(_iconHandle); _iconHandle = IntPtr.Zero; }
        _marshal.Dispose();
    }

    private static class Native
    {
        internal const int SW_HIDE = 0;
        internal const int SW_SHOW = 5;

        [DllImport("kernel32.dll")] internal static extern IntPtr GetConsoleWindow();
        [DllImport("kernel32.dll")] internal static extern uint GetConsoleProcessList(uint[] processList, uint count);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr hIcon);
    }
}
