using System.Runtime.Versioning;

namespace JonsboLite;

/// <summary>
/// Linux transport, over /dev/hidraw. The kernel binds hid-generic to this panel on its
/// own — there is no vendor driver and no kernel module to build.
///
/// hidraw's write() uses the same convention as the Windows HID stack: byte 0 is the
/// report number, and 0x00 for a device whose descriptor declares no report ID. This
/// panel declares none, so the 65-byte buffer is byte-for-byte what the Windows path
/// sends.
///
/// hidraw nodes are root-only by default. Ship a udev rule so the desktop user gets
/// access without the daemon needing any privilege at all:
///
///   SUBSYSTEM=="hidraw", ATTRS{idVendor}=="5131", ATTRS{idProduct}=="2007", TAG+="uaccess"
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxHidRawPanel : IPanelTransport
{
    private const int FrameLength = Frame.PayloadLength + 1;   // + report-ID prefix
    private const string HidRawClass = "/sys/class/hidraw";

    private FileStream? _stream;
    private readonly byte[] _frame = new byte[FrameLength];

    public string? DevicePath { get; private set; }
    public ushort ProductId { get; private set; }

    public string Model => PanelModels.Name(ProductId);
    public bool IsOpen => _stream is not null;
    public string? Description => IsOpen ? $"{Model}  {DevicePath}" : null;

    public bool Open()
    {
        Close();
        foreach ((string node, ushort pid) in Enumerate())
        {
            try
            {
                // BufferSize 0 keeps FileStream out of the way: each Write becomes one
                // write(2), which is what hidraw requires — a report cannot be split.
                _stream = new FileStream(node, new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Write,
                    Share = FileShare.ReadWrite,
                    BufferSize = 0,
                });
                DevicePath = node;
                ProductId = pid;
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                Console.Error.WriteLine(
                    $"no permission to open {node}. Install the udev rule described in " +
                    "reverse-engineering/PROTOCOL.md, then replug the panel.");
            }
            catch (IOException)
            {
                // Busy or vanished between enumeration and open; try the next candidate.
            }
        }
        return false;
    }

    public bool Send(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != Frame.PayloadLength)
            throw new ArgumentException($"payload must be exactly {Frame.PayloadLength} bytes", nameof(payload));
        if (_stream is null) return false;

        _frame[0] = 0x00;                    // report ID; the descriptor declares none
        payload.CopyTo(_frame.AsSpan(1));

        try
        {
            _stream.Write(_frame, 0, FrameLength);
            _stream.Flush();
            return true;
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            Close();                         // unplugged, most likely
            return false;
        }
    }

    /// <summary>
    /// Walks /sys/class/hidraw, matching each node's parent HID device by its uevent.
    /// HID_ID is "bus:vendor:product" with 8 hex digits each, e.g. 0003:00005131:00002007.
    /// </summary>
    private static IEnumerable<(string Node, ushort Pid)> Enumerate()
    {
        string[] entries;
        try { entries = Directory.GetDirectories(HidRawClass); }
        catch (DirectoryNotFoundException) { yield break; }   // no HID support in this kernel
        catch (IOException) { yield break; }

        Array.Sort(entries, StringComparer.Ordinal);

        foreach (string entry in entries)
        {
            string uevent = Path.Combine(entry, "device", "uevent");
            string[] lines;
            try { lines = File.ReadAllLines(uevent); }
            catch { continue; }

            ushort pid = 0;
            bool matched = false;
            foreach (string line in lines)
            {
                if (!line.StartsWith("HID_ID=", StringComparison.Ordinal)) continue;
                string[] parts = line["HID_ID=".Length..].Split(':');
                if (parts.Length != 3) break;
                // Vendor and product are normally 8 hex digits, of which the low 4 matter.
                // A shorter field would make the slices below throw, and that exception
                // would escape this iterator all the way out of the send loop — one
                // oddly-formatted device on the system would take the daemon down.
                if (parts[1].Length < 4 || parts[2].Length < 4) break;
                if (!ushort.TryParse(parts[1][^4..], System.Globalization.NumberStyles.HexNumber, null, out ushort vid)) break;
                if (!ushort.TryParse(parts[2][^4..], System.Globalization.NumberStyles.HexNumber, null, out pid)) break;
                matched = PanelModels.IsPanel(vid, pid);
                break;
            }
            if (!matched) continue;

            string node = "/dev/" + Path.GetFileName(entry);
            if (File.Exists(node)) yield return (node, pid);
        }
    }

    public void Close()
    {
        _stream?.Dispose();
        _stream = null;
        DevicePath = null;
    }

    public void Dispose() => Close();
}
