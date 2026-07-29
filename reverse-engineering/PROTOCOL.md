# JONSBO PC Monitor — hardware protocol

Reverse-engineered from `JONSBO PC Monitor` v1.0.4 (`../original-app/resources/app.asar`,
unminified Electron main process) and verified against a live `VID_5131&PID_2007` device.

Paths below like `main/util/device_hid.js` are relative to the root of the extracted
`app.asar`; paths like `resources/main` are relative to `../original-app/`.

**Status: confirmed end-to-end.** `send-frame.ps1` in this folder drives the physical
display with the layout below — every field renders at the expected position and value,
with the official app not running.

## TL;DR

The device is a **USB HID vendor-defined display**. The entire protocol is **one
64-byte output report of packed BCD-ish decimal fields, resent every 200 ms**.
There is no handshake, no ACK, no framing, no checksum, no image/theme upload.

Everything else in the 172 MB install — Electron, Chromium, the React UI, the CPUID
and HWiNFO SDKs — exists only to produce ~20 numbers.

## Architecture of the original

```
SystemInfos.exe (.NET, admin)          Electron main                 Device
  cpuidsdk64.dll / HWiNFO64.dll  ──►  %TEMP%\JONSBO_PC_Monitor.bin ──►  HID
  Hardware.Info.dll                    (64 KB shared file, JSON)        64-byte
                                       polled every 900 ms              report
                                       node-hid write every 200 ms      @ 5 Hz
```

- `main/util/system.js` spawns `main/SDK/VC#/SystemInfos/vs2008/bin/Release/SystemInfos.exe`.
- `main/util/shared-memory-exchange.js` reads `%TEMP%\JONSBO_PC_Monitor.bin`:
  `uint32le length` followed by that many bytes of UTF-8 JSON. Capacity `1 << 16`.
- `main/util/device_hid.js` packs the frame and writes it. **This is the live code path.**
- `main/util/device.js` is dead legacy code — it `require`s a native addon
  (`main/SDK/c++Send/build/Release/rt_sender.node`) that is not shipped. Ignore it.

## Transport

| | |
|---|---|
| Class | USB HID, vendor-defined |
| Usage page / usage | `0xFF00` / `0x01` |
| Report ID | **none** (descriptor declares no Report ID item) |
| Output report | 64 bytes (Windows `OutputReportByteLength` = 65 incl. the report-ID prefix byte) |
| Input report | 64 bytes — **declared but never read by the app** |
| Feature reports | none |
| Cadence | `setInterval(..., 200)` → 5 Hz, unconditional resend |

### Known VID/PID (`main/constant/index.js`)

| VID | PID | Vendor comment |
|---|---|---|
| `0x5131` | `0x2007` | 公版 — "public/reference version" |
| `0x5131` | `0x2009` | 奥斯艾 — Ocypus |
| `0x5131` | `0x200A` | 锐拓 — Ruituo |

Device strings on the reference unit: manufacturer string is mojibake, product string `FBB`,
release `0x0100`.

### A note on byte 0

`device_hid.js` builds its buffer with `sendData[0] = 1` and hands it to `node-hid`.
That leading byte is the **Windows report-ID prefix**, which HIDCLASS strips before the
64 bytes go on the wire. So *app buffer index i* → *device payload offset i-1*.
This document uses payload offsets.

The app's buffer is also accidentally **69 bytes**, not 64 — `fill` is computed as
`64 - sendData.length` while `sendData.length` is still 38, and then indices 41/42 are
assigned afterwards, extending the array to 43 before the 26 filler bytes are appended.
Windows silently truncates the write to 65 (verified: `WriteFile` returns `written=65`).
Indices 38/39/40 are array holes that `Buffer.from` coerces to `0`.
Send exactly 65 bytes (`0x00` + 64 payload) and you avoid the whole mess.

## Frame layout (64-byte payload)

Every field is a plain unsigned byte holding a **decimal** quantity — values are split
across bytes by `/100` and `%100`, not by bit packing.

