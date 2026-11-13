# UI behavior and qualification

The Windows Forms app uses one window with internal pages. Home exposes Personalizzazione, Gaming, Rete, Manutenzione, Stampanti and Strumenti avanzati. The established dark palette, typography, native dialogs and keyboard navigation are retained.

## State and confirmation

Back/Alt+Left preserves the earlier page controls and results. Returning does not rerun actions. Diagnostics cancel and finish before leaving; modifying actions must reach their outcome. Cancel is the default confirmation action.

Personalization offers explicit apply/show/hide choices and restore, with state, scope and recovery warnings. The desktop settings are independent and recover their compound values together. Gaming levels are cumulative checklists with per-action controls; guided settings remain incomplete, even after opening Windows. Hardware-dependent features are checked in Windows rather than inferred as available.

Keep-awake shows active/inactive/error and expiry. Its request survives page navigation and ends when the app closes. WinRM starts in an unverified state and never changes configuration on page entry. Verification and modifications require the local elevated worker.

## Qualification

UI tests use the actual Windows Forms message loop with synthetic identities, registry values, power plans and network interfaces. Render fixtures cover the new pages and existing local operations at simulated 100/150/200% scaling. Simulation is not physical-DPI or OS-matrix qualification.

The current checks do not establish clean-VM behavior of real desktop changes, power/animation writes, firewall rules or external WinRM authentication. Those changes must be qualified on dedicated Windows 10/11 machines. Historical Italian notes and retired enterprise captures describe previous revisions only.
