# OnlyTweakWindows

Portable toolkit for technicians and administrators of Windows enterprise environments: Active Directory and workstation diagnostics, guided interventions, per-customer profiles and RSAT setup. Windows Forms app with an Italian interface, delivered as a single self-contained x64 executable (.NET 10 embedded, no runtime to install).

> **Status:** verified locally with fixtures and simulated domains only. No tests against real domains or clean Windows VMs have been run, and the current build is **unsigned**, intended for lab qualification. Build and fixture results do not replace environment qualification.

## Features

- **Technician workstation**: check Windows PowerShell 5.1, modules and consoles; inventory and selectively install RSAT components (AD DS/LDS, GPO, DNS, DFS, DHCP) from the corporate source or a local FoD folder, leaving WSUS/Windows Update policies untouched.
- **Customer environments**: separate profile and session per customer, with DNS domain, explicit domain controller and allowed FQDN targets; connect with the current identity or session-only network credentials.
- **Active Directory**: paged search of users, groups and computers, object comparison across DCs, account status, unlock, enable/disable and password reset for individual ordinary users, always against the explicitly selected DC.
- **Common problems**: domain/DC, DNS, time and authentication, AD replication, GPO, SYSVOL and workstation health, with partial results kept when a sub-check fails.
- **DFS and replication**: AD replication across partitions and partners, SMB namespaces and targets, DFSR backlog samples, SYSVOL/NETLOGON.
- **Workstation interventions**: computer policy refresh, DNS cache, spooler, domain secure channel, network reset, SFC/DISM, with reads before and after plus audit.
- **Local tweaks**: classic context menu on Windows 11, file extensions, hidden items and Explorer start location; each change shows a preview, asks for confirmation, writes only to HKCU and can be undone from a persistent checkpoint.
- **Network diagnostics**: ping, DNS A/AAAA/CNAME, TCP port test, connections and PIDs, routing and WinHTTP proxy; targets are validated and commands have a 60 s timeout and cancellation.
- **Remote preparation**: export Inspect/WhatIf/Apply/Undo scripts, GPO instructions and a JEA module; nothing is configured remotely by default.
- **Reports**: voluntary JSON/HTML export after previewing content and destination.

## Safety model

- Closed catalog of operations: no free shell, no downloaded scripts, no bypass of ExecutionPolicy, App Control or AppLocker.
- Changes require confirmation, a pre-change audit and a re-read of the result; uncertain outcomes call for a re-read before retrying.
- Windows and AD ACLs and delegations remain the authorization boundary; use delegated accounts and suitable privileged workstations.
- No telemetry. Reports and audit files contain enterprise data (identities, names, addresses, paths) and must be handled per the customer's policy.

## Compatibility

- Technician host: Windows 10 (build 17763 or later) and Windows 11, x64 Intel/AMD. Servers, ARM64 and x86 are not supported by the app.
- Remote servers: Windows Server 2019, 2022 and 2025 (2016 to be qualified separately). WinRM uses Kerberos, optionally HTTPS; there is no automatic fallback to CredSSP, Basic, unencrypted traffic or wildcard TrustedHosts.

## Build and test

Requirements: .NET 10 SDK, PowerShell 7.4+ (`pwsh` on `PATH`), Windows x64 and NuGet access.

```bat
build.cmd
test.cmd --artifact --ui --read-only --render
```

`build.cmd` publishes `dist/OnlyTweakWindows.exe` and records a hashed receipt in `artifacts/build-evidence/publish.json`. The test bench uses fixtures and never contacts real AD; `--ui` and `--render` need an interactive desktop. To prepare a delivery bundle with a manifest of hashes, packages and Authenticode status:

```powershell
pwsh -NoProfile -File scripts/prepare-release.ps1
```

The repository contains a CI workflow, but GitHub Actions are disabled: build and test locally.

## Documentation

Documentation is written in Italian.

- [Full guide](docs/GUIDE.it.md): first use, local tweaks, enterprise functions, security, compatibility, build and delivery.
- [Technical documentation and Microsoft sources](docs/ENTERPRISE.md)
- [UI/UX history](docs/UI-UX.md)

## Security

See [SECURITY.md](SECURITY.md) to report a vulnerability.

## License

Proprietary, all rights reserved. See [LICENSE](LICENSE). Third-party dependencies keep their own licenses.