| Offset | Field | Encoding |
|---|---|---|
| 0 | header | constant `0x00` |
| 1 | header | constant `0x01` |
| 2 | header | constant `0x02` |
| 3 | CPU temp, integer part | °C or °F per offset 5 |
| 4 | CPU temp, fraction | hundredths (`.00`–`.99`) |
| 5 | temp unit | `0` = Celsius, `1` = Fahrenheit |
| 6 | CPU utilization | percent |
| 7 | CPU power, low | watts % 100 |
| 8 | CPU power, fraction | hundredths |
| 9 | CPU clock, high | MHz / 100 |
| 10 | CPU clock, low | MHz % 100 |
| 11 | CPU VID, integer part | volts |
| 12 | CPU VID, fraction | hundredths |
| 13 | GPU temp, integer part | |
| 14 | GPU temp, fraction | hundredths |
| 15 | temp unit | same value as offset 5 |
| 16 | GPU utilization | percent |
| 17 | GPU power, low | watts % 100 |
| 18 | GPU power, fraction | hundredths — **always 0, see bugs** |
| 19 | GPU clock, high | MHz / 100 |
| 20 | GPU clock, low | MHz % 100 |
| 21 | Fan RPM, high | rpm / 100 |
| 22 | Fan RPM, low | rpm % 100 |
| 23 | Pump RPM, high | rpm / 100 |
| 24 | Pump RPM, low | rpm % 100 |
| 25 | year, first digit | `"2"` of `"2026"` |
| 26 | year, last two digits | `26` |
| 27 | month | 1–12 |
| 28 | day of month | 1–31 |
| 29 | hours | 0–23 |
| 30 | minutes | 0–59 |
| 31 | seconds | 0–59 |
| 32 | weekday | JS `getDay()` — 0 = Sunday |
| 33 | memory utilization | percent |
| 34 | CPU power, high | watts / 100 |
| 35 | GPU power, high | watts / 100 |
| 36–39 | unused | 0 |
| 40 | display mode (Ocypus) | `0` = show CPU temp, `1` = show CPU power |
| 41 | display mode (Ruituo) | `0` = show CPU utilization, `1` = show CPU power |
| 42–63 | unused | 0 |

Reassembly:

```
cpu_temp_C  = p[3]  + p[4]/100          # p[5] says which unit
cpu_util_%  = p[6]
cpu_power_W = p[34]*100 + p[7] + p[8]/100
cpu_clock   = p[9]*100  + p[10]         # MHz
cpu_vid_V   = p[11] + p[12]/100
gpu_temp    = p[13] + p[14]/100
gpu_util_%  = p[16]
gpu_power_W = p[35]*100 + p[17] + p[18]/100
gpu_clock   = p[19]*100 + p[20]         # MHz
fan_rpm     = p[21]*100 + p[22]
pump_rpm    = p[23]*100 + p[24]
mem_util_%  = p[33]
```

### Encodable ranges

Every field is a byte, so the wire format itself is the limit. An implementation must
clamp to these; the vendor app does not, and wraps instead.

| Field | Encodable range | Why |
|---|---|---|
| Temperatures (offsets 3, 13) | **0–255** | single byte for the whole part |
| Fractions (offsets 4, 8, 12, 14, 18) | 0–99 | hundredths |
| Percentages (6, 16, 33) | 0–100 | |
| Clocks and RPM (9/10, 19/20, 21/22, 23/24) | 0–9999 | high byte is `value / 100`, so ≤ 99 |
| Power (34+7+8, 35+17+18) | 0–9999 | high byte is `watts / 100` |

The hi/lo pairs cannot overflow once the source is clamped to 9999, because `9999 / 100`
is 99. Temperatures are the trap: the vendor app clamps them to **999** and then writes
that into one byte, so 266 °F — a 130 °C CPU in Fahrenheit mode — is transmitted as 10.
JonsboLite caps the whole part at 255 instead, so an out-of-range reading looks obviously
wrong rather than plausibly cold.

## Connection lifecycle

- Enumerate HID devices, keep those matching the VID/PID table.
- Open by path; the app keys everything on `pnpId || path`.
- No init sequence — start writing immediately.
- `usb.on("attach"/"detach")` (libusb) drives hotplug; on attach it waits 500 ms then reconnects.
- A reconnect sweep is rescheduled every 2–4 s (randomised) regardless of state.
- On `powerMonitor` `suspend` / `lock-screen` it closes all handles; on `resume` /
  `unlock-screen` it destroys and reconnects.
- Any write error tears down the interval and closes the device.

## Bugs in the original worth *not* copying

1. **GPU power fraction is always zero.** `device_hid.js:282` computes
   `(gpu_PowerDraw?.value - gpu_PowerDraw) * 100`, but `gpu_PowerDraw` is already a
   number so `.value` is `undefined` → `NaN` → coerced to `0`. The CPU path uses
   `cpuPowerDrawObj?.value` correctly.
2. **Fahrenheit fractions are wrong.** The integer part is converted to °F, but the
   fraction is derived from the raw Celsius value:
   `cpu_Temp_X = cputemperatureObj?.value - cpu_Temp_Z` subtracts a Fahrenheit integer
   from a Celsius float, so offset 4 is garbage (often clamped to 0) in °F mode.
3. **69-byte writes**, described above.
4. **Unconditional 5 Hz resend.** `device_hid.js` always writes; the dead `device.js`
   at least skipped duplicate frames. The frame changes every second anyway (clock),
   so dedupe only helps between clock ticks.
5. `sendList` starts as `new Array(64).fill(0)` (a JS array, not a Buffer) and is written
   as-is until the first sensor callback lands.

## Sensor side

The shared-memory JSON has these top-level keys:

`baseInfo`, `pid`, `cpuLayout`, `graphics`, `memLayout`, `diskLayout`, `netInfo`,
`mainBordLayout`, `unKnowLayout`, `coolLayout`, `fanLayout`

