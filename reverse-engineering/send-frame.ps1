<#
Zero-dependency reference implementation of the JONSBO PC Monitor display protocol.

Opens the vendor-defined HID interface (VID 0x5131) and drives it with a single
64-byte output report. Proves the frame layout documented in PROTOCOL.md without
Electron, node-hid, hidapi or the CPUID/HWiNFO SDKs.

  .\send-frame.ps1                      # 20 s test pattern with distinctive values
  .\send-frame.ps1 -Seconds 60          # run longer
  .\send-frame.ps1 -CpuTemp 72.5 -GpuTemp 61 -CpuUtil 30 -GpuUtil 90 -FanRpm 900

Sensor values are parameters here on purpose: reading real sensors is the part
this script deliberately leaves to a proper implementation (LibreHardwareMonitorLib).
#>
[CmdletBinding()]
param(
  [int]    $Seconds     = 20,
  [double] $CpuTemp     = 88.50,
  [int]    $CpuUtil     = 42,
  [double] $CpuPower    = 65,
  [int]    $CpuClockMhz = 4550,
  [double] $CpuVid      = 1.25,
  [double] $GpuTemp     = 77,
  [int]    $GpuUtil     = 55,
  [double] $GpuPower    = 120,
  [int]    $GpuClockMhz = 2500,
  [int]    $FanRpm      = 1200,
  [int]    $PumpRpm     = 850,
  [int]    $MemUtil     = 61,
  [ValidateSet('C','F')][string] $TempUnit    = 'C',
  [ValidateRange(0,1)]  [int]    $DisplayMode = 0
)

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class JonsboHid {
  [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
  [DllImport("setupapi.dll", CharSet=CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr h, int f);
  [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr ds, IntPtr d, ref Guid g, int i, ref SP_DID did);
  [DllImport("setupapi.dll", CharSet=CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr ds, ref SP_DID did, IntPtr detail, int size, ref int req, IntPtr dd);
  [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr ds);
  [StructLayout(LayoutKind.Sequential)] struct SP_DID { public int cbSize; public Guid g; public int Flags; public IntPtr Res; }
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr CreateFile(string n, uint a, uint s, IntPtr sec, uint d, uint f, IntPtr t);
  [DllImport("kernel32.dll", SetLastError=true)] static extern bool WriteFile(IntPtr h, byte[] b, uint n, out uint written, IntPtr ov);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

  // 公版 0x2007, Ocypus 0x2009, Ruituo 0x200A -- all share VID 0x5131
  static readonly string[] Ids = { "vid_5131&pid_2007", "vid_5131&pid_2009", "vid_5131&pid_200a" };
  static IntPtr handle = IntPtr.Zero;
  public static string Path = null;

  public static bool Open() {
    Guid g; HidD_GetHidGuid(out g);
    IntPtr ds = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12); // PRESENT | DEVICEINTERFACE
    var did = new SP_DID(); did.cbSize = Marshal.SizeOf(did);
    try {
      for (int i = 0; SetupDiEnumDeviceInterfaces(ds, IntPtr.Zero, ref g, i, ref did); i++) {
        int req = 0;
        SetupDiGetDeviceInterfaceDetail(ds, ref did, IntPtr.Zero, 0, ref req, IntPtr.Zero);
        IntPtr det = Marshal.AllocHGlobal(req);
        try {
          Marshal.WriteInt32(det, IntPtr.Size == 8 ? 8 : 6);
          if (!SetupDiGetDeviceInterfaceDetail(ds, ref did, det, req, ref req, IntPtr.Zero)) continue;
          string p = Marshal.PtrToStringUni(new IntPtr(det.ToInt64() + 4));
          if (p == null) continue;
          foreach (string id in Ids) {
            if (p.IndexOf(id, StringComparison.OrdinalIgnoreCase) < 0) continue;
            handle = CreateFile(p, 0x40000000u /* GENERIC_WRITE */, 3 /* SHARE RW */, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero);
            if (handle.ToInt64() == -1) { handle = IntPtr.Zero; continue; }
            Path = p; return true;
          }
        } finally { Marshal.FreeHGlobal(det); }
      }
    } finally { SetupDiDestroyDeviceInfoList(ds); }
    return false;
  }

  // The descriptor declares no Report ID, so Windows wants a leading 0x00 prefix
  // followed by exactly 64 payload bytes.
  public static bool Send(byte[] payload64) {
    byte[] frame = new byte[65];
    frame[0] = 0x00;
    Array.Copy(payload64, 0, frame, 1, 64);
    uint written;
    return WriteFile(handle, frame, 65, out written, IntPtr.Zero) && written == 65;
  }

  public static void Close() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
}
'@

function New-Payload {
  $p = New-Object byte[] 64

  # Values are clamped the same way the original does before splitting.
  $ct = [Math]::Min($CpuTemp,  999.0); $gt = [Math]::Min($GpuTemp,  999.0)
  $cp = [Math]::Min($CpuPower, 9999.0); $gp = [Math]::Min($GpuPower, 9999.0)
  $cc = [Math]::Min($CpuClockMhz, 9999); $gc = [Math]::Min($GpuClockMhz, 9999)
  $fr = [Math]::Min($FanRpm, 9999);      $pr = [Math]::Min($PumpRpm, 9999)

  $cpW = [int][Math]::Floor($cp); $gpW = [int][Math]::Floor($gp)
  $unit = if ($TempUnit -eq 'F') { 1 } else { 0 }

  $p[0]  = 0x00; $p[1] = 0x01; $p[2] = 0x02                                  # constant header

  $p[3]  = [byte][Math]::Floor($ct)                                          # CPU temp integer
  $p[4]  = [byte][Math]::Floor(($ct - [Math]::Floor($ct)) * 100)             # CPU temp hundredths
  $p[5]  = [byte]$unit
  $p[6]  = [byte][Math]::Min($CpuUtil, 255)
  $p[7]  = [byte]($cpW % 100)                                                # CPU watts low
  $p[8]  = [byte][Math]::Floor(($cp - $cpW) * 100)                           # CPU watts hundredths
  $p[9]  = [byte][Math]::Floor($cc / 100)                                    # CPU MHz high
  $p[10] = [byte]($cc % 100)                                                 # CPU MHz low
  $p[11] = [byte][Math]::Floor($CpuVid)                                      # CPU VID volts
  $p[12] = [byte][Math]::Floor(($CpuVid - [Math]::Floor($CpuVid)) * 100)     # CPU VID hundredths

  $p[13] = [byte][Math]::Floor($gt)
  $p[14] = [byte][Math]::Floor(($gt - [Math]::Floor($gt)) * 100)
  $p[15] = [byte]$unit
  $p[16] = [byte][Math]::Min($GpuUtil, 255)
  $p[17] = [byte]($gpW % 100)
  $p[18] = [byte][Math]::Floor(($gp - $gpW) * 100)                           # original always sends 0 here
  $p[19] = [byte][Math]::Floor($gc / 100)
  $p[20] = [byte]($gc % 100)

  $p[21] = [byte][Math]::Floor($fr / 100); $p[22] = [byte]($fr % 100)        # fan rpm
  $p[23] = [byte][Math]::Floor($pr / 100); $p[24] = [byte]($pr % 100)        # pump rpm

  $now = Get-Date
  $p[25] = [byte][int]("$($now.Year)".Substring(0,1))                        # "2" of "2026"
  $p[26] = [byte]($now.Year % 100)
  $p[27] = [byte]$now.Month
  $p[28] = [byte]$now.Day
  $p[29] = [byte]$now.Hour
  $p[30] = [byte]$now.Minute
  $p[31] = [byte]$now.Second
  $p[32] = [byte][int]$now.DayOfWeek                                         # 0 = Sunday

  $p[33] = [byte][Math]::Min($MemUtil, 255)
  $p[34] = [byte][Math]::Floor($cpW / 100)                                   # CPU watts high
  $p[35] = [byte][Math]::Floor($gpW / 100)                                   # GPU watts high

  $p[40] = [byte]$DisplayMode                                                # Ocypus: 0 temp / 1 power
  $p[41] = [byte]$DisplayMode                                                # Ruituo: 0 util / 1 power
  return ,$p
}

if (-not [JonsboHid]::Open()) {
  Write-Error "No JONSBO display found (VID 0x5131, PID 0x2007/0x2009/0x200A). Is it plugged in?"
  exit 1
}

Write-Host "device : $([JonsboHid]::Path)"
Write-Host "sending: CPU $CpuTemp$TempUnit / $CpuUtil% / ${CpuClockMhz}MHz / ${CpuPower}W   GPU $GpuTemp$TempUnit / $GpuUtil% / ${GpuPower}W   FAN ${FanRpm}rpm   PUMP ${PumpRpm}rpm   MEM $MemUtil%"
Write-Host "cadence: 5 Hz for $Seconds s -- Ctrl+C to stop"

$deadline = (Get-Date).AddSeconds($Seconds)
$frames = 0
try {
  while ((Get-Date) -lt $deadline) {
    if (-not [JonsboHid]::Send((New-Payload))) {
      Write-Error "write failed (win32 error $([Runtime.InteropServices.Marshal]::GetLastWin32Error()))"
      break
    }
    $frames++
    Start-Sleep -Milliseconds 200
  }
} finally {
  [JonsboHid]::Close()
  Write-Host "sent $frames frames"
}
