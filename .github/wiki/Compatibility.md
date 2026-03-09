# Comprehensive Compatibility Matrix

This document outlines the detailed hardware and software support status for **TAD-RV** (`v26.3.08.130+`). Devices that do not meet the minimum criteria will be actively blocked by the installer.

### 💻 Operating Systems

| OS Version & Edition | Architecture | Status | Additional info |
| :--- | :--- | :--- | :--- |
| **Windows 11** (24H2) | `x64`, `ARM64` | 🟢 **Optimal** | - Full support for advanced containerized Program-Lock<br>- Native WMI and toast alerts |
| **Windows 11** (21H2, 22H2, 23H2) | `x64`, `ARM64` | 🟢 **Supported** | - Standard behavior, fully tested |
| **Windows 10** (22H2) | `x86`, `x64` | 🟢 **Supported** | - **Bare Minimum OS** for current branches |
| **Windows 10 LTSC** (2019 / 2021) | `x86`, `x64` | 🟢 **Supported** | - Ideal for permanent and stable school deployments |
| **Windows 8.1 / 8** | `x86`, `x64` | 🟡 **LTS Only** | - Restricted to `.NET` legacy framework builds<br>- Extremely limited WMI hardware polling properties |
| **Windows 7** SP1 | `x86`, `x64` | 🟡 **LTS Only** | - Reaching end-of-life status<br>- Lacks modern toast notification (`LAN disconnected`) APIs |
| **Windows Vista & XP** | `x86` | 🔴 **Blocked** | - Hard-blocked by modern API dependencies |

<br>

### 🖨️ Processors (CPU)

| Architecture / Family | Core Count | Status | Additional info |
| :--- | :--- | :--- | :--- |
| **Intel Core (Gen 8 - Gen 14)** | 4+ Cores | 🟢 **Optimal** | - Overhead from teacher surveillance is practically unnoticeable |
| **AMD Ryzen (Zen 2 - Zen 4)** | 4+ Cores | 🟢 **Optimal** | - Excellent multi-threading for background driver tasks |
| **Intel Core (Gen 2 - Gen 7)** | 2+ Cores | 🟢 **Supported** | - Fully working, mild performance spikes during full remote lockouts |
| **AMD FX / Older Athlon** | 2+ Cores | 🟢 **Supported** | - Setup succeeds, but WinForms installer might run slower |
| **ARM64** *(Snapdragon / Apple VM)* | Multi-Core | 🟡 **Emulated**| - Runs under Windows 11 x64 emulation translation layer<br>- Kernel driver requires careful signed installation |
| **Single-Core** *(Pentium 4, etc.)* | 1 Core | 🔴 **Blocked** | - Hard-blocked by logic: `Environment.ProcessorCount < 2`<br>- Throws `:( Oh no! Your PC is not supported` |

<br>

### 💾 Memory & Storage

| Component | Minimum Spec | Recommended | Additional info |
| :--- | :--- | :--- | :--- |
| **RAM** | 4 GB | 8 GB+ | - Windows 10 base usage leaves enough headroom for TAD-RV Services |
| **Storage Type**| HDD (5400 RPM)| SSD / NVMe | - SSD heavily recommended to avoid IO bottlenecks during network syncs |
| **Storage Space**| 100 MB free | 500 MB+ free | - The `v130` framework is natively deployed.<br>- Massively optimized following deep git cleanup |

<br>

### ⚙️ System State Requirements

| Subsystem | Identifier | Status | Additional info |
| :--- | :--- | :--- | :--- |
| **Windows Updates** | `WUApiLib` / COM | 🔴 **Strict** | - Local list **MUST** be empty (`IsInstalled=0 and IsHidden=0`)<br>- Setup aborts if pendings exist |
| **Network Link** | `TCP / IP` | 🔴 **Strict** | - Hard-wired or stable WLAN required<br>- UI triggers **Warning: Dashboard cannot reach the network** if cable is pulled |
