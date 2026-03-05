# TAD.RV — Wiki

> Real-time endpoint monitoring and control for Windows classrooms and managed labs.

**TAD.RV** gives IT administrators a live dashboard of every managed workstation — screen thumbnails, remote view streaming, lock/unlock, broadcast messages, software deployment, and Active Directory-aware provisioning. No kernel driver required in standard mode. Everything runs in user-mode.

---

## Quick Navigation

| | |
|---|---|
| 🚀 [Installation](Installation) | Install the service on endpoints and set up the admin dashboard |
| 🎓 [Admin Dashboard Guide](Admin-Dashboard) | Live grid, Remote View, recording, lock controls |
| 🖥️ [Management Console Guide](Management-Console) | Deployment, policy editor, alerts, classroom designer |
| 🌐 [Network Requirements](Network-Requirements) | Ports, protocols, firewall rules |
| 🔧 [Troubleshooting](Troubleshooting) | Common problems and fixes |
| ❓ [FAQ](FAQ) | Frequently asked questions |
| 📋 [Changelog](Changelog) | What's new in each version |
| 🏗️ [Architecture](Architecture) | Technical deep-dive into the system design |
| 🔨 [Build Guide](Build-Guide) | Build from source |

---

## What Is TAD.RV?

TAD.RV is a classroom and lab management platform for Windows environments. It is composed of three installable components:

| Component | Role | Who installs it |
|---|---|---|
| **TADBridgeService** | Background service on each student PC — captures screen, handles commands | On every managed endpoint |
| **TADAdmin** | Teacher dashboard — live grid, Remote View, lock, message, record | On the teacher's machine |
| **TADDomainController** | IT admin console — deploy, configure policy, manage alerts | On IT admin / server |

All three are included in the release downloads.

---

## System Requirements

| | Requirement |
|---|---|
| **OS** | Windows 10 / 11 (64-bit) |
| **Architecture** | x64 only |
| **Network** | All machines on the same LAN (UDP multicast + TCP) |
| **.NET runtime** | Self-contained — no runtime install needed |
| **GPU (optional)** | Intel iGPU (UHD 6xx/7xx) for hardware H.264 acceleration |
| **Domain (optional)** | Active Directory for group-based policy and provisioning |

---

## How Discovery Works

TAD.RV uses **zero-configuration UDP multicast** — the admin dashboard finds all online managed PCs automatically. No IP lists. No configuration files.

```
Student PCs broadcast  UDP 239.1.1.1:17421  every 5 seconds
TADAdmin listens       UDP 239.1.1.1:17421  and connects via TCP port 17420
```

---

## License

Proprietary — all rights reserved. © 2026 TAD Europe — https://tad-it.eu
