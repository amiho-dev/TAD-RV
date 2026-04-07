# Installation & Requirements

## Minimum Requirements

- OS: Windows 10 or Windows 11
- CPU: at least 2 cores
- Disk: at least 100 MB free
- System state: no pending critical Windows updates

## Recommended GUI Rollout

1. Start TADDomainController.
2. Open the Deploy page.
3. Enter service path and target install path.
4. Optionally enable:
   - USB block for student endpoints
   - Update push after deployment
5. Select Deploy Now.
6. Review the deployment log.

## Post-Install Validation

- Service status is healthy on the dashboard.
- Managed clients appear in the Admin grid.
- A test command (for example Message or Lock) is applied successfully.

## If Something Fails

- Confirm Admin and clients are in the same network segment.
- Check firewall rules for discovery and control channels.
- Refresh operational logs in Domain Controller and inspect recent events.
