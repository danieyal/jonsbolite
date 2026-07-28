# Third-party notices

This project is MIT licensed (see [LICENSE](LICENSE)). It builds against the packages
below, **all of which are bundled into the published single-file `JonsboLite.exe`**.
Distributing that executable therefore means distributing these components, and the
notices here travel with it.

## Weak-copyleft components (MPL-2.0)

The Mozilla Public License 2.0 is **file-level** copyleft. It does not affect the license
of this project's own source — MPL-2.0 §3.3 explicitly permits distributing a Larger Work
under terms of your choice. The obligations attach only to the MPL-covered files
themselves, which are used **unmodified**:

- keep the notices below intact, and
- per §3.2, tell recipients of the binary where to get the Source Code Form. The upstream
  URLs below satisfy that, since nothing here is modified.

| Component | Version | License | Source |
|---|---|---|---|
| LibreHardwareMonitorLib | 0.9.7-pre715 | MPL-2.0 | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor |
| BlackSharp.Core | 1.1.3 | MPL-2.0 | https://github.com/Blacktempel/BlackSharp |
| DiskInfoToolkit | 2.1.2 | MPL-2.0 | https://github.com/Blacktempel/DiskInfoToolkit |
| RAMSPDToolkit-NDD | 1.5.0 | MPL-2.0 | https://github.com/Blacktempel/RAMSPDToolkit |

Full text: https://mozilla.org/MPL/2.0/

If you ever **modify** any of these, that modified file stays MPL-2.0 and its source must
be made available. Everything in `JonsboLite/` is original work and unaffected.

## Permissive components

| Component | Version | License | Source |
|---|---|---|---|
| HidSharp | 2.6.4 | Apache-2.0 | https://software.seekye.com/hidsharp |
| Mono.Posix.NETStandard | 1.0.0 | MIT | https://github.com/mono/mono |
| System.CodeDom | 10.0.10 | MIT | https://github.com/dotnet/runtime |
| System.IO.Ports | 10.0.10 | MIT | https://github.com/dotnet/runtime |
| System.Management | 10.0.10 | MIT | https://github.com/dotnet/runtime |
| System.Threading.AccessControl | 10.0.10 | MIT | https://github.com/dotnet/runtime |

HidSharp is Apache-2.0: Copyright 2010-2025 James F. Bellinger. Apache-2.0 requires that
its attribution notice be retained, which this file does.
Full text: https://www.apache.org/licenses/LICENSE-2.0

HidSharp and the toolkit packages arrive transitively through LibreHardwareMonitorLib.
JonsboLite's own HID transport ([`HidDisplay.cs`](JonsboLite/HidDisplay.cs)) does not use
HidSharp — it calls SetupAPI and `WriteFile` directly.

## Kernel driver

LibreHardwareMonitor loads a signed kernel driver to read MSRs and SuperIO chips. That is
what makes elevation necessary, and it is why some security software flags hardware
monitors. Nothing here modifies or redistributes the driver beyond what the upstream
package does.

## Not included in this repository

The stock vendor application (`original-app/`) is proprietary to Dongguan Hongtai
Technology Co, Ltd. and is **not** redistributed here — see
[`reverse-engineering/original-app-manifest.md`](reverse-engineering/original-app-manifest.md).
[`reverse-engineering/PROTOCOL.md`](reverse-engineering/PROTOCOL.md) documents an interface
— device IDs, byte offsets, field encodings — and contains no vendor code.
