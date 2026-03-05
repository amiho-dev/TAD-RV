# Installation

This page covers installing TAD.RV in three scenarios:
- [Quick test on a single machine](#single-machine-test)
- [Manual install on a few endpoints](#manual-endpoint-install)
- [Silent GPO deployment to many endpoints](#gpo-silent-deployment)

---

## Downloads

All installers are on the [Releases](https://github.com/amiho-dev/TAD-RV/releases) page:

| Installer | Purpose |
|---|---|
| `TADClientSetup-vX.X.X-win-x64.exe` | Endpoint agent — install on every student/managed PC |
| `TADAdminSetup-vX.X.X-win-x64.exe` | Teacher/admin dashboard — install on teacher PC |
| `TADDomainControllerSetup-vX.X.X-win-x64.exe` | IT management console — install on IT admin machine |

---

## Single-Machine Test

To try TAD.RV on one machine (teacher and student on the same PC):

1. Run `TADClientSetup.exe` — installs the background service
2. Run `TADAdminSetup.exe` — installs the admin dashboard
3. Open `TADAdmin.exe` — it will discover the local service and show the machine in the grid

> The service starts automatically. No reboot required.

---

## Manual Endpoint Install

For a small number of student PCs:

### Step 1 — Install on each endpoint

Run `TADClientSetup.exe` with administrator rights on each student PC. The installer:

1. Copies `TADBridgeService.exe` to `C:\Program Files\TAD_RV\`
2. Registers the Windows service with auto-start and recovery policy
3. Starts the service immediately

### Step 2 — Install the admin dashboard

Run `TADAdminSetup.exe` on the teacher's machine.

### Step 3 — Launch TADAdmin

Open `TADAdmin.exe`. All online student PCs on the same LAN will appear in the grid within seconds.

---

## GPO Silent Deployment

For large environments, use Group Policy to push the service silently.

### Prerequisites

- Active Directory domain
- A network share accessible from all endpoints (e.g. `\\DC\NETLOGON\TAD\`)
- `TADClientSetup.exe` copied to the share

### Create the GPO

1. Open **Group Policy Management**
2. Create a new GPO on the target OU (e.g. `Computers\Labs`)
3. Edit the GPO → **Computer Configuration** → **Policies** → **Windows Settings** → **Scripts (Startup)**
4. Add a new startup script:
   - Script path: `\\DC\NETLOGON\TAD\TADClientSetup.exe`
   - Parameters: `/silent`
5. Link the GPO to the target OU

### Startup Script Alternative

If you prefer a PowerShell script, TAD.RV ships `tools/Scripts/Deploy-TadRV.ps1`:

```powershell
# Deploy-TadRV.ps1 — copy and register the service silently
\\DC\NETLOGON\TAD\Deploy-TadRV.ps1 -ServiceBinaryPath "\\DC\NETLOGON\TAD\TADBridgeService.exe"
```

The script handles:
- Copying the binary to `C:\Program Files\TAD_RV\`
- Registering the service (`sc create`)
- Setting recovery policy (restart on failure: 5s / 10s / 30s)
- Starting the service

---

## Verifying the Installation

### On the endpoint

```powershell
# Check service status
Get-Service TADBridgeService

# Should show: Running
```

```powershell
# Check Event Log
Get-EventLog -LogName Application -Source "TADBridgeService" -Newest 10
```

### On the admin dashboard

1. Open `TADAdmin.exe`
2. The newly installed endpoint should appear in the grid within ~10 seconds
3. The status dot will be green (online)

---

## Uninstalling

### Via Control Panel

Go to **Settings → Apps** and uninstall **TAD.RV Client** or **TAD.RV Admin**.

### Manual removal

```powershell
# Stop and remove service
sc stop TADBridgeService
sc delete TADBridgeService

# Remove files
Remove-Item "C:\Program Files\TAD_RV" -Recurse -Force

# Remove registry
Remove-Item "HKLM:\SOFTWARE\TAD_RV" -Recurse
```

---

## Firewall Rules

TAD.RV creates two Windows Firewall rules automatically during installation:

| Rule | Direction | Protocol | Port |
|---|---|---|---|
| TAD.RV Discovery | Inbound | UDP | 17421 |
| TAD.RV Control | Inbound | TCP | 17420 |

If your endpoints use a third-party firewall, add equivalent allow rules for these ports.

See [Network Requirements](Network-Requirements) for the full network reference.

---

## Next Steps

- [Admin Dashboard Guide](Admin-Dashboard) — learn to use the live view, Remote View, and controls
- [Troubleshooting](Troubleshooting) — if an endpoint doesn't appear in the grid
