# OnlyTweak user guide

Use this guide to find features in the Italian interface and understand what happens when you run them. For setup and a first walkthrough, see the [README](../README.md#quick-start).

## Personalization

Open **Personalizzazione**. Find the setting and read its current configuration and scope. Choose the relevant **Applica** button, then read the preview before selecting **Conferma**. **Annulla** is the default action.

Desktop icons can be selected independently: Questo PC, Cestino, Rete, File utente and Pannello di controllo. Both desktop registry branches are checkpointed together, preserving different original values and types. Windows themes and the desktop's own “show icons” choice can affect the final appearance.

Explorer options include file extensions, hidden items, the start page, selection checkboxes and full title-bar path. Windows 11 also offers the unofficial classic-menu workaround. Check the result in Explorer or on the desktop after the app reports the new setting. If the change is not visible, sign out and back in. Explorer stays open during the operation.

**Ripristina precedente** restores the captured values, including absence and original registry types. It stops if another tool changed the values or a prior operation left an incomplete checkpoint. Keep the checkpoint and inspect the issue; do not force repeated writes.

## Convenience and keep-awake

From **Personalizzazione**, open **Comodità e mantieni sveglio**. Choose **Apri impostazioni** on a clipboard history, suggestions or notifications entry to open the relevant Windows page. Make and verify your choice in Windows. The OnlyTweak entry stays marked **Da completare nelle impostazioni Windows**. Clipboard cloud synchronization stays under your control in Windows.

Open **Mantieni sveglio** on the same page. Choose 15 or 30 minutes, or 1, 2, 4 or 8 hours. Select **Mantieni acceso anche lo schermo** if you want to keep the display on. Choose **Avvia** and confirm to start or replace a session. The request continues while you navigate and ends when the timer expires, you confirm **Termina**, or the app closes. Windows still controls deliberate sleep, lid actions and device policy.

## Gaming

Open **Gaming** and choose a level. Each level includes the entries from the previous levels. Choose individual actions in the checklist.

| Level | Tasks |
|---|---|
| **Base** | Game Mode, recording in the background, monitor refresh rate, driver/update review |
| **Bilanciato** (Balanced) | Base plus animation reduction, an existing power plan, startup-app review |
| **Avanzato** (Advanced) | All previous tasks plus per-game GPU, windowed optimizations, HAGS and VRR where supported |

Guided entries remain **Da completare nelle impostazioni Windows**. Hardware and driver support must be checked there. Windowed optimizations are a Windows 11 feature for the relevant DirectX 10/11 presentation modes; they are not a universal gaming switch.

Automatic animation changes preserve the original client/menu/combo-animation settings. Power-plan selection preserves the original active plan and does not create or delete plans. Restore is available per operation; external changes or missing original plans block automatic recovery.

Compare the same game, scene and graphics settings before/after one change. Observe fluidity, heat, noise and stability. The app has no FPS benchmark and promises no gain. Security protections, updates, virtualization and Windows packages remain untouched by the gaming path.

## Local tools

In **Rete**, open **Ping**, **Risoluzione DNS** or **Test porta TCP**. Enter a fully qualified DNS name. For DNS, choose **A**, **AAAA** or **CNAME**. For TCP, enter a port from 1 to 65535. **Avvia diagnosi** runs the check. Use **Annulla diagnosi** to stop it. A negative result describes the selected check from the current PC.

**Ripristino rete** contains separate actions for the DNS cache, Winsock and TCP/IP. Winsock and TCP/IP resets can interrupt connectivity and require a manual restart. Before resetting TCP/IP, record any static IP, gateway and DNS settings you may need to re-enter.

In **Manutenzione → Verifica e riparazione**, choose **SFC · verifica** or **DISM · analizza componenti** to check Windows. Repairs have separate buttons and a confirmation. Let a repair finish and read the tool's summary.

In **Stampanti**, select a printer and choose **Apri coda** to view its jobs. **Annulla lavori…** targets that printer's jobs. **Spooler… → Svuota tutte le code locali** permanently removes local spool jobs for all users.

## Local PowerShell Remoting / WinRM

Open **Strumenti avanzati → PowerShell Remoting / WinRM**. Merely opening the page changes nothing. **Verifica** reads the local configuration through an elevated worker.

Select an interface, then **Abilita sulla LAN privata**. The worker requires a private connection profile, one eligible IPv4 address and an active private firewall with blocked unsolicited inbound traffic. Existing listeners, an existing OnlyTweak endpoint/rule, broad WinRM ingress or an active recovery checkpoint cause a conflict instead of being overwritten.

The app starts WinRM, binds an HTTP listener to the selected address, registers **OnlyTweak.Local** with an administrators/SYSTEM descriptor, and finally enables its dedicated private-subnet firewall rule on TCP 5985. It never calls the broad Enable-PSRemoting setup, changes client trust/authentication, enables every endpoint or opens a public-network rule. Workgroup client trust, remote UAC/account behavior and end-to-end authentication are separate concerns; this page is not a client connection manager.

**Ripristina precedente** compares the live configuration with the recorded state, removes only the recorded app-owned rule/endpoint/listener, and restores the original service state/start mode. External changes block recovery. A journaled partial operation can be undone when its live state matches; an unjournaled partial change requires manual review. Checkpoints are retained after restore.

Actual administrative behavior, firewall isolation and authentication must be qualified in dedicated Windows VMs. Fixture success is not live WinRM qualification.

## Data and recovery

User audit/checkpoints: `%LOCALAPPDATA%\OnlyTweak`. Administrator WinRM checkpoint: `%PROGRAMDATA%\OnlyTweak\LocalRemoting`. Existing customer-profile data is not loaded, imported or removed. Local logs may reveal device/user information; review before sharing.

**Indietro** or **Alt+Left** returns to the previous page without rerunning an action. Leaving a running diagnostic requests cancellation and waits for it to finish. Let a modifying operation reach its outcome before navigating away. If a retry is blocked because the outcome is uncertain, use **Rileggi stato** and review the reread before trying again.

See the [UI screenshots](UI-UX.md#screenshots), [technical details and sources](TECHNICAL.md), [security](../SECURITY.md) and [license](../LICENSE).
