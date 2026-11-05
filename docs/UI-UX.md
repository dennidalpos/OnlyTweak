# UI behavior and qualification

[Preserved Italian history](UI-UX.it.md) · [User guide](GUIDE.md)

The current Windows Forms interface uses one window containing UserControl pages. Home exposes eight sections; an internal enterprise index leads to the enterprise pages. Back/Alt+Left preserves page controls, selection, filters, scroll position and results. The technician route opens Technician/RSAT directly.

Customer sessions remain explicit until disconnect, profile change or application close. Native credentials/file/folder dialogs and external system tools retain their own windows and identities.

## State and confirmation

Profile editors, password entry, action confirmation, parameters, output, previews and history use the internal navigation. Cancel is the default confirmation action. Sensitive fields are cleared when their use ends. Unsaved edits ask before leaving.

Back cancels cancellable diagnostics and waits for completion. Modifying actions block navigation until their outcome. Returning to a page never reruns an operation.

The tweak page shows the four HKCU options, current state, preview/confirmation and persistent restore. Network/Windows technical diagnostics use typed inputs and separate negative, incomplete and unavailable outcomes. Windows protection settings remain read-only.

## Evidence and limits

The UI fixture bench uses a real message loop with simulated operations. Its renders cover home, enterprise pages, confirmations, editors, tweaks and diagnostic parameters, including simulated 100/150/200% scaling. Those outputs are fixtures, not screenshots proving operation on every Windows build or physical monitor.

Real interactive/DPI qualification remains OT-VM-02; local tweak/diagnostic VM qualification remains OT-VM-TWEAK-01. The archived 1.x renders and dated Italian notes describe their own revision only. Do not present them as current UI captures or infer live test success from a render.

No new screenshots or fabricated interface images are added by this documentation update.
