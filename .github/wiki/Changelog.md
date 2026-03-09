# Changelog

## v26.3.10.131 (Latest)
- **Feature Finalization**: Concluded synchronization for all Teacher Dashboard actions, bridging front-end context menu items to the TCP background services.
- **Remote Execution Tools**: Added `Launch App / CMD` and `Launch URL` natively into the Teacher Dashboard. Teachers can now push web pages or remote initiate applications explicitly to student instances in a single click.
- **Dashboard UI Update**: Implemented Javascript action bindings mapping the Teacher Dashboard commands directly to native `System.Diagnostics.Process` calls via the WebMessage listener.
- **Release Schedule Bump**: Pushed baseline dependencies forward marking the official March 10th milestone release.


## v26.3.10.131 (Latest)
- **Feature Finalization**: Concluded synchronization for all Teacher Dashboard actions, bridging front-end context menu items to the TCP background services.
- **Remote Execution Tools**: Added `Launch App / CMD` and `Launch URL` natively into the Teacher Dashboard. Teachers can now push web pages or remote initiate applications explicitly to student instances in a single click.
- **Dashboard UI Update**: Implemented Javascript action bindings mapping the Teacher Dashboard commands directly to native `System.Diagnostics.Process` calls via the WebMessage listener.
- **Release Schedule Bump**: Pushed baseline dependencies forward marking the official March 10th milestone release.


## v26.3.08.130
- **Massive Storage Cleanup**: Removed leftover binaries and optimized git tracking. 
- **Bare Minimum Enforcement**: Installation now strictly requires Windows 10 or newer, and a multi-core processor. An error dialog (`:( Oh no! Your PC is not supported by TAD-RV anymore!`) will appear on unsupported hardware.
- **Windows Update Enforcement**: Added a system check that prevents setup if the Windows Update list is not empty. Your OS must be fully updated.
- **GUI Installer**: Transitioned from a CLI setup tool to a modern WinForms wizard interface.
- **Dashboard Enhancements**: Added physical OS and CPU model monitoring to the web interface.
- **Licensing Restrictions**: Updated the `LICENSE` to allow forks but deny re-releasing or renaming without explicit permission. Special thanks to `f-rakete`.
- **Demo Mode Fixes**: Completed the underlying architecture for Web-Lock, Program-Lock, and Logoff/Reboot commands.
