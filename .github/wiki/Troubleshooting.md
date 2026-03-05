# Troubleshooting

---

## Student PC not appearing in TADAdmin

**Symptom:** You've installed the client but the tile never appears in the grid.

### Step 1 — Check the service is running

On the student PC:
```powershell
Get-Service TADBridgeService
```
Expected: `Status = Running`

If stopped:
```powershell
Start-Service TADBridgeService
```

If the service fails to start, check the Event Log:
```powershell
Get-EventLog -LogName Application -Source "TADBridgeService" -Newest 20
```

### Step 2 — Check firewall rules

```powershell
Get-NetFirewallRule -DisplayName "TAD.RV*" | Select DisplayName, Enabled
```

Both rules should show `Enabled = True`. If missing:
```powershell
New-NetFirewallRule -DisplayName "TAD.RV Discovery" -Direction Inbound -Protocol UDP -LocalPort 17421 -Action Allow
New-NetFirewallRule -DisplayName "TAD.RV Control"   -Direction Inbound -Protocol TCP -LocalPort 17420 -Action Allow
```

### Step 3 — Verify multicast is working

Run this on the teacher's machine to capture discovery packets:
```powershell
# Listen for TAD.RV multicast beacons for 10 seconds
$client = New-Object System.Net.Sockets.UdpClient
$client.JoinMulticastGroup([System.Net.IPAddress]::Parse("239.1.1.1"))
$client.Client.Bind([System.Net.IPEndPoint]::new([System.Net.IPAddress]::Any, 17421))
$ep = [System.Net.IPEndPoint]::new([System.Net.IPAddress]::Any, 0)
$client.Client.ReceiveTimeout = 10000
try { $bytes = $client.Receive([ref]$ep); [System.Text.Encoding]::UTF8.GetString($bytes) } catch { "No beacon received" }
```

If no beacon is received:
- Check the student service is running
- Check both machines are on the same subnet (see [Network Requirements](Network-Requirements#multicast-across-vlans))
- Check for a managed switch filtering multicast — try connecting a direct cable

### Step 4 — Check the network adapter

TAD.RV discovers the student PC's IP by looking at the primary network adapter. If the PC has multiple adapters (e.g. Wi-Fi + VPN), the wrong IP may be advertised.

Check `HKLM\SOFTWARE\TAD_RV\LastIP` on the student — this is the IP being broadcast.

### Step 5 — Restart both sides

```powershell
# On student PC
Restart-Service TADBridgeService

# Then restart TADAdmin on teacher PC
```

---

## Video thumbnails are black / blank

**Symptom:** The tile appears but shows a black or blank preview.

### Possible causes

**A) H.264 decoder not available (no iGPU)**

TAD.RV uses the WebCodecs API for hardware H.264 decoding. Some PCs without an integrated GPU may not support the required codec profile.

Check the browser console in TADAdmin (press `F12` in the WebView2 window) for errors like `VideoDecoder: codec not supported`.

Fix: The service will fall back to JPEG snapshots — ensure `ScreenCaptureEngine` is in JPEG fallback mode (check Event Log for `QuickSync init failed`).

**B) Student PC has no display output**

If the student PC boots headless (rare in classrooms) or the display is off, DXGI Desktop Duplication may return empty frames. Ensure a monitor is connected or configure a virtual display.

**C) First frame hasn't arrived yet**

Wait 2–5 seconds. The first keyframe takes a moment. The placeholder icon disappears as soon as the first frame is rendered.

**D) Session 0 isolation**

If the student is not logged in (Windows is at the login screen), the service runs in Session 0 and cannot capture the login screen due to desktop isolation. This is by design — once the user logs in, thumbnails appear automatically.

---

## Remote View opens but shows the login screen / blank

The student is at the Windows login screen. TAD.RV cannot stream the login screen (Session 0 isolation is enforced by Windows — this is not a TAD.RV limitation). The stream will show the desktop once the user logs in.

---

## Remote View is choppy / high latency

### Check LAN bandwidth

The main-stream uses 2–4 Mbps per student. On a Wi-Fi connection (teacher or student), latency naturally increases. For best performance:
- Both teacher and student PCs should be on **wired Ethernet**
- Use a Gigabit switch

