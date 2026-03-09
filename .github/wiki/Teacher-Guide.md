# Teacher Guide

The **Teacher Dashboard** provides centralized monitoring and control over student PCs.

## New Dashboard Features

Starting with recent updates, you can now view advanced system information directly from the web dashboard:
- **Operating System Version**: Verified natively.
- **CPU Model**: Pulled via WMI for active hardware monitoring.

## Safety Measures

If a student removes the LAN cable to bypass controls, the dashboard will immediately show a toast notification: `Warning: Dashboard cannot reach the network (LAN cable removed?)`

## Locking and Unlocking

Use the provided locks to keep students focused:
- **Web-Lock**: Disables all browser access natively through the driver filter.
- **Program-Lock**: Prevents launching any unauthorized user-mode applications.
