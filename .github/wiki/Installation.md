# Installation & Requirements

## System Requirements

As of version `v26.3.08.130`, the minimum system requirements have changed:

- **OS**: Windows 10 or later (Bare Minimum)
- **CPU**: Multi-core processor (2 cores or more)
- **Storage**: At least 100MB of free space
- **Windows Updates**: Must be fully up to date! The setup will **block** installation if your Windows Update list is not empty.

> **Why Windows 10 minimum?**
> Legacy OS versions lack modern security and API features required by our kernel driver and web dashboard components.

## Running the Setup

The setup is now provided as a convenient GUI (Graphical User Interface).

1. Download the latest `TADSetup.exe` release.
2. Run it as Administrator.
3. The Setup wizard will check your hardware and Windows Update status. If there are pending updates, it will prompt you: `Your Windows Update list is not empty.` You must install them and reboot before proceeding.
4. Follow the setup wizard to deploy **Console**, **Teacher**, or **Driver Service** components.
