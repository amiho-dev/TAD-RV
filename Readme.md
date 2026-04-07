# TAD-RV

TAD-RV is a Windows platform for classroom and endpoint management.
It has three primary components:

- TADAdmin: live grid, remote view, lock controls, messaging, filtering
- TADDomainController: deployment, policy operations, update orchestration, operational logs
- TADBridgeService: endpoint service that receives commands and reports status

## Current Highlights

- Unified release naming: v26.04.XX.XXX
- In-place updater across all editions (Admin, Domain Controller, Client Service)
- Critical patch support through force-update markers
- Admin dashboard additions: game filter controls and settings modal
- Domain Controller additions: one-click USB policy toggle, update queueing, operational log refresh
- Improved privacy redaction fallback for login/password windows in remote view

## GUI-First Operations

Daily operations are designed around the UI, not CLI workflows:

- Deployment through the Domain Controller Deploy page
- Policy operations through toggles and guided actions
- Update visibility and critical update enforcement from in-app flows
- Troubleshooting through status cards and integrated logs

## Update Streams

Two update streams are intended:

- Stable: standard production stream (v26.04.XX.XXX)
- Beta-LTS: slower-moving stability-focused stream for sensitive environments

Recommended usage:

- Standard school environments: Stable
- Exam periods / long-term lab environments: Beta-LTS after pilot validation

## Compatibility (Short)

- Fully supported: Windows 10/11 x64, 2+ CPU cores
- Limited: Windows 11 ARM64 via x64 emulation
- LTS-only: legacy hardware and legacy OS lines
- Not supported: single-core and obsolete platforms

## Documentation

- docs/Architecture.md: plain-language architecture overview
- docs/Deployment-Guide.md: rollout and operational flow
- docs/Teacher-Guide.md: Admin/Teacher workflows
- docs/Console-Guide.md: Domain Controller workflows
- .github/wiki/: quick operational references and release policy pages

## License

Proprietary. All rights reserved. (C) 2026 TAD Europe
