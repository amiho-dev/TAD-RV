# Network Requirements

TAD.RV uses two protocols for communication between the admin dashboard and student endpoints.

---

## Required Ports

| Port | Protocol | Direction | Purpose |
|---|---|---|---|
| **17420** | TCP | Admin → Student | Control commands and screen stream |
| **17421** | UDP Multicast | Student → Admin (broadcast) | Zero-config endpoint discovery |

Both ports must be **open inbound on student PCs** and reachable from the teacher's machine.

---

## Multicast Discovery

Student PCs broadcast a **UDP multicast packet** every 5 seconds to:

```
Group address:  239.1.1.1
Port:           17421
```

TADAdmin listens on this address and automatically connects to any new endpoints it discovers. No IP lists, no DNS, no configuration required.

### Requirements for multicast

- All machines must be on the **same Layer-2 network segment** (same VLAN / subnet)
- Multicast must not be filtered by managed switches (most consumer switches pass multicast by default)
- Windows Firewall must allow inbound UDP on port 17421 *(the installer creates this rule automatically)*

> If your network spans multiple VLANs, see [Multicast Across VLANs](#multicast-across-vlans) below.

---

## TCP Control Channel

Once discovered, TADAdmin establishes a **persistent TCP connection** to each student on port 17420.

This channel carries:
- Lock / Unlock / Freeze commands
- Message broadcast payloads
- H.264 sub-stream frames (1fps 480p thumbnails)
- H.264 main-stream frames (30fps 720p Remote View)
- JPEG snapshot fallback frames
- Status beacons (hostname, username, lock state, etc.)

### Bandwidth per student

| Stream | Resolution | Framerate | Approx. bitrate |
|---|---|---|---|
| Sub-stream (thumbnail) | 480×270 | 1 fps | 150–250 kbps |
| Main-stream (Remote View) | 1280×720 | 30 fps | 2–4 Mbps |
| JPEG fallback (no GPU) | 480×270 | 1/30 s | ~60 kbps |

For a **30-student classroom**, worst-case bandwidth (all students being watched simultaneously in RV) would be ~120 Mbps — a standard Gigabit LAN handles this comfortably.

In normal operation (thumbnails only, no open RV), the total LAN load is approximately **5–8 Mbps**.

---

## Windows Firewall Rules

The installer automatically creates these inbound rules:

| Rule name | Direction | Protocol | Port |
|---|---|---|---|
| TAD.RV Discovery | Inbound | UDP | 17421 |
| TAD.RV Control | Inbound | TCP | 17420 |

To verify they are active:

```powershell
Get-NetFirewallRule -DisplayName "TAD.RV*" | Select DisplayName, Enabled, Direction
```

If the rules are missing, re-run the installer or add them manually:

```powershell
New-NetFirewallRule -DisplayName "TAD.RV Discovery" -Direction Inbound -Protocol UDP -LocalPort 17421 -Action Allow
New-NetFirewallRule -DisplayName "TAD.RV Control"   -Direction Inbound -Protocol TCP -LocalPort 17420 -Action Allow
```

---

## Third-Party Firewalls

If student PCs run endpoint security software (e.g. Defender for Endpoint, Sophos, ESET):

- Add an exception for `TADBridgeService.exe` in the firewall component
- Allow inbound UDP 17421 and TCP 17420 for the service executable

---

## Multicast Across VLANs

If the teacher's machine and student PCs are on **different VLANs**, UDP multicast will not traverse the router boundary without configuration.

Options:

### Option A: IGMP snooping + multicast routing

Enable PIM-SM or IGMP proxy on the inter-VLAN router to forward multicast group `239.1.1.1` between the relevant VLANs.

### Option B: Unicast fallback (manual IP list)

Add student IPs manually in TADAdmin via **Settings → Add endpoint manually**. TADAdmin will connect directly by TCP without needing multicast discovery.

### Option C: Same VLAN

The simplest solution — put teacher and student machines in the same VLAN.

---

## Corporate Proxy

TAD.RV communicates **only on the local LAN**. It does not make outbound internet connections during normal operation. The only exception is the **self-update check** (`TAD-Update.exe`), which contacts `github.com` to check for new releases.

To disable the update check, set in registry:

```
HKLM\SOFTWARE\TAD_RV\DisableAutoUpdate = 1 (DWORD)
```

---

## Summary Checklist

- [ ] Port TCP 17420 open inbound on all student PCs
- [ ] Port UDP 17421 open inbound on all student PCs
- [ ] Teacher machine and student PCs on same L2 segment (or multicast routing configured)
- [ ] `TADBridgeService.exe` allowed through any third-party endpoint firewall
