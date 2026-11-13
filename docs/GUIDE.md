# OnlyTweak user guide

OnlyTweak is a portable utility for your own Windows PC. The UI is in Italian. Start with the [README](../README.md) for build and first use. Domain/customer administration is not part of this edition. [Preserved Italian history](GUIDE.it.md) describes an older edition.

## Personalization

Open **Personalizzazione**. Each setting shows its current configuration, user scope and recovery choices. Choose **Applica · …**, inspect the preview, then confirm. Cancel is the default confirmation action.

Desktop icons can be selected independently: Questo PC, Cestino, Rete, File utente and Pannello di controllo. Both desktop registry branches are checkpointed together, preserving different original values and types. Windows themes and the desktop's own “show icons” choice can affect the final appearance.

Explorer options include file extensions, hidden items, the start page, selection checkboxes and full title-bar path. Windows 11 also offers the unofficial classic-menu workaround. Rereading registry values does not prove the visual result. Sign out if needed; the app does not restart Explorer automatically.

**Ripristina precedente** restores the captured values, including absence and original registry types. It stops if another tool changed the values or a prior operation left an incomplete checkpoint. Keep the checkpoint and inspect the issue; do not force repeated writes.

## Convenience and keep-awake

**Comodità e mantieni sveglio** contains guided Windows settings for clipboard history, suggestions and app notifications. Opening Windows does not mark a task completed. Clipboard cloud synchronization is not activated by OnlyTweak.

**Mantieni sveglio** offers 15/30 minutes or 1/2/4/8 hours, with an optional display-on request. Starting replaces the app's earlier request after confirmation. The session continues while you navigate and ends on expiry, **Termina**, or app exit. It does not change the power plan or prevent deliberate sleep, lid actions or every device policy.

## Gaming

The level selector shows a cumulative checklist, not an automatic “boost” button. Every operation remains an explicit choice.

| Level | Tasks |
|---|---|
| Base | Game Mode, recording in the background, monitor refresh rate, driver/update review |
| Balanced | Base plus animation reduction, an existing power plan, startup-app review |
| Advanced | All previous tasks plus per-game GPU, windowed optimizations, HAGS and VRR where supported |

Guided entries remain **Da completare nelle impostazioni Windows**. Hardware and driver support must be checked there. Windowed optimizations are a Windows 11 feature for the relevant DirectX 10/11 presentation modes; they are not a universal gaming switch.

Automatic animation changes preserve the original client/menu/combo-animation settings. Power-plan selection preserves the original active plan and does not create or delete plans. Restore is available per operation; external changes or missing original plans block automatic recovery.

Compare the same game, scene and graphics settings before/after one change. Observe fluidity, heat, noise and stability. The app has no FPS benchmark and promises no gain. Security protections, updates, virtualization and Windows packages remain untouched by the gaming path.

## Local tools

**Rete** contains typed diagnostics and separately confirmed resets. A negative ping/TCP result alone does not prove a general failure. A missing command, incomplete output, cancellation and an observed negative result remain distinct.

**Manutenzione** exposes Windows maintenance tools. Repairs can take time and are not force-cancelled as if they were read-only diagnostics. Network resets can interrupt connectivity and require a manual restart.

**Stampanti** shows local/connected queues. Purging one selected queue differs from cleaning every local spool job. Global cleanup permanently affects jobs from all users; read the confirmation.

## Local PowerShell Remoting / WinRM

Open **Strumenti avanzati → PowerShell Remoting / WinRM**. Merely opening the page changes nothing. **Verifica** reads the local configuration through an elevated worker.

Select an interface, then **Abilita sulla LAN privata**. The worker requires a private connection profile, one eligible IPv4 address and an active private firewall with blocked unsolicited inbound traffic. Existing listeners, an existing OnlyTweak endpoint/rule, broad WinRM ingress or an active recovery checkpoint cause a conflict instead of being overwritten.

The app starts WinRM, binds an HTTP listener to the selected address, registers **OnlyTweak.Local** with an administrators/SYSTEM descriptor, and finally enables its dedicated private-subnet firewall rule on TCP 5985. It never calls the broad Enable-PSRemoting setup, changes client trust/authentication, enables every endpoint or opens a public-network rule. Workgroup client trust, remote UAC/account behavior and end-to-end authentication are separate concerns; this page is not a client connection manager.

**Ripristina precedente** compares the live configuration with the recorded state, removes only the recorded app-owned rule/endpoint/listener, and restores the original service state/start mode. External changes block recovery. A journaled partial operation can be undone when its live state matches; an unjournaled partial change requires manual review. Checkpoints are retained after restore.

Actual administrative behavior, firewall isolation and authentication must be qualified in dedicated Windows VMs. Fixture success is not live WinRM qualification.

## Data and recovery

User audit/checkpoints: `%LOCALAPPDATA%\OnlyTweak`. Administrator WinRM checkpoint: `%PROGRAMDATA%\OnlyTweak\LocalRemoting`. Existing customer-profile data is not loaded, imported or removed. Local logs may reveal device/user information; review before sharing.

Back/Alt+Left returns without rerunning an action. Leaving a diagnostic cancels it and waits; modifying operations must reach their outcome. **Rileggi stato** is required after uncertainty before retrying; rereading alone does not prove repair.

See [technical details and sources](TECHNICAL.md), [UI qualification](UI-UX.md), [security](../SECURITY.md) and [license](../LICENSE).