Each `*Layout` object carries parallel arrays: `temperature_list`, `utilization_list`,
`powerDraw_list`, `currentRefreshRate_list`, `vid_list`, `fanSpeed_list`, `name_list`.
Each entry is `{ sensorid, sensorname, value }`. The UI lets the user pick which sensor
in each list is `checked`; the packer takes the checked one, else the first.

Frame fields map to: `cpuLayout_temperature`, `cpuLayout_utilization`,
`cpuLayout_powerDraw`, `cpuLayout_currentRefreshRate`, `cpuLayout_vid`,
`graphics_temperature`, `graphics_utilization`, `graphics_powerDraw`,
`graphics_currentRefreshRate`, `fanLayout_fanSpeed`, `coolLayout_fanSpeed`,
`memLayout_utilization`.

`netInfo`, `diskLayout` and `mainBordLayout` are collected but **never sent to this device**.

## Building a lightweight replacement

The display side is trivial — 64 bytes at 5 Hz, no state. The only real work is sensors.

- **Sensors:** [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)
  covers every value this frame needs (CPU package temp/power/clock/VID, GPU temp/util/power/clock,
  Nuvoton/ITE fan tachos, RAM load) and replaces `cpuidsdk64.dll` + `HWiNFO64.dll` + `Hardware.Info.dll`.
  Needs admin for ring-0 access, same as the original.
- **HID:** raw `CreateFile`/`WriteFile` on the HID interface path — no hidapi needed on Windows.
  See `send-frame.ps1` in this folder for a zero-dependency proof.
- **Shape:** a single C# tray app (LibreHardwareMonitorLib + HID write loop) lands around
  a few MB against 172 MB, with one process instead of two and no Chromium.
- Keep the 200 ms cadence — the firmware has no timeout behaviour documented, but the
  original never stops writing while connected.
- The 64-byte **input report is unexplored**. If you want device→host status or button
  events, that is where to look; the official app never reads it.

## Porting to Linux

Nothing in the protocol is Windows-specific, and the panel needs no vendor driver — the
kernel binds `hidraw` to a vendor-defined HID automatically.

**Transport.** Open `/dev/hidraw*`; discover by scanning `/sys/class/hidraw/*/device/uevent`
for `HID_ID=0003:00005131:00002007` (or `...2009` / `...200A`). The **same 65-byte buffer
works unchanged**: Linux hidraw's `write()` also takes the report ID as byte 0, using
`0x00` for unnumbered reports, which is exactly what the Windows path already sends.

hidraw nodes are root-only by default, so ship a udev rule — one line per supported
product ID, or a vendor-wide match if you prefer:

```udev
SUBSYSTEM=="hidraw", ATTRS{idVendor}=="5131", ATTRS{idProduct}=="2007", TAG+="uaccess"
SUBSYSTEM=="hidraw", ATTRS{idVendor}=="5131", ATTRS{idProduct}=="2009", TAG+="uaccess"
SUBSYSTEM=="hidraw", ATTRS{idVendor}=="5131", ATTRS{idProduct}=="200a", TAG+="uaccess"
```

`idProduct` is matched lowercase. `uaccess` grants the logged-in user access, so the panel
needs no privilege at all. The shipped rule is
[`JonsboLite/Linux/99-jonsbolite.rules`](../JonsboLite/Linux/99-jonsbolite.rules), which
also carries a `GROUP=` variant for headless machines where there is no active seat for
`uaccess` to target.

**Sensors.** LibreHardwareMonitor is effectively Windows-only; the equivalents are sysfs:

| Field | Source |
|---|---|
| CPU temp | `/sys/class/hwmon/*` with `name` = `k10temp` (AMD) or `coretemp` (Intel) → `temp1_input` |
| CPU load | `/proc/stat` |
| CPU clock | `/sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq` |
| CPU power | `/sys/class/powercap/` RAPL — coverage on Zen is patchy, the weakest field |
| Vcore, fans | SuperIO driver, `nct6683` for the NCT6687D — usually needs `modprobe nct6683 force=1`, as most consumer boards are not whitelisted |
| GPU (AMD) | `/sys/class/drm/card*/device/gpu_busy_percent` plus its `hwmon/` for temp, power, freq |
| GPU (NVIDIA) | NVML (`libnvidia-ml.so`) |
| Memory | `/proc/meminfo` |

hwmon is world-readable, so unlike Windows this needs **no elevation**.

**Everything else.** `JonsboLite.Core` already targets plain `net8.0`; a port is two new
classes behind `IPanelTransport` / `ISensorSource` and a branch in `Platform.cs`. The tray
is the one piece that does not carry over — WinForms `NotifyIcon` is Windows-only, so
either run headless under `systemd --user` or implement StatusNotifierItem over D-Bus.

None of the Linux specifics above have been tested on hardware; they are derived from the
kernel interfaces, unlike the Windows behaviour in this document, which is verified.

## Open questions

- Whether the firmware requires the `00 01 02` header or ignores it (it is constant in
  every frame the official app sends, so keep it).
- What PIDs `0x2009` / `0x200A` render differently — only the two `displayMode` bytes
  differ in software, so the layout choice is firmware-side.
- Whether any input report is ever produced by the device.
