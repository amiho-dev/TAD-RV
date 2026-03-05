# FAQ

---

## General

### Does TAD.RV require a kernel driver?

No. The kernel driver (`TAD.RV.sys`) is **optional** and not installed by the standard setup. TAD.RV runs entirely in user mode — the Windows service, screen capture, remote view, and all controls work without any kernel driver.

The kernel driver adds tamper-resistant process protection and filesystem guards for high-security environments. See the [Kernel Install Guide](https://github.com/amiho-dev/TAD-RV/blob/main/docs/Kernel-Install-Guide.md) if you need it.

---

### Does TAD.RV require Active Directory / a domain?

No. TAD.RV works on workgroup (non-domain) machines. Active Directory integration is optional and adds:
- Group-based role assignment
- Automatic provisioning from `Policy.json` on the NETLOGON share
- OU-targeted GPO deployment

---

### Does TAD.RV work over Wi-Fi?

Yes, but with caveats:
- **Discovery**: UDP multicast works on most Wi-Fi networks, but some enterprise Wi-Fi controllers filter multicast between clients. If students don't appear, try adding them manually.
- **Remote View**: Works over Wi-Fi but may be choppy. For best quality, use wired Ethernet.
- **Controls (lock, message, etc.)**: Work fine over Wi-Fi — they are tiny TCP commands.

---

### How many students can TADAdmin handle?

TADAdmin is designed for classrooms of up to **50 simultaneous endpoints**. The grid scales dynamically. Performance beyond 50 depends on the teacher's PC hardware (mainly GPU decode power for thumbnails).

---

### What Windows versions are supported?

| OS | Supported |
|---|---|
| Windows 10 (21H2 and later) | ✅ |
| Windows 11 | ✅ |
| Windows Server 2019/2022 (admin machine) | ✅ |
| Windows 7 / 8 / 8.1 | ❌ |

---

### Is a .NET runtime required on student PCs?

No. The setup EXE is self-contained — it bundles the .NET 8 runtime. No separate installation is required on endpoints.

---

## Remote View

### Why does the Remote View start blurry then sharpen?

TADAdmin first connects the **sub-stream** (1fps 480p) — this appears immediately. After 1–2 seconds, the **main-stream** (30fps 720p) kicks in and the quality jumps. This two-phase startup is by design to minimize initial delay.

### Can I view multiple students simultaneously?

The current version supports one fullscreen Remote View at a time. All tiles show live 1fps thumbnails simultaneously — Remote View is for focused full-quality viewing. Multi-window RV is planned for a future release.

### Why can't I see the login screen?

Windows Session 0 isolation prevents applications from capturing the login screen. This is a Windows security feature, not a TAD.RV limitation. The stream works as soon as a user is logged in.

### Does the student know they're being watched?

The student sees a small 📡 streaming indicator badge on the teacher's dashboard. There is no notification shown on the **student's** screen during normal Remote View — this is by design for classroom monitoring.

---

## Recording

### Where are recordings saved?

Recordings save to the **teacher's Downloads folder** automatically when recording stops. Filename format: `TAD-Recording_{StudentName}_{timestamp}.webm`

### What format are recordings?

WebM container with VP9 video codec (or VP8 as fallback). These files play in any modern browser, VLC, or most video editors.

### Can I record all students at once?

Not in the current version. Recording is per-student. Planned for a future release.

### Is there a recording time limit?

No hard limit — recording continues until you stop it. File size grows at approximately 200–400 KB per second (dependent on screen activity and VP9 efficiency).

---

## Lock / Controls

### Can students unlock themselves?

No. The lock overlay is driven by the TADBridgeService running as LocalSystem — it cannot be dismissed by the user. Only an Unlock command from TADAdmin removes it.

### What happens if the teacher closes TADAdmin while students are locked?

The lock persists. TADBridgeService maintains lock state independently of the admin dashboard. To unlock, reopen TADAdmin and send Unlock, or run:
```powershell
# Emergency unlock on the student PC directly
Stop-Service TADBridgeService  # This releases the lock overlay
```

### Does Logoff/Reboot/Shutdown prompt the student?

No. These commands execute immediately without any confirmation dialog on the student's screen. Use with care.

---

## Privacy

### Can TAD.RV see passwords?

No. The PrivacyRedactor component detects password input fields (via Windows Accessibility APIs) and blacks out those screen regions before frames leave the student PC. The teacher never receives raw password pixels.

### Is screen data transmitted over the internet?

No. All communication is strictly on the local LAN between the teacher PC and student PCs. No data leaves your network.

### Is screen data stored anywhere?

Screen data is not stored except when you explicitly use the **Record** feature, which saves a file to the teacher's machine.

---

## Deployment

### Can I deploy silently without any user interaction?

Yes. Run `TADClientSetup.exe /silent` — it installs and starts the service with no UI.

### Can I deploy via SCCM / Intune?

Yes. The installer supports silent mode and produces standard Windows service registration. The command-line for SCCM:
```
TADClientSetup.exe /silent /norestart
```

### How do I update student endpoints?

TAD.RV includes a self-update mechanism (`TAD-Update.exe`). When a new version is available on GitHub Releases, the service downloads and self-updates in the background. This can be disabled via registry if you prefer manual rollouts.

---

## Technical

### What GPU is required for H.264 encoding?

TAD.RV uses Intel QuickSync via the Media Foundation Transform API. Supported hardware:
- Intel 6th Gen (Skylake) and later integrated graphics
- UHD 600/700 series (recommended)

AMD and NVIDIA are not supported for encoding. On non-Intel systems, the service falls back to JPEG snapshots.

### Does TAD.RV work in virtual machines?

The screen capture works, but H.264 hardware encoding requires GPU pass-through. In a standard VM (Hyper-V, VMware without GPU pass-through), the service falls back to JPEG mode automatically.

### What is the TAD.RV kernel driver for?

The optional kernel driver (`TAD.RV.sys`) adds:
- Tamper protection — prevents the service process from being killed by students
- File system protection — prevents service executables from being deleted
- Heartbeat watchdog — re-locks if the service is somehow stopped

These protections are relevant in environments where students have local admin rights or use bypass tools. In managed standard-user environments, the kernel driver is not needed.
