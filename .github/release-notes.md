> [!WARNING]
> Vibe coded — largely AI-written, with human review, and verified on a single panel
> (Jonsbo TH240 ARGB AIO) on a single machine. It writes to a USB HID device and, on
> Windows, loads a kernel driver to read sensors. Use at your own risk; no warranty.

## Install

**Windows** — run the `.msi`, or unzip the portable build anywhere. Needs the
[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

For live CPU package temperature, power and fan speeds, use **Enable JonsboLite at
startup** from the Start Menu. Those sensors need ring-0 access, and without the elevated
logon task they read 0.

**Linux** — `sudo dpkg -i jonsbolite_*.deb`, or unpack a tarball and run `./install.sh`.
Self-contained, so there is no runtime to install, and JonsboLite needs no root to run —
only to place the udev rule. Replug the panel afterwards so the rule applies.

Then check what it picked up:

```
jonsbolite --show-binding
```

If fan, pump or Vcore read 0 on Linux, load your board's SuperIO driver:
`sudo modprobe nct6683 force=1` (or `nct6775` / `it87`, board dependent).

Anything bound to the wrong sensor can be pinned in `jonsbolite.json` — see
`--list-sensors`.

## Verify

`SHA256SUMS` covers every asset:

```
sha256sum -c SHA256SUMS
```

## Note on Linux

The Linux build has had far less real-world exposure than the Windows one. The panel
protocol is identical and verified, but hwmon sensor naming varies between boards, so
expect to pin a field or two.
