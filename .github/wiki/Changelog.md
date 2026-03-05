# Changelog

All notable changes to TAD.RV are documented here.
For full release notes and download links see the [GitHub Releases](https://github.com/amiho-dev/TAD-RV/releases) page.

---

## v26.3.04.127 — Live Stream UX + View Modes

**Released:** 2026-03-04

### Bug Fixes
- **Fixed:** Closing the Remote View modal no longer sends `rv_stop` — grid thumbnails stay live after closing RV

### New Features
- **Watch button** — one-click eye icon on each tile opens Remote View directly
- **Play overlay** — clicking the video preview in a tile opens fullscreen Remote View
- **View mode toggle** — switch between Thumbnail view (large tiles, prominent video) and List view (compact rows for high-density monitoring)

### i18n
- All 7 language packs updated (EN/DE/FR/NL/ES/IT/PL) with new view mode keys

---

## v26.3.04.126 — Screen Recording + Auto Sub-Stream

**Released:** 2026-03-04

### New Features
- **Screen recording** — record any student's screen as WebM VP9 video via right-click context menu or the RV modal header button
- **Auto sub-stream** — TADAdmin automatically requests a 1fps H.264 live thumbnail stream from every student on first connection; grid tiles show live decoded video instead of periodic JPEG snapshots
- **Recording indicators** — red pulsing border and "Recording" badge on active recording tiles
- **RV modal record button** — start/stop recording directly from the fullscreen Remote View modal
- Recordings auto-save as `TAD-Recording_{name}_{timestamp}.webm` to the teacher's Downloads folder

### Changes
- JPEG snapshot fallback interval extended from 15s to 30s (live sub-stream makes frequent snapshots redundant)

### i18n
- All 7 languages: added `ctx.record`, `ctx.stopRecord`, `ind.recording`, `toast.recordStarted/Saved/Failed/NoCanvas`

---

## v26.3.04.125 — Lock Screen Fix + Logoff/Reboot/Shutdown

**Released:** 2026-02-28

### Bug Fixes
- **Fixed:** Lock overlay was invisible — `LaunchInUserSession()` used `SW_HIDE`; corrected to `SW_SHOW` so the overlay is visible on all monitors
- **Fixed:** "Send Message" in context menu was broadcasting to all students instead of the individual right-clicked student

### New Features
- **Logoff / Reboot / Shutdown** commands in the right-click context menu
- **Multi-monitor lock** — overlay covers all attached monitors using `WH_KEYBOARD_LL` hook
- **LAN disconnect detection** — network disconnected indicator badge appears when student loses LAN connectivity
- **Freeze → Lock** — freeze function merged into Lock for a cleaner UX

### i18n
- All 7 languages updated with new command keys

---

## v26.3.04.124 — Program Lock

**Released:** 2026-02-21

### New Features
- Per-student **Program Lock** — blocks unauthorized executables using process creation monitoring
- Program Lock indicator badge on tiles
- Context menu toggle for Program Lock
- Toolbar **Program Lock All** button

---

## v26.3.04.123 — Web Lock

**Released:** 2026-02-14

### New Features
- Per-student **Web Lock** — blocks internet access individually
- Web Lock indicator badge
- Context menu toggle
- Toolbar **Web Lock All** button

---

## v26.3.04.122 — Hand Raise

**Released:** 2026-02-07

### New Features
- Student-side **Hand Raise** button in system tray
- Hand raise indicator badge and tile highlight in TADAdmin
- Teacher can lower hand via context menu

---

## v26.3.04.121 — Internationalization

**Released:** 2026-01-31

### New Features
- Full i18n system in all three components
- 7 language packs: EN, DE, FR, NL, ES, IT, PL
- Language switcher in toolbar
- All UI strings externalized to lang pack JS modules

---

## v26.3.04.120 — Initial Release

**Released:** 2026-01-15

### Features
- Live student grid with JPEG thumbnail snapshots
- Remote View (H.264 dual-stream: 1fps sub + 30fps main)
- Lock / Unlock (individual and all)
- Blank Screen
- Broadcast message
- Zero-config UDP multicast discovery
- WebView2 dashboard with GitHub-dark theme
- TADDomainController management console
- TADBridgeService Windows agent
- Demo mode (12 simulated students)
