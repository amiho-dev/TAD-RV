# Architecture

> A technical overview of the TAD.RV system design. For deeper detail see [Architecture.md](https://github.com/amiho-dev/TAD-RV/blob/main/docs/Architecture.md) in the repository.

---

## System Overview

```
Teacher's machine                         Student PCs (each)
─────────────────                         ──────────────────
TADAdmin.exe                              TADBridgeService.exe
  WebView2 Dashboard        TCP 17420       (LocalSystem service)
  ├── TcpClientManager  ────────────────►  ├── TadTcpListener
  ├── DiscoveryListener ◄── UDP 17421 ───  ├── MulticastDiscovery
  └── WebCodecs decoder                    ├── ScreenCaptureEngine
       H.264 → Canvas                      │    DXGI → QuickSync H.264
                                           └── PrivacyRedactor

IT Admin's machine
──────────────────
TADDomainController.exe
  ├── Deployment wizard
  ├── Policy editor → HKLM\SOFTWARE\TAD_RV
  └── Alert viewer (Windows Event Log)
```

---

## Components

### TADAdmin (Teacher Dashboard)

- **Framework:** .NET 8 WPF shell + WebView2
- **UI:** Embedded SPA (HTML/CSS/JS) with GitHub-dark theme
- **C# ↔ JS bridge:** `PostWebMessageAsJson` / `window.chrome.webview.postMessage`
- **Networking:** `TcpClientManager` — persistent TCP connection pool to all students
- **Discovery:** `DiscoveryListener` — UDP multicast on 239.1.1.1:17421

### TADBridgeService (Endpoint Agent)

- **Framework:** .NET 8 Worker Service
- **Identity:** LocalSystem
- **Screen capture:** DXGI Desktop Duplication → Intel QuickSync H.264 (Media Foundation Transform)
  - Sub-stream: 480×270 @ 1fps, ~200 kbps
  - Main-stream: 1280×720 @ 30fps, ~3 Mbps
- **Privacy:** PrivacyRedactor blacks out password field regions before encoding
- **TCP server:** `TadTcpListener` on port 17420 — accepts one connection per admin dashboard
- **Discovery:** UDP multicast beacon every 5 seconds

### TADDomainController (IT Console)

- **Framework:** .NET 8 WPF shell + WebView2
- **Services:** `DeploymentService`, `RegistryService`, `EventLogService`, `SystemInfoService`, `TADServiceController`
- **Bridge:** `chrome.webview.hostObjects` for synchronous JS → C# calls

---

## Screen Streaming Pipeline

```
Student PC                                      Teacher PC
──────────────────────────────────────────────────────────────────────
DXGI Desktop Duplication
  │  (zero-copy GPU texture)
  ▼
PrivacyRedactor
  │  (blacks out password regions)
  ▼
QuickSync H.264 Encoder (MFT)
  │  Sub: 480p 1fps          Main: 720p 30fps
  ▼                          ▼
TadTcpListener ─── TCP ──► TcpClientManager
  binary frame               │
  [4-byte len][cmd][H.264]   ▼
                           PostWebMessageAsJson (base64)
                             │
                             ▼
                           WebCodecs VideoDecoder
                             │  (hardware H.264 decode)
                             ▼
                           Canvas.drawImage()
                             │
                      Student tile preview (480p 1fps)
                      RV modal (720p 30fps)
```

---

## Protocol Wire Format

All messages between TADAdmin and TADBridgeService use this binary framing:

```
[ 4 bytes big-endian: payload length ][ 1 byte: TadCommand ][ N bytes: payload ]
```

### Key Commands

| Command | Hex | Direction | Description |
|---|---|---|---|
| `RvStart` | `0x20` | Admin → Service | Start 1fps sub-stream |
| `RvStop` | `0x21` | Admin → Service | Stop sub-stream |
| `RvFocusStart` | `0x22` | Admin → Service | Start 30fps main-stream |
| `RvFocusStop` | `0x23` | Admin → Service | Stop main-stream |
| `VideoFrame` | `0xA0` | Service → Admin | H.264 sub-stream delta frame |
| `VideoKeyFrame` | `0xA1` | Service → Admin | H.264 sub-stream keyframe |
| `MainFrame` | `0xA2` | Service → Admin | H.264 main-stream delta frame |
| `MainKeyFrame` | `0xA3` | Service → Admin | H.264 main-stream keyframe |
| `Snapshot` | `0x50` | Admin → Service | Request JPEG snapshot |
| `SnapshotData` | `0xB0` | Service → Admin | JPEG response |
| `Lock` | `0x10` | Admin → Service | Lock screen |
| `Unlock` | `0x11` | Admin → Service | Unlock screen |
| `PushMessage` | `0x30` | Admin → Service | Show message dialog |
| `Logoff` | `0x40` | Admin → Service | Log off user |
| `Reboot` | `0x41` | Admin → Service | Reboot immediately |
| `Shutdown` | `0x42` | Admin → Service | Shutdown immediately |

---

## Optional Kernel Driver

The optional `TAD.RV.sys` WDM kernel driver adds:

| Capability | Mechanism |
|---|---|
| Process tamper protection | `ObRegisterCallbacks` — strips TERMINATE, VM_WRITE etc. |
| File system protection | Minifilter — blocks deletion/rename of protected files |
| Heartbeat watchdog | Kernel DPC timer — re-locks if service dies |
| Authenticated unload | 256-bit pre-shared key via `IOCTL_TAD_UNLOCK` |
| Rate limiting | 5 bad unlock attempts → 30-second lockout |

IOCTL communication: `DeviceIoControl` on `\\.\TadRvLink`, `METHOD_BUFFERED`, device type `0x8000`.

---

## Demo / Emulation Mode

Both TADAdmin and TADBridgeService support a demo/emulation mode:

- `TADAdmin.exe --demo` — generates 12 synthetic students with animated canvas desktops
- `TADBridgeService.exe --emulate` — simulates IOCTL responses without a kernel driver

Demo mode uses identical event interfaces to production mode so the full UI is exercisable.