### Check iGPU availability

The H.264 encoder runs on the student's integrated GPU. If the iGPU is under heavy load or unavailable, the encoder falls back to software (slower, higher CPU).

Check Task Manager → Performance → GPU on the student PC during a Remote View session.

### Reduce resolution

Not directly configurable in this version. Future versions will support resolution selection.

---

## "Locked" badge stuck on a student

If a student's tile shows the locked badge but the screen is not actually locked:

1. Send an explicit **Unlock** command (right-click → Unlock)
2. If that fails, the service and dashboard may have lost sync — restart the student service:
   ```powershell
   Restart-Service TADBridgeService
   ```
   The tile will reconnect and its state will resync within seconds.

---

## Lock overlay not appearing on student screen

The lock overlay is launched in the user's desktop session via `CreateProcessAsUser`. This requires:
- A user to be logged in (Session > 0)
- The service has successfully resolved the active session ID

If the student is at the login screen, the lock overlay cannot be displayed (there is no interactive session to paint on).

If a user is logged in but the overlay doesn't appear:
- Check the Event Log on the student PC for errors from `TADBridgeService` source
- Ensure `TADOverlay.exe` is present in `C:\Program Files\TAD_RV\`

---

## Service fails to start — Event Log errors

### "Driver bridge initialization failed"

The kernel driver (`TAD.RV.sys`) is not loaded. In standard deployments, TAD.RV runs **without** the kernel driver — this error indicates the driver was expected but not found.

Fix: Either install the kernel driver (see [Kernel Install Guide](https://github.com/amiho-dev/TAD-RV/blob/main/docs/Kernel-Install-Guide.md)) or ensure the service is configured for user-mode only operation.

### "Port 17420 already in use"

Another process is using TCP port 17420. Find and terminate it:
```powershell
netstat -ano | findstr ":17420"
# Note the PID, then:
taskkill /PID <pid> /F
```

### "Access denied to registry key"

The service runs as `LocalSystem` which should have full registry access. If this error appears, the registry key may have incorrect permissions:
```powershell
$acl = Get-Acl "HKLM:\SOFTWARE\TAD_RV"
$acl.Access | Format-Table
```
Ensure `NT AUTHORITY\SYSTEM` has `FullControl`.

---

## TADAdmin crashes on launch

### WebView2 runtime not installed

TADAdmin requires the **WebView2 Evergreen Runtime**. The installer bundles and installs it, but if it was skipped:
- Download from: https://developer.microsoft.com/en-us/microsoft-edge/webview2/
- Install the Evergreen Standalone Installer

### "Access denied" launching TADAdmin

TADAdmin does not require administrator rights. If you see an access denied error, check that the install directory has correct permissions for the current user.

---

## Recording doesn't save / empty file

### Browser download blocked

WebView2 may block file downloads if the download path is restricted. Check:
- The user's Downloads folder exists and is writable
- No group policy blocks downloads in Edge/WebView2

### VP9 codec not available

TADAdmin tries VP9 then VP8 for recording. If neither is available in the WebView2 runtime, recording will fail with an error toast. Update WebView2 to the latest version.

---

## Endpoint stuck at "Connecting" forever

If a tile shows the connecting state (white dot) indefinitely:

1. The service is broadcasting but TADAdmin cannot establish the TCP connection
2. Check firewall allows TCP 17420 from the teacher's IP
3. Check there is no NAT between teacher and student (they must be on the same routed LAN)
4. Try adding the student IP manually: **Settings → Add endpoint manually**

---

## Getting Logs

### Student service logs (Windows Event Log)
```powershell
Get-EventLog -LogName Application -Source "TADBridgeService" -Newest 50 | Format-List
```

### TADAdmin debug output

Press `F12` inside TADAdmin to open the WebView2 DevTools console. Errors and debug messages from the dashboard JavaScript appear here.

### Export all TAD.RV events
```powershell
Get-EventLog -LogName Application -Source "TADBridgeService" |
  Export-Csv "$env:USERPROFILE\Desktop\TAD-Events.csv" -NoTypeInformation
```
