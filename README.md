# JONSBO PC Monitor — reverse engineering + lightweight replacement

[![CI](https://github.com/danieyal/jonsbolite/actions/workflows/ci.yml/badge.svg)](https://github.com/danieyal/jonsbolite/actions/workflows/ci.yml)

> [!WARNING]
> **This project is vibe coded — written largely by an AI assistant, with human review.
> Use it at your own risk.**
>
> It talks directly to hardware: it opens a USB HID device and writes to it several times
> a second, and on Windows it loads a kernel driver to read sensors. That is what the
> vendor software does too, and nothing here writes firmware, touches flash, or sends any
> command the stock application does not — but it has been verified on exactly one panel
> on one machine.
>
> The protocol findings in [`reverse-engineering/`](reverse-engineering/) are the solid
> part: they were confirmed against real hardware, not inferred. The Linux support in
> particular has never run against a physical panel at all. There is no warranty, express
> or implied — see [LICENSE](LICENSE).

## Tested on

| Device | Panel | OS | Status |
|---|---|---|---|
| Jonsbo TH240 ARGB AIO | `5131:2007` reference | Windows 11 | Working |

Only one device so far, and only one of the three known panel variants. If you have
other hardware — a different Jonsbo case, an Ocypus (`5131:2009`) or Ruituo (`5131:200a`)
panel, or any of this on Linux — testing it and adding a row here would be genuinely
useful. `jonsbolite --show-binding` reports what it detected.

The stock JONSBO PC Monitor app is a 293 MB Electron bundle plus a second .NET process,
all to push ~20 numbers to a small USB panel. This workspace documents how it talks to
that panel and replaces it with a single 3.7 MB executable.

## Layout

| Folder | Tracked | |
|---|---|---|
| [`JonsboLite/`](JonsboLite/) | yes | the replacement — Windows tray app and Linux daemon |
| [`JonsboLite.Core/`](JonsboLite.Core/) | yes | the protocol, portable `net8.0`, no OS dependency |
| [`packaging/`](packaging/) | yes | MSI and zip for Windows, tarball and .deb for Linux |
| [`reverse-engineering/`](reverse-engineering/) | yes | protocol spec + a dependency-free PowerShell prover |
| `original-app/` | **no** | the stock app, untouched, kept locally for reference |

`original-app/` is deliberately gitignored — 293 MB of third-party proprietary binaries
that are not ours to redistribute. What it contained is recorded in
[`reverse-engineering/original-app-manifest.md`](reverse-engineering/original-app-manifest.md):
version, update-feed lineage, and SHA-256 of the key artifacts, so the findings can be
re-verified against the same build. Clone this repo and that folder simply will not exist;
nothing in `JonsboLite/` depends on it.

Nothing on this machine launches `original-app/` either — it is a copy, not the live install.

## Contributing

CI builds both target frameworks on Windows and the portable one on Linux, with warnings
as errors, and runs the Linux sensor discovery against a real kernel.

Workflows are audited by [zizmor](https://docs.zizmor.sh/) in CI and should stay clean:

```bash
uv tool install zizmor          # or pipx install zizmor
GH_TOKEN=$(gh auth token) zizmor --persona pedantic .github/workflows/
```

The packaging scripts are linted with [shellcheck](https://www.shellcheck.net/), including
its style checks. On Windows there is no native package, so the least painful route is:

```powershell
npm install --no-save shellcheck
.\node_modules\shellcheck\bin\shellcheck.exe packaging/linux/*.sh
```

Actions are pinned to commit hashes rather than tags — a tag can be moved by whoever
controls it. The trailing comment on each `uses:` records the human-readable version.

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
