# OnlyTweak user guide

[Italian reference](GUIDE.it.md)

OnlyTweak is a portable Windows technician toolkit with an Italian interface, separate customer sessions, AD/workstation diagnostics, guided interventions and RSAT preparation. Current evidence is local fixtures and simulated environments. Real-domain, clean-VM, signing and pilot qualifications remain separate; this guide does not authorize those operations.

## First use

Use the supplied self-contained x64 OnlyTweakWindows.exe. The UI does not require elevation on startup; privileged local actions use a separate elevated process. Check the current artifact manifest instead of treating historical package sizes as a requirement.

1. Open **Postazione tecnico** and inspect Windows PowerShell 5.1, modules and consoles.
2. Inventory RSAT and install only the required AD/GPO/DNS/DFS/DHCP components from the approved corporate source or compatible local FoD folder. Windows Update/WSUS policy is not loosened.
3. Under **Ambienti aziendali**, configure customer DNS domain, explicit DC, allowed FQDN targets and reviewed ordinary-user scope.
4. Connect using the current identity or session-only network credentials from the Windows dialog. Starting a process does not validate the password; verify target authentication.
5. Select a destination and procedure, inspect evidence and limits, then confirm any intended intervention.
6. Export JSON/HTML only after reviewing content and destination.

The single-window navigation preserves page state. Back/Alt+Left returns to the previous page; diagnostics must cancel and finish before leaving, while modifying actions must reach their outcome. Returning does not rerun an action. Customer sessions persist until explicit disconnect, profile change or application close. Native credential/file dialogs and external Microsoft tools keep their own identity/lifecycle.

## Local tweaks and diagnostics

The four HKCU tweaks control Windows 11 classic context menu, file extensions, hidden items and Explorer start location. Preview/confirmation, pre-change audit, checkpoint and reread are required. The classic menu is an unofficial workaround; a registry reread does not prove its visual effect on every Windows build.

Restore uses machine/SID-bound checkpoints with original type/value/absence and ownership of created keys. Incomplete, changed or externally modified state needs review; no automatic reset or Explorer restart occurs. Checksums/ACLs are not immutable protection against the same user or an administrator. Old 1.x checkpoints are not automatically imported.

Network technical diagnostics include four-request ping, DNS A/AAAA/CNAME, TCP-port checks, connections/PIDs, routes and WinHTTP proxy. Windows tools add identity/groups and user gpresult. Inputs are typed and allowlisted; normal diagnostics have timeout/cancellation and bounded output. They use the app's local identity, not customer-worker credentials. Missing modules, partial reads, negative tests and confirmed failures are distinct outcomes.

## Enterprise workflows

| Area | Supported behavior |
|---|---|
| Common problems | Domain/DC, DNS, authentication/time, AD replication, GPO, SYSVOL and workstation signals |
| Technician/RSAT | Component/module/policy inventory, selective preparation and external Microsoft consoles |
| AD | Paged user/group/computer search, cross-DC comparison and selected ordinary-user actions |
| DFS/replication | Partitions/partners, SMB namespaces/targets, directional DFSR samples, SYSVOL/NETLOGON |
| Workstation | Computer policy, DNS cache, spooler, local member secure channel, network reset, SFC/DISM |
| Remote preparation | Export Inspect/WhatIf/Apply/Undo, GPO instructions and JEA module; no implicit remote setup |

Queries validate the selected DC identity/domain and retain the actual server in results. There is no silent fallback to another DC. Target changes clear prior search/selection; user writes remain bound to the profile's explicit DC.

Independent diagnostic collections preserve successful reads when another source fails. Empty events do not establish health. Partial collection remains inconclusive, while a directly observed issue remains visible. Update JEA modules through the approved channel; do not infer availability from an older contract.

For interventions distinguish accepted request, completed execution and observed effect. Native success/reboot codes alone do not prove repair. After uncertainty use **Rileggi stato** before retrying. Diagnostic cancellation targets its own process tree; modifying repairs are not generically force-killed or rolled back.

## Data and permissions

Profiles/audit/checkpoints live under LocalAppData/OnlyTweak. Reports can contain enterprise identities, names, addresses and paths. No automatic telemetry is provided; follow the customer's retention and sharing policy.

Only fixed catalog operations are exposed. There is no free shell, downloaded script or bypass of ExecutionPolicy/App Control/AppLocker. Windows/AD delegation remains the authorization boundary. Use reviewed ordinary-user OUs and exclude protected/service/custom privileged accounts; detection cannot identify every service account without SPN. New passwords use protected local transport, but managed-memory copies cannot be universally excluded.

Spool cleanup is limited to SPL/SHD at the first level of the verified standard spool path. Windows protection controls are read-only. Local audit is not immutable and does not replace central logging.

## Compatibility and verification

The technician app targets Windows 10 build 17763+ and Windows 11 x64 Intel/AMD. Server host, ARM64 and x86 are excluded. Actual support lifecycle and enterprise qualification are separate from technical startup. Planned remote targets are Server 2019/2022/2025; 2016 requires separate qualification.

WinRM uses Kerberos, optionally valid HTTPS. No automatic CredSSP/Basic/unencrypted/wildcard TrustedHosts fallback exists. Secure-channel repair is local on the affected member with appropriate identity/elevation, not a DC or a remote second-hop operation.

~~~bat
build.cmd
test.cmd --artifact --ui --read-only --render
~~~

Development needs Windows x64, .NET 10 SDK, PowerShell 7.4+ and dependency access. These public commands write repository-local dist/artifacts; managed agent runs use a verified isolated runtime copy. UI/render flags need an interactive desktop; simulated scaling is not physical DPI qualification. read-only adds host reads, not real AD access. GitHub Actions is currently disabled despite the retained workflow.

prepare-release.ps1 creates a local bundle/receipt with hashes, resolved packages/runtime and Authenticode state. Sign embedded scripts before building. Changed source/assets or externally changed executable invalidate an earlier receipt. RequireSigned checks actual bundled bytes; it does not install, publish or qualify an environment. Current unsigned artifacts remain for lab qualification.

See [architecture](ENTERPRISE.md), [UI behavior](UI-UX.md), [security](../SECURITY.md), and the proprietary [license](../LICENSE).
