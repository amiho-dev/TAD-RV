# Changelog

## v26.3.08.130 (Latest)
- **Massive Storage Cleanup**: Removed leftover binaries and optimized git tracking. 
- **Bare Minimum Enforcement**: Installation now strictly requires Windows 10 or newer, and a multi-core processor. An error dialog (`:( Oh no! Your PC is not supported by TAD-RV anymore!`) will appear on unsupported hardware.
- **Windows Update Enforcement**: Added a system check that prevents setup if the Windows Update list is not empty. Your OS must be fully updated.
- **GUI Installer**: Transitioned from a CLI setup tool to a modern WinForms wizard interface.
- **Dashboard Enhancements**: Added physical OS and CPU model monitoring to the web interface.
- **Licensing Restrictions**: Updated the `LICENSE` to allow forks but deny re-releasing or renaming without explicit permission. Special thanks to `f-rakete`.
- **Demo Mode Fixes**: Completed the underlying architecture for Web-Lock, Program-Lock, and Logoff/Reboot commands.
