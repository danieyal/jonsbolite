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

## Install

Grab a build from the [releases page](https://github.com/danieyal/jonsbolite/releases).

**Windows** — run the `.msi`, or unzip the portable build anywhere. Needs the
[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0). Then use
*Enable JonsboLite at startup* from the Start Menu to register the elevated logon task —
without it, CPU package temperature, power and fan speeds read 0.

**Linux** — `sudo dpkg -i jonsbolite_*.deb`, or unpack a tarball and run `./install.sh`.
Self-contained, so there is no runtime to install, and no root is needed to *run* it —
only to drop the udev rule in place. Replug the panel afterwards.

Either way, start with `jonsbolite --show-binding` to see which sensors it picked.

## Build

Packaging scripts live in [`../packaging/`](../packaging/) and produce everything a
release contains:

```powershell
.\packaging\windows\build-msi.ps1 -Version 1.0.0   # MSI + portable zip -> dist\
```
```bash
./packaging/linux/build-packages.sh 1.0.0          # tar.gz + .deb, x64 and arm64 -> dist/
```

The Windows script needs the WiX CLI: `dotnet tool install --global wix --version 5.0.2`.
Version 6 and later require accepting a paid maintenance-fee licence, so this pins to 5,
the last MIT-licensed line.

To build by hand:

**Windows**

```powershell
dotnet publish -f net8.0-windows -c Release
# -> bin\Release\net8.0-windows\win-x64\publish\JonsboLite.exe        3.7 MB
```

Needs the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

**Linux**

```bash
dotnet publish -f net8.0 -r linux-x64 -c Release
# -> bin/Release/net8.0/linux-x64/publish/JonsboLite                  134 KB

# or with the runtime baked in, nothing to install on the target:
dotnet publish -f net8.0 -r linux-x64 -c Release \
  -p:SelfContained=true -p:PublishTrimmed=true                      # 13.2 MB
```

The Linux build is tiny because it needs no sensor library — the kernel already exposes
everything through hwmon and procfs. It trims cleanly, so the self-contained build is a
single dependency-free 13 MB file. Swap `linux-x64` for `linux-arm64` on a Pi or similar.

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

### Windows: run it elevated

LibreHardwareMonitor needs ring-0 access for CPU package temperature/power/clock and
motherboard fan tachometers. Unelevated those fields read 0 and the app says so on startup.
`install-autostart.ps1` registers a hidden elevated logon task — the same mechanism the
official app uses — so you get elevation without a UAC prompt every boot:

```powershell
# from an elevated PowerShell
.\install-autostart.ps1
Start-ScheduledTask -TaskName JonsboLite

.\install-autostart.ps1 -Uninstall
```

The official app's own autostart task must be disabled first, or both will fight over
the panel. Look for a `JONSBO_PC_Monitor` (or `C-STAT` / `HT_DIGITAL` / `Ocypus_Display`)
scheduled task.

### Linux: no privilege needed

hwmon and procfs are world-readable, and panel access comes from a udev rule rather than
from running as root — so unlike Windows, nothing here wants elevation.

```bash
sudo cp Linux/99-jonsbolite.rules /etc/udev/rules.d/
sudo udevadm control --reload-rules && sudo udevadm trigger
# replug the panel, then:
./JonsboLite --show-binding
```

To run it permanently, `Linux/jonsbolite.service` is a systemd **user** unit — it starts
with your session and needs no root:

```bash
mkdir -p ~/.config/systemd/user && cp Linux/jonsbolite.service ~/.config/systemd/user/
# point ExecStart at the binary, then
systemctl --user daemon-reload && systemctl --user enable --now jonsbolite
journalctl --user -u jonsbolite -f
```

Both files carry notes on the headless case, where `uaccess` does not apply because there
is no active seat — use the `GROUP=` form of the rule and `loginctl enable-linger`.

Two things to expect on first run, and why `--show-binding` is the place to start:

- **Fan and Vcore need a SuperIO driver.** Most consumer boards are not in `nct6683`'s
  whitelist, so `sudo modprobe nct6683 force=1` is usually required before `fanRpm`,
  `pumpRpm` and `cpuVid` appear at all. Add it to `/etc/modules-load.d/` once it works.
- **CPU package power may be missing.** It comes from RAPL via `/sys/class/powercap`,
  whose AMD coverage varies by kernel version. If `cpuPower` reads 0 and no `package-0`
  zone exists, that field has no source on your kernel.

## Configuration

Optional `jonsbolite.json`, looked for in two places, in order:

1. **next to the executable** — a portable copy keeps its settings with it
2. **the per-user config directory** — `%APPDATA%\JonsboLite\` on Windows,
   `$XDG_CONFIG_HOME/jonsbolite/` (usually `~/.config/jonsbolite/`) on Linux

An installed copy lives somewhere you cannot write, so it uses the second. `--write-config`
generates a file and prints where it went; `--config <path>` overrides both.

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

**`JonsboLite/`** — the app, built for both `net8.0` and `net8.0-windows`

| File | |
|---|---|
| `Platform.cs` | the one place that picks an implementation |
| `Program.cs` | CLI, reconnect loop |
| `Windows/WindowsHidPanel.cs` | transport via SetupAPI + `WriteFile` |
| `Windows/LibreHardwareSensors.cs` | sensors via LibreHardwareMonitor |
| `Windows/Tray.cs` | notification-area icon |
| `Linux/LinuxHidRawPanel.cs` | transport via `/dev/hidraw` |
| `Linux/LinuxSysfsSensors.cs` | sensors via hwmon, procfs, powercap, drm |
| `Linux/99-jonsbolite.rules` | udev rule |
| `Linux/jonsbolite.service` | systemd user unit |

The split is compile-time: `Windows/` is excluded from the `net8.0` build and `Linux/`
from `net8.0-windows`, with `Platform.cs` choosing via `#if WINDOWS`. Building both
frameworks — which `dotnet build` does by default — is what stops either side rotting.

The only thing that does not carry over is the tray: WinForms `NotifyIcon` is Windows-only,
so the Linux build runs headless and `--no-tray` is implicit. `IStatusSink` is the seam
where a StatusNotifierItem implementation would go.

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

## Status

The Windows path is verified against hardware: bindings, frames and the panel itself.

**The Linux path has never been run.** It compiles and publishes, and the protocol it
sends is the same verified 65 bytes, but no part of the hidraw or sysfs code has touched a
real device. Treat the sensor bindings especially as a starting point — hwmon labelling
varies enough between boards that `--show-binding` and a few config pins are likely.

## Not implemented

- The 64-byte **input report** the panel declares. The official app never reads it either,
  so it is unknown whether the firmware ever sends anything.
- Multiple panels at once. The original supports it; this opens the first one it finds.
- A tray icon / GUI. It is a console app that reconnects on hotplug and survives
  sleep/resume by virtue of reopening on any failed write.
