# TAD.RV — Teacher Controller Guide

**(C) 2026 TAD Europe — https://tad-it.eu**

> **Scope**: User guide for the TAD.RV Teacher Controller application —
> classroom monitoring, screen viewing, and student management.

---

## Table of Contents

1. [Getting Started](#1-getting-started)
2. [Interface Overview](#2-interface-overview)
3. [Student Discovery](#3-student-discovery)
4. [Live Monitoring](#4-live-monitoring)
5. [Remote Viewing (RV)](#5-remote-viewing-rv)
6. [Screen Freeze](#6-screen-freeze)
7. [Room Management](#7-room-management)
8. [Language Settings](#8-language-settings)
9. [Troubleshooting](#9-troubleshooting)

---

## 1. Getting Started

### System Requirements

| Requirement | Details |
|---|---|
| **OS** | Windows 10/11 (64-bit) |
| **Network** | Same LAN/VLAN as student machines |
| **Bandwidth** | 10+ Mbps recommended for class of 30 |
| **Display** | 1920×1080 minimum recommended |

### Launch

Run `TadTeacher.exe` — no installation or admin privileges required. The application opens maximised and immediately begins discovering student endpoints on the local network.

### Prerequisites on Student Machines

Students must be running `TadBridgeService.exe` with:
- **Multicast discovery** enabled (default)
- **TCP listener** active on port 47101 (default)
- **Screen capture** policy enabled (`LOG_SCREENSHOTS` flag)

## 2. Interface Overview

```
┌──────────────────────────────────────────────────────────────┐
│  [TAD.RV]  Student View ▾  Actions ▾        🌐 EN  Room: 101│
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐       │
│  │Student 1│  │Student 2│  │Student 3│  │Student 4│       │
│  │ [thumb] │  │ [thumb] │  │ [thumb] │  │ [thumb] │       │
│  │  ●live  │  │  ●live  │  │ ●frozen │  │  ●live  │       │
│  └─────────┘  └─────────┘  └─────────┘  └─────────┘       │
│                                                              │
│  ┌─────────┐  ┌─────────┐                                  │
│  │Student 5│  │Student 6│                                  │
│  │ [thumb] │  │ [thumb] │    Connected: 6                  │
│  │  ●live  │  │  ●live  │    Room: Lab 101                 │
│  └─────────┘  └─────────┘                                  │
│                                                              │
├──────────────────────────────────────────────────────────────┤
│  ● 6 connected  │  0 alerts  │  Room: Lab 101              │
└──────────────────────────────────────────────────────────────┘
```

### Top Navigation Bar

| Element | Description |
|---|---|
| **Student View** | Switch between thumbnail grid and list view |
| **Actions** | Freeze All, Unfreeze All, Send Message |
| **Language selector** | Switch UI language (EN, DE, FR, NL, ES, IT, PL) |
| **Room selector** | Choose active classroom layout |

### Stats Bar

| Indicator | Description |
|---|---|
| **Connected** | Number of active student connections |
| **Alerts** | Unread security alerts from student endpoints |
| **Room** | Currently selected classroom |

## 3. Student Discovery

The Teacher Controller uses **UDP multicast** to discover student endpoints automatically:

- Multicast group: `239.77.65.68`
- Port: `47100`
- Announcement interval: every 5 seconds

Students appear in the grid within seconds of connecting to the network. No manual configuration is required as long as Teacher and Students share the same VLAN.

### Firewall Requirements

| Direction | Protocol | Port | Purpose |
|---|---|---|---|
| Inbound | UDP | 47100 | Receive student multicast announcements |
| Outbound | TCP | 47101 | Connect to student service for RV/commands |

## 4. Live Monitoring

The student grid shows:
- **Thumbnail**: Scaled-down live screenshot (updated every few seconds)
- **Status indicator**: Green (live), Blue (frozen), Grey (offline)
- **Student name**: Hostname or AD display name
- **Alert badge**: Red dot if the student has active alerts

Click a student thumbnail to select it. Selection enables per-student actions.

## 5. Remote Viewing (RV)

Double-click a student tile or use the **View Screen** button to open full-resolution remote viewing.

### Features

- **H.264 decoded**: Efficient video streaming via WebCodecs API
- **Adaptive quality**: Automatically adjusts based on available bandwidth
- **Privacy redaction**: Sensitive areas (passwords, banking) are masked by the service before transmission
- **Non-interactive**: View-only — Teacher cannot control the student's mouse or keyboard

### Closing RV

Click the **×** button or press **Escape** to return to the grid view.

## 6. Screen Freeze

Freeze displays a full-screen overlay on student machines, blocking interaction.

### Freeze All

1. Click **Actions → Freeze All** (or use keyboard shortcut)
2. Select duration:
   | Option | Description |
   |---|---|
   | **Until manually unfrozen** | Stays frozen until you click Unfreeze All |
   | **5 minutes** | Auto-unfreezes after 5 minutes |
   | **10 minutes** | Auto-unfreezes after 10 minutes |
   | **15 minutes** | Auto-unfreezes after 15 minutes |
3. Choose freeze type:
   - **Blank screen**: Student sees a black screen with "Screen Frozen by Teacher"
   - **Attention**: Student sees "Look at the teacher" message
4. Click **Freeze**

### Freeze Selected

Select one or more students in the grid, then use **Actions → Freeze Selected**.

### Unfreeze

- **Unfreeze All**: Releases all frozen students immediately
- **Individual**: Right-click a frozen student → Unfreeze

## 7. Room Management

Rooms define the spatial layout of student machines in a classroom. This helps teachers identify physical location from the software view.

### Creating a Room

1. Open the **Room selector** dropdown
2. Click **Manage Rooms**
3. Click **Add Room**
4. Enter room name (e.g., "Lab 101")
5. Drag student tiles to match physical desk arrangement
6. Click **Save**

Rooms are stored locally on the teacher's machine.

## 8. Language Settings

Click the language indicator (e.g., **EN**) in the top navigation bar to switch the UI language.

| Code | Language |
|---|---|
| EN | English |
| DE | Deutsch |
| FR | Français |
| NL | Nederlands |
| ES | Español |
| IT | Italiano |
| PL | Polski |

Language preference is saved and restored on next launch.

## 9. Troubleshooting

### Students not appearing

| Cause | Solution |
|---|---|
| Different VLAN | Ensure Teacher and Students are on the same network segment |
| Firewall blocking UDP | Open UDP port 47100 inbound |
| Service not running | Verify `TadBridgeService` is running on student machines |
| Multicast disabled | Check network switch supports multicast / IGMP snooping |

### RV not working

| Cause | Solution |
|---|---|
| TCP blocked | Open TCP port 47101 between Teacher and Students |
| Screen capture disabled | Ensure `LOG_SCREENSHOTS` flag is set in Policy.json |
| High latency | Reduce number of concurrent RV sessions |

### Freeze not taking effect

| Cause | Solution |
|---|---|
| Student offline | Check student connection status (grey indicator) |
| Service crashed | Check Windows Event Log on student machine |
| TCP connection lost | Student will auto-reconnect; retry freeze |

### General

- Check the status bar at the bottom of the window for connection counts
- All errors are logged to the Windows Event Log under source `TadTeacher`
- For persistent issues, try restarting the Teacher application

---

*See also: [Architecture.md](Architecture.md) · [Deployment-Guide.md](Deployment-Guide.md) · [Console-Guide.md](Console-Guide.md)*
