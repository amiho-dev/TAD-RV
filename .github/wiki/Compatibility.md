# Comprehensive Compatibility Matrix

This document outlines the detailed hardware and software support status for **TAD-RV** (`v26.3.08.130+`). Devices that do not meet the minimum criteria will be actively blocked by the installer.

### 🌐 Global Prerequisites
Before consulting the matrix, ensure your systems meet these baseline checks:
- **Memory**: 4 GB RAM minimum (8 GB+ recommended).
- **Storage**: 100 MB free space (SSD highly recommended to avoid IO bottlenecks during network syncs).
- **Network**: Stable TCP/IP connection (WLAN/LAN). Removing LAN triggers a dashboard drop-off alert.
- **System State**: Windows Updates **MUST** be completely empty (`IsInstalled=0 and IsHidden=0`) or the setup will abort.

### 📊 All-In-One Compatibility Matrix

| Hardware / CPU Family | Core Count | Target Operating System | Status | Additional Notes |
| :--- | :--- | :--- | :--- | :--- |
| **Modern Intel / AMD**<br>*(Intel Gen 8+ / Ryzen Zen 2+)* | 4+ Cores | **Windows 11** *(Full)*<br>**Windows 10** *(22H2)* | 🟢 **Optimal** | - Full support for advanced containerized Program-Lock.<br>- Overhead from teacher surveillance is unnoticeable.<br>- Native WMI and toast alerts fully active. |
| **Mid-Age Intel / AMD**<br>*(Intel Gen 2-7 / AMD FX)* | 2+ Cores | **Windows 10** *(22H2)*<br>**Windows 10 LTSC** *(2019/2021)* | 🟢 **Supported** | - Fully working native components.<br>- Mild performance spikes possible during full remote lockouts.<br>- WinForms installer might render slightly slower. |
| **ARM64 Devices**<br>*(Snapdragon / Apple Silicon VM)* | Multi-Core | **Windows 11 ARM64** | 🟡 **Emulated** | - Runs under Windows 11 x64 emulation translation layer.<br>- Kernel driver requires strict manual certificate signing. |
| **Legacy Hardware**<br>*(Any Dual-Core+)* | 2+ Cores | **Windows 8.1 / 8**<br>**Windows 7 SP1** | 🟡 **LTS Only** | - Restricted to `.NET` legacy framework builds.<br>- Lacks modern APIs for toast notifications (`LAN disconnected`).<br>- Limited WMI hardware polling properties. |
| **Single-Core Devices**<br>*(Pentium 4, old Celerons)* | 1 Core | *Any Windows Version* | 🔴 **Blocked** | - Hard-blocked by installation logic: `Environment.ProcessorCount < 2`.<br>- Setup throws `:( Oh no! Your PC is not supported` |
| **Obsolete Operating Systems** | *Any* | **Windows Vista, XP, or older** | 🔴 **Blocked** | - Hard-blocked by modern .NET API dependencies and root driver requirements. |
