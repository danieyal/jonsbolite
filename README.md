# JONSBO PC Monitor — reverse engineering + lightweight replacement

The stock JONSBO PC Monitor app is a 293 MB Electron bundle plus a second .NET process,
all to push ~20 numbers to a small USB panel. This workspace documents how it talks to
that panel and replaces it with a single 3.7 MB executable.

## Layout

| Folder | Tracked | |
|---|---|---|
| [`JonsboLite/`](JonsboLite/) | yes | the replacement — Windows tray app and Linux daemon |
| [`JonsboLite.Core/`](JonsboLite.Core/) | yes | the protocol, portable `net8.0`, no OS dependency |
| [`reverse-engineering/`](reverse-engineering/) | yes | protocol spec + a dependency-free PowerShell prover |
| `original-app/` | **no** | the stock app, untouched, kept locally for reference |

`original-app/` is deliberately gitignored — 293 MB of third-party proprietary binaries
that are not ours to redistribute. What it contained is recorded in
[`reverse-engineering/original-app-manifest.md`](reverse-engineering/original-app-manifest.md):
version, update-feed lineage, and SHA-256 of the key artifacts, so the findings can be
re-verified against the same build. Clone this repo and that folder simply will not exist;
nothing in `JonsboLite/` depends on it.

Nothing on this machine launches `original-app/` either — it is a copy, not the live install.

## Start here

- **[`reverse-engineering/PROTOCOL.md`](reverse-engineering/PROTOCOL.md)** — the full wire
  format: USB HID, vendor-defined, one 64-byte output report at 5 Hz, every field a packed
  decimal. Also lists the bugs in the original worth not reproducing.
- **[`reverse-engineering/send-frame.ps1`](reverse-engineering/send-frame.ps1)** — drives the
  panel with no dependencies at all (raw `CreateFile`/`WriteFile`). Useful for confirming
  the protocol without building anything.
- **[`JonsboLite/README.md`](JonsboLite/README.md)** — build, run, configure.

## The short version

The panel is a HID device on VID `0x5131` (PID `0x2007` reference, `0x2009` Ocypus,
`0x200A` Ruituo), usage page `0xFF00`, no report ID, 64 bytes in each direction.

There is no handshake, no ACK, no checksum, no framing, and no image or theme upload.
You open it and start writing 64-byte frames:

```
offset 0..2    constant 00 01 02
offset 3..12   CPU   temp / unit / load / power / clock / VID
offset 13..20  GPU   temp / unit / load / power / clock
offset 21..24  fan and pump RPM
offset 25..32  date and time
offset 33..35  memory load, CPU and GPU power hundreds
offset 40..41  panel-specific display mode
```

Wide values are split across bytes by `/100` and `%100` rather than bit-packed, so
4550 MHz travels as `45, 50`.

## License

MIT — see [LICENSE](LICENSE).

The published executable bundles LibreHardwareMonitorLib and its dependencies, four of
which are MPL-2.0. That is file-level copyleft and does not affect this project's MIT
terms (MPL-2.0 §3.3), but the binary carries notice obligations — see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), and ship it alongside any release.

## Status

**Windows** — confirmed end-to-end against physical hardware: every field renders at the
expected position with the official app not running, driven by both `send-frame.ps1` and
JonsboLite. 3.7 MB against the stock 293 MB.

**Linux** — implemented and building (134 KB, or 13.2 MB self-contained), **not yet run on
hardware**. The panel needs no vendor driver there and no elevation at all: `hidraw` plus a
udev rule for the device, hwmon and procfs for the sensors. See
[`JonsboLite/README.md`](JonsboLite/README.md#linux-no-privilege-needed).

The 64-byte **input report** the panel declares is still unexplored — the official app
never reads it either, so it is unknown whether the firmware ever sends anything.
