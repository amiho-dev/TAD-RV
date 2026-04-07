# Compatibility Matrix

This table summarizes practical support status for production operations.

| Platform | Status | Recommended Stream | Notes |
| --- | --- | --- | --- |
| Windows 11 x64 with modern Intel/AMD CPU | Optimal | Stable | Best performance for remote view and filtering |
| Windows 10 x64 with 2+ cores | Supported | Stable | Suitable for standard school deployments |
| Windows 11 ARM64 (x64 emulation) | Limited | Stable after pilot | Validate in pilot classrooms first |
| Legacy hardware with legacy OS lines | LTS-only | Beta-LTS | Basic capability only, reduced feature scope |
| Single-core or obsolete systems | Unsupported | None | Installation should be blocked |

## Stream Rules

- Stable: regular feature and bug-fix flow
- Beta-LTS: slower change velocity and longer validation windows

## Decision Guidance

- Prefer Beta-LTS when stability outweighs new features.
- Prefer Stable when fast feature delivery is needed.
