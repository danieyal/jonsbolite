using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JonsboLite;

/// <summary>
/// Talks to the JONSBO / Ocypus / Ruituo monitor panel over its vendor-defined HID
/// interface. Raw SetupAPI + CreateFile/WriteFile — no hidapi, no node-hid.
///
/// The report descriptor declares no Report ID, so Windows expects a leading 0x00
/// prefix byte followed by exactly 64 payload bytes (OutputReportByteLength = 65).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HidDisplay : IDisposable
{
    public const int PayloadLength = 64;
    private const int FrameLength = PayloadLength + 1; // + report-ID prefix

    private const ushort VendorId = 0x5131;
    private static readonly ushort[] ProductIds = { 0x2007, 0x2009, 0x200A };

    private SafeFileHandle? _handle;
    private readonly byte[] _frame = new byte[FrameLength];

    public string? DevicePath { get; private set; }
    public ushort ProductId { get; private set; }
    public bool IsOpen => _handle is { IsInvalid: false, IsClosed: false };

    public static string ModelName(ushort pid) => pid switch
    {
        0x2007 => "reference (公版)",
        0x2009 => "Ocypus (奥斯艾)",
        0x200A => "Ruituo (锐拓)",
        _ => $"unknown 0x{pid:X4}",
    };

    /// <summary>Finds and opens the first supported panel. Returns false if none is present.</summary>
    public bool Open()
    {
        Close();
        foreach ((string path, ushort pid) in EnumerateDevices())
        {
            SafeFileHandle h = Native.CreateFile(
                path,
                Native.GENERIC_WRITE,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                IntPtr.Zero,
                Native.OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (h.IsInvalid)
            {
                h.Dispose();
                continue;
            }

            _handle = h;
            DevicePath = path;
            ProductId = pid;
            return true;
        }
        return false;
    }

    /// <summary>Writes one 64-byte payload. Returns false if the device went away.</summary>
    public bool Send(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != PayloadLength)
            throw new ArgumentException($"payload must be exactly {PayloadLength} bytes", nameof(payload));
        if (!IsOpen) return false;

        _frame[0] = 0x00; // Windows report-ID prefix; HIDCLASS strips it before the wire
        payload.CopyTo(_frame.AsSpan(1));

        if (Native.WriteFile(_handle!, _frame, FrameLength, out uint written, IntPtr.Zero) && written == FrameLength)
            return true;

        Close();
        return false;
    }

    private static IEnumerable<(string Path, ushort Pid)> EnumerateDevices()
    {
        Native.HidD_GetHidGuid(out Guid hidGuid);
        IntPtr set = Native.SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
            Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
        if (set == Native.INVALID_HANDLE_VALUE) yield break;

        try
        {
            var iface = new Native.SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<Native.SP_DEVICE_INTERFACE_DATA>() };
            for (int i = 0; Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref iface); i++)
            {
                string? path = GetInterfacePath(set, ref iface);
                if (path is null) continue;

                foreach (ushort pid in ProductIds)
                {
                    if (path.Contains($"vid_{VendorId:x4}&pid_{pid:x4}", StringComparison.OrdinalIgnoreCase))
                    {
                        yield return (path, pid);
                        break;
                    }
                }
            }
        }
        finally
        {
            Native.SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static string? GetInterfacePath(IntPtr set, ref Native.SP_DEVICE_INTERFACE_DATA iface)
    {
        Native.SetupDiGetDeviceInterfaceDetail(set, ref iface, IntPtr.Zero, 0, out int required, IntPtr.Zero);
        if (required <= 0) return null;

        IntPtr detail = Marshal.AllocHGlobal(required);
        try
        {
            // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W: 8 on x64 (4 pad + 4 size), 6 on x86
            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
            if (!Native.SetupDiGetDeviceInterfaceDetail(set, ref iface, detail, required, out _, IntPtr.Zero))
                return null;
            return Marshal.PtrToStringUni(detail + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(detail);
        }
    }

    public void Close()
    {
        _handle?.Dispose();
        _handle = null;
        DevicePath = null;
    }

    public void Dispose() => Close();

    private static class Native
    {
        internal const uint GENERIC_WRITE = 0x40000000;
        internal const uint FILE_SHARE_READ = 1;
        internal const uint FILE_SHARE_WRITE = 2;
        internal const uint OPEN_EXISTING = 3;
        internal const int DIGCF_PRESENT = 0x02;
        internal const int DIGCF_DEVICEINTERFACE = 0x10;
        internal static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

        [StructLayout(LayoutKind.Sequential)]
        internal struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [DllImport("hid.dll")]
        internal static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwnd, int flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid classGuid,
            int index, ref SP_DEVICE_INTERFACE_DATA iface);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA iface,
            IntPtr detail, int detailSize, out int required, IntPtr devInfo);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
            uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WriteFile(SafeFileHandle handle, byte[] buffer, uint count,
            out uint written, IntPtr overlapped);
    }
}
