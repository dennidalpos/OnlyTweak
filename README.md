# OnlyTweakWindows

Portable Windows utility for private PC users: desktop and Explorer personalization, gaming configuration, network diagnostics, maintenance and printing. Italian Windows Forms interface, C# / .NET 10, self-contained x64 executable.

> Local fixture and UI checks are development evidence. Clean-VM testing of real changes, physical DPI, signing and external WinRM connections require separate qualification. An unsigned bundle is intended for lab evaluation.

## Quick start

1. Build from this checkout as described below, then open `dist/OnlyTweakWindows.exe`. The .NET runtime is included. The main window does not require elevation.
2. Open **Personalizzazione** for desktop icons and Explorer tweaks, or **Gaming** for a progressive configuration checklist.
3. Read the current state and preview before confirming a change. **Ripristina precedente** restores the captured configuration, rather than assumed Windows defaults.
4. **Apri impostazioni** opens a guided Windows setting. It remains marked as requiring completion; opening a page does not apply or verify the setting.

## Common tasks

- **Personalizzazione:** show/hide This PC, Recycle Bin, Network, User Files and Control Panel independently; Windows 11 classic context menu; extensions, hidden files, Explorer start page, selection checkboxes and full title-bar path.
- **Comodità:** guided clipboard history, Windows suggestions and app notifications. Cloud clipboard synchronization is never enabled automatically.
- **Mantieni sveglio:** choose 15 minutes to 8 hours and optionally keep the display on. The temporary request ends at expiry or app exit without changing the power plan.
- **Gaming:** three cumulative levels, with each action selected individually. Base covers Game Mode, background recording, display refresh and drivers. Balanced adds reversible animation reduction, existing power-plan selection and startup-app review. Advanced guides per-game GPU, windowed-game optimizations, HAGS and VRR when available.
- **Rete:** configuration, ping, DNS A/AAAA/CNAME, TCP port tests, connections, routes, proxy and explicit network resets.
- **Manutenzione / Stampanti:** Windows tools, SFC/DISM, storage settings, printer queues and spooler operations.
- **Strumenti avanzati:** local Windows tools, read-only protection information and local PowerShell Remoting / WinRM enablement.

There is no customer-profile workspace, domain management, RSAT/JEA provisioning or remote-administration client.

## Configuration and data

Local audit files, registry checkpoints and preference checkpoints live under `%LOCALAPPDATA%\OnlyTweak`. WinRM recovery state is protected for administrators and SYSTEM under `%PROGRAMDATA%\OnlyTweak\LocalRemoting`. Existing customer data and historical checkpoints are left in place; they are not imported or removed.

Changes use a fixed operation catalog, confirmation, pre-change audit and rereading. Privileged local work uses a separate elevated worker. There is no free shell, downloaded script, telemetry or ExecutionPolicy bypass.

## Limits and recovery

- Registry and API rereads verify configuration, not every visible effect or gaming performance. No FPS improvement is guaranteed.
- The Windows 11 classic menu is an unofficial, build-dependent workaround. Explorer is not restarted automatically; sign-out may be needed.
- Restore stops on incomplete checkpoints, unexpected values or external configuration changes. Local audit is not immutable.
- Gaming does not disable Defender, firewall, updates, memory integrity or virtualization. It does not debloat Windows, remove packages or change speculative timer settings.
- Power plans may affect battery life, heat and fan noise. Only plans already available on the PC can be selected.
- Spool cleanup permanently removes all local print jobs, including other users' jobs. Network resets may interrupt connectivity and need a manual reboot. An SFC/DISM exit code alone does not prove repair.
- WinRM is an advanced local receiver, using endpoint **OnlyTweak.Local**, administrators only, TCP 5985 on the selected private IPv4 interface and subnet. Conflicting existing configurations, public networks, ambiguous addresses and external changes block automatic modification or restore. Client authentication and end-to-end connectivity must be tested separately. See the [guide](docs/GUIDE.md).

## Compatibility

The startup check accepts Windows 10 build 17763+ and Windows 11 on native Intel/AMD x64. Server hosts, ARM64 and x86 are rejected. Individual controls are limited by Windows build and installed components; hardware-dependent graphics options remain guided checks. Technical startup compatibility does not establish OS servicing status or clean-VM qualification.

## Build and test

Requirements: Windows x64, a stable .NET 10 SDK compatible with `global.json` (10.0.100 with `latestFeature` roll-forward), PowerShell 7.4+ (`pwsh` on PATH), Windows PowerShell 5.1 and NuGet access.

Run from the repository root in Command Prompt:

```bat
build.cmd
test.cmd --artifact --ui --read-only --render
```

Build publishes `dist/OnlyTweakWindows.exe` with a hashed receipt at `artifacts/build-evidence/publish.json`. The custom executable harness uses synthetic backends and isolated PowerShell fixtures. `--artifact` checks the published bundle, so build first; `--ui` and `--render` need an interactive desktop. `--read-only` adds real host reads. Tests do not apply administrative changes to the development PC.

For any permitted redistribution, keep the companion LICENSE, CREDITS.md, NOTICE.md and `licenses/` directory with the executable. Build and local delivery include them; the executable alone is not the complete redistribution package. [CREDITS](CREDITS.md) records the component licenses and Windows-runtime terms requiring owner/legal review.

Build/test/render outputs stay in `dist/` and `artifacts/`. Restore may update `packages.lock.json` for the selected SDK; review that change separately.

Prepare a local delivery bundle after a successful build:

```powershell
pwsh -NoProfile -File scripts/prepare-release.ps1
```

Output: `artifacts/delivery/2.0.0/`, including hashes, resolved dependencies/runtime and Authenticode status. No upload occurs. Without signing parameters it packages the unsigned build. `-RequireSigned` requires valid signatures on the executable and embedded scripts; sign scripts before building. Changed inputs or executable bytes invalidate the build receipt.

## Help and documentation

- [User guide](docs/GUIDE.md)
- [Architecture and primary sources](docs/TECHNICAL.md)
- [UI behavior and qualification](docs/UI-UX.md)

For a reproducible issue, provide the Windows build, action and observed result. Remove personal names, addresses and other private data from shared evidence. Historical enterprise and Italian documents are retained as history, not current feature guides.

## Security and license

See [SECURITY.md](SECURITY.md) for vulnerability reporting and [LICENSE](LICENSE) for the proprietary, all-rights-reserved license. Third-party dependencies retain their own licenses.
