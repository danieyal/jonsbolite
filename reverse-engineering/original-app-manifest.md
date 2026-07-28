# Original app — provenance

`original-app/` holds the stock vendor application this workspace was reverse-engineered
from. It is **not tracked in git**: 293 MB across 129 files of third-party proprietary
binaries has no business in a source repository, and redistributing it is not ours to do.

This manifest records exactly which build the findings in [`PROTOCOL.md`](PROTOCOL.md)
came from, so they can be re-verified against the same bits.

## Build

| | |
|---|---|
| Product | JONSBO PC Monitor |
| Version | 1.0.4 (`package.json` inside `app.asar`) |
| Installer lineage | `HT_LED Setup 1.0.2.exe` (`resources/app-update.yml`, released 2024-12-11) |
| Publisher | Dongguan Hongtai Technology Co, Ltd. |
| Update feed | `https://ht-1303622853.cos.ap-guangzhou.myqcloud.com/JONSBO_PC_Monitor` |
| On-disk size | 293.4 MB, 129 files |

The same codebase ships white-labelled as **C-STAT**, **HT_DIGITAL**, **JONSBO_PC_Monitor**
and **Ocypus_Display** — one autostart task definition per brand sits in `resources/main/`.

## Key artifacts (SHA-256)

| File | Bytes | SHA-256 |
|---|---|---|
| `JONSBO PC Monitor.exe` | 172,479,552 | `2E486271BDE8370DEC724565C94BAC2F8EE5804314E6E9FCEAFAFB33E55F8661` |
| `resources/app.asar` | 22,745,486 | `9DF3F48084E80E1CBE1375EC16C525AF0D0D063ECCEC45F1DD9DF509C878BF7B` |
| `…/Release/SystemInfos.exe` | 130,112 | `0FD69608F7457B5AF90F63C76DA445F06CEBC4B929E711750C58BB2CC7AD359D` |
| `…/Release/cpuidsdk64.dll` | 3,204,688 | `859954AA51DD3E320B9D99BA0C9D824FD4CAB27912CBD78CC54E415B0B2A4C27` |
| `…/Release/HWiNFO64.dll` | 1,941,408 | `7C2B23D75C4E9FF89549599C0A3F5AAAC7D94A8CF2CC51A70678B100C747FC1F` |
| `…/Release/Hardware.Info.dll` | 103,424 | `0EFE34682450AEBB23B3CE017DC4B6644A370F776E293D0E628567914F6793DA` |

`…/Release/` is `resources/main/SDK/VC#/SystemInfos/vs2008/bin/Release/`.

## Reproducing the analysis

Everything in `PROTOCOL.md` comes from the Electron main process, which ships unminified
inside `app.asar`. No decompiler is needed — asar is a header-prefixed tar-alike:

```
uint32le  4
uint32le  headerPickleSize
uint32le  headerStringPickleSize
uint32le  headerStringLength
bytes     headerStringLength   // UTF-8 JSON directory
...                            // file contents at 8 + headerPickleSize + entry.offset
```

Extract with `npx asar extract app.asar out/`, then read:

| Path in the asar | |
|---|---|
| `main/util/device_hid.js` | the live code path — packs and writes the frame |
| `main/util/deviceList.js` | enumeration, hotplug, reconnect, power events |
| `main/constant/index.js` | the VID/PID table |
| `main/util/shared-memory-exchange.js` | the sensor IPC format |
| `main/util/device.js` | dead legacy code, requires a native addon that is not shipped |

The sensor side is observable at runtime without touching the binaries at all: run the
official app and read `%TEMP%\JONSBO_PC_Monitor.bin` — `uint32le` length followed by that
many bytes of UTF-8 JSON.
