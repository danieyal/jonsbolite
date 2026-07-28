# JonsboLite

A lightweight replacement for the JONSBO PC Monitor desktop app. Drives the same
monitor panel (VID `0x5131`, PID `0x2007` / `0x2009` / `0x200A`) using the protocol
documented in [`../reverse-engineering/PROTOCOL.md`](../reverse-engineering/PROTOCOL.md).

|  | Original | JonsboLite |
|---|---|---|
| Install size | 293 MB | **3.7 MB** |
| Processes | 2 (Electron + `SystemInfos.exe`) | **1** |
| Runtime | Chromium + Node + .NET | .NET 8 |
| Sensor stack | `cpuidsdk64` + `HWiNFO64` + `Hardware.Info` | LibreHardwareMonitorLib |
| Sensor IPC | 64 KB shared file, JSON, polled at 900 ms | in-process |

## Build

```powershell
dotnet publish -c Release
# -> bin\Release\net8.0-windows\win-x64\publish\JonsboLite.exe
```

Framework-dependent, so it needs the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
For a no-runtime-required build at the cost of size, add `-p:SelfContained=true`.

## Run

```powershell
JonsboLite                     # live sensor data, 5 Hz, tray icon
JonsboLite --no-tray           # foreground, Ctrl+C to stop
JonsboLite --test              # fixed test pattern, no sensors touched
JonsboLite --show-binding      # which sensor feeds each panel field
JonsboLite --list-sensors      # every sensor name on this machine
JonsboLite --unit F            # Fahrenheit
JonsboLite --help
```

### Tray icon

The icon **is** the readout — it draws the current CPU temperature, tinted green below
70 °C, amber to 85 °C, red above, and grey when no panel is connected. Hovering shows CPU
and GPU temp/load plus fan and memory. Right-click for:

- **Temperature unit** — °C / °F, applied to both the panel and the icon
- **Panel display mode** — the 0/1 toggle Ocypus and Ruituo firmware interpret
- **Show/hide console window** — only offered when this process owns its console;
  launched from a terminal the console belongs to your shell and is left alone
- **Open config file**
- **Exit**

Unit and mode changes are written back to `jonsbolite.json` immediately. Use `--no-tray`
for a plain foreground process, `--console` to keep the window up in tray mode.

**Run it elevated.** LibreHardwareMonitor needs ring-0 access for CPU package
temperature/power/clock and motherboard fan tachometers. Unelevated those fields read 0
and the app says so on startup. `install-autostart.ps1` registers a hidden elevated logon
task — the same mechanism the official app uses — so you get elevation without a UAC
prompt every boot:

```powershell
# from an elevated PowerShell
.\install-autostart.ps1
Start-ScheduledTask -TaskName JonsboLite

.\install-autostart.ps1 -Uninstall
```

The official app's own autostart task must be disabled first, or both will fight over
the panel. Look for a `JONSBO_PC_Monitor` (or `C-STAT` / `HT_DIGITAL` / `Ocypus_Display`)
scheduled task.

## Configuration

Optional `jonsbolite.json` next to the exe. `--write-config` generates one.

```json
{
  "tempUnit": "C",
  "displayMode": 0,
  "intervalMs": 200,
  "sensors": {
    "pumpRpm": "Fan #3"
  }
}
```

`sensors` pins a panel field to an exact sensor, matched as `"Hardware / Sensor"` or just
`"Sensor"` — the headless equivalent of the official app's sensor picker. Use
`--list-sensors` to find names. Fields: `cpuTemp` `cpuLoad` `cpuPower` `cpuClock` `cpuVid`
`gpuTemp` `gpuLoad` `gpuPower` `gpuClock` `fanRpm` `pumpRpm` `memLoad`.

`displayMode` only matters on Ocypus (`0` = CPU temp, `1` = CPU power) and Ruituo
(`0` = CPU load, `1` = CPU power) panels; the firmware decides what to do with it.

Auto-binding is preference-ordered per field, so it works unconfigured on most systems.
`pumpRpm` and `cpuVid` are deliberately left unbound when nothing plausible matches —
showing a case fan as pump speed, or a `+12V` rail as CPU voltage, is worse than showing
nothing.

`cpuVid` prefers the SuperIO-measured **Vcore** rail over the CPU's own VID sensors. On
Zen APUs (verified on a Ryzen 7 8700F) every `Core #N VID` sensor pins at 0.08 V and
`Core (SVI2 TFN)` reports an implausible 1.55 V, while the board's Vcore tracks correctly
at ~1.1–1.3 V. Pin `cpuVid` in config if you want the raw VID instead.

## Layout

Two projects. `JonsboLite.Core` targets plain `net8.0` — no OS suffix, no platform
packages — so anything that reaches for P/Invoke, WinForms or LibreHardwareMonitor fails
to compile there. That is what keeps the protocol portable rather than merely intended to be.

**`../JonsboLite.Core/`** — portable, no OS dependency

| File | |
|---|---|
| `Frame.cs` | the protocol: packs readings into the 64-byte payload |
| `Readings.cs` | the twelve values, plus the temperature unit |
| `PanelField.cs` | field enum and its config-key naming |
| `IPanelTransport.cs` | how frames reach the panel |
| `ISensorSource.cs` | where values come from |
| `Config.cs` | `jonsbolite.json`, source-generated JSON (trim/AOT safe) |

**`JonsboLite/`** — Windows

| File | |
|---|---|
| `WindowsHidPanel.cs` | `IPanelTransport` via SetupAPI + `WriteFile` |
| `LibreHardwareSensors.cs` | `ISensorSource` via LibreHardwareMonitor + field binding |
| `Platform.cs` | the one place that picks an implementation |
| `Tray.cs` | notification-area icon |
| `Program.cs` | CLI, reconnect loop |

A port means two new classes and a branch in `Platform.cs`. The protocol, config, CLI and
send loop are untouched by it — see the Linux notes in
[../reverse-engineering/PROTOCOL.md](../reverse-engineering/PROTOCOL.md).

## Differences from the original

Two bugs from the original are fixed rather than reproduced (see PROTOCOL.md):

- GPU power fractional digits are actually sent; the original always sent `0`.
- Fahrenheit fractions are computed after the unit conversion, not before, so they are
  no longer garbage in °F mode.

Also: writes are exactly 65 bytes instead of a 69-byte buffer that Windows truncates.

## License

MIT ([../LICENSE](../LICENSE)). The single-file exe bundles LibreHardwareMonitorLib and
its transitive dependencies — four are MPL-2.0, one Apache-2.0 — so distribute
[../THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) with any binary you hand out.

## Not implemented

- The 64-byte **input report** the panel declares. The official app never reads it either,
  so it is unknown whether the firmware ever sends anything.
- Multiple panels at once. The original supports it; this opens the first one it finds.
- A tray icon / GUI. It is a console app that reconnects on hotplug and survives
  sleep/resume by virtue of reopening on any failed write.
