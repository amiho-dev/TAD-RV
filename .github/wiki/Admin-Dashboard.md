# Admin Dashboard Guide

**TADAdmin** is the teacher-facing dashboard for real-time classroom monitoring and control. It runs on the teacher's PC and connects to all student endpoints automatically.

---

## Table of Contents

1. [Launching TADAdmin](#1-launching-tadmin)
2. [The Student Grid](#2-the-student-grid)
3. [Live Video Thumbnails](#3-live-video-thumbnails)
4. [Remote View (Live Stream)](#4-remote-view-live-stream)
5. [Screen Recording](#5-screen-recording)
6. [Lock / Unlock](#6-lock--unlock)
7. [Send Message](#7-send-message)
8. [Web Lock / Program Lock](#8-web-lock--program-lock)
9. [Logoff / Reboot / Shutdown](#9-logoff--reboot--shutdown)
10. [View Modes](#10-view-modes)
11. [Keyboard Shortcuts](#11-keyboard-shortcuts)

---

## 1. Launching TADAdmin

```
TADAdmin.exe          — connects to live student endpoints
TADAdmin.exe --demo   — demo mode with 12 simulated students (no real endpoints needed)
```

On launch, TADAdmin listens on UDP multicast and connects to every TADBridgeService it finds on the LAN. All online student PCs appear in the grid within seconds — no IP configuration required.

---

## 2. The Student Grid

Each student is represented by a **tile** showing:

| Element | Meaning |
|---|---|
| **Video preview** | Live H.264 thumbnail (1fps, 480p) decoded on the iGPU |
| **Hostname** | Computer name |
| **Username** | Currently logged-in user |
| **Status dot** | 🟢 Online · 🔴 Locked · 🟠 Offline · ⚪ Connecting |
| **Indicator badges** | Locked, Web Locked, Program Locked, Recording, Streaming, Hand Raised, Network Disconnected |
| **Quick-action bar** | Lock / Unlock / Message / Details buttons (appear on hover) |

### Interacting with a tile

| Action | How |
|---|---|
| Open Remote View | **Double-click** the tile, or click the 👁️ Watch button |
| Context menu | **Right-click** the tile |
| Send message | Click the 💬 button on hover |
| Lock student | Click the 🔒 button on hover |

---

## 3. Live Video Thumbnails

TADAdmin automatically requests a **1fps H.264 sub-stream** from each connected student as soon as they come online. This gives you a live video thumbnail in each tile — no snapshots, no static screenshots.

The stream is hardware-decoded on the teacher's GPU using the **WebCodecs API** (Chrome/WebView2 built-in).

### What you see

- **Placeholder icon** (monitor symbol) — endpoint has connected but no frame received yet (takes 1–3 seconds)
- **Live video** — once the first H.264 keyframe arrives, the placeholder disappears and the tile shows real-time desktop content

### Bandwidth

The sub-stream uses approximately **150–250 kbps per student** at 1fps 480p. For a 30-student class: ~6–7 Mbps total on the classroom LAN — well within standard 1 Gbps switch capacity.

---

## 4. Remote View (Live Stream)

Remote View (RV) opens a **fullscreen live stream** of a single student's desktop at **30fps, 720p**, hardware-encoded on the student's PC and decoded on yours.

### Opening Remote View

- **Double-click** any student tile
- **Right-click** → Remote View
- Click the 👁️ **Watch** button on the tile

### Inside the RV modal

| Control | Function |
|---|---|
| 🔴 Record button | Start/stop screen recording of this student |
| 👁️ Privacy toggle | Blur the canvas (so students can't see you screen-sharing it) |
| 🔒 Lock button | Lock this student's screen |
| ✕ Close | Close the modal (stream continues in background as thumbnail) |

### Stream quality

- **Sub-stream** (fallback): 480p, 1fps — shown briefly while the main stream starts
- **Main-stream**: 720p, 30fps — switches automatically once the main encoder connects

The stream info in the modal footer shows which stream is active.

### Closing Remote View

Click ✕ or press `Escape`. The main-stream stops but the **1fps sub-stream continues** so grid thumbnails stay live.

---

## 5. Screen Recording

Record any student's screen as a **WebM video file** (VP9 codec), saved directly to your Downloads folder.

### Start recording

- **Right-click** a tile → **Record Screen**
- Or open Remote View → click the 🔴 **Record** button in the modal header

A red pulsing border appears on the tile and a "Recording" badge shows while active.

### Stop recording

- **Right-click** → **Stop Recording**
- Or click the 🔴 button again in the RV modal

The video saves automatically as:
```
TAD-Recording_{StudentName}_{timestamp}.webm
```

### Recording quality

| Started from | Quality |
|---|---|
| Grid (tile context menu) | Automatically upgrades to 30fps 720p for the duration |
| RV modal | Uses the already-active 30fps main-stream |

### Playback

The `.webm` file plays in any modern browser (Edge, Chrome, Firefox) or VLC.

---

## 6. Lock / Unlock

Locking a student shows a fullscreen overlay on their PC — they cannot interact with the desktop. The overlay covers all monitors.

### Lock individual student

- **Right-click** tile → **Lock**
- Click the 🔒 quick-action button on hover
- Or use the Lock button inside Remote View

### Lock all students

Click **Lock All** in the toolbar.

### Unlock

- **Right-click** → **Unlock**
- Click the 🔓 button
- **Unlock All** in the toolbar

The tile shows a red locked indicator while a student is locked.

---

## 7. Send Message

Display a text message in a popup on one or all student screens.

### Broadcast message (all students)

Click the **Message** button in the toolbar → type the message → Send.

### Message to one student

**Right-click** tile → **Send Message** → type → Send.

The message appears as a modal dialog on the student's screen until dismissed.

---

## 8. Web Lock / Program Lock

### Web Lock

Blocks all internet access on the student PC by disabling the network adapter (or applying a loopback route, depending on policy configuration).

- **Right-click** → **Web Lock** (toggle — click again to unlock)
- A 🌐 badge appears on the tile when active

### Program Lock

Prevents the launch of any unauthorized executables — only whitelisted applications can run.

- **Right-click** → **Program Lock** (toggle)
- A ⚙️ badge appears on the tile when active

---

## 9. Logoff / Reboot / Shutdown

Available in the right-click context menu:

| Action | Effect |
|---|---|
| **Logoff** | Signs the current user out immediately |
| **Reboot** | Restarts the PC immediately |
| **Shutdown** | Powers off the PC immediately |

> ⚠️ These actions are immediate and do not prompt the student for confirmation.

---

## 10. View Modes

Use the **view mode toggle** in the toolbar to switch between:

| Mode | Description | Best for |
|---|---|---|
| **Thumbnail** | Larger tiles with prominent video previews | Monitoring screen content |
| **List** | Compact rows — more students visible at once | Large classes, checking status |

---

## 11. Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| `Escape` | Close any open modal / Remote View |
| `F5` | Refresh / re-discover endpoints |
| `Ctrl+A` | Select all |
| `Ctrl+L` | Lock all |
| `Ctrl+U` | Unlock all |

---

## Indicator Reference

| Badge | Meaning |
|---|---|
| 🔒 Locked | Student screen is locked |
| 🌐 Web Locked | Internet access blocked |
| ⚙️ Program Locked | Application whitelist active |
| 📹 Recording | Screen recording in progress |
| 📡 Streaming | Remote View stream active |
| ✋ Hand Raised | Student has raised their hand |
| ⚡ Disconnected | LAN cable/Wi-Fi disconnected on student PC |
| ⬛ Blanked | Screen blanked |
