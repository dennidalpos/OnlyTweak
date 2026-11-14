# UI behavior and qualification

The Windows Forms app uses one window with internal pages. Home exposes Personalizzazione, Gaming, Rete, Manutenzione, Stampanti and Strumenti avanzati. The established dark palette, typography, native dialogs and keyboard navigation are retained.

## Area colors and navigation

Each home entry has a dark colored surface, coordinated icon and border, and a directional chevron. Personalizzazione uses violet, Gaming pink, Rete blue, Manutenzione green, Stampanti gold and Strumenti avanzati orange. Labels and distinct icons identify areas independently of color. The repeated app eyebrow has been removed to give content more space.

Area identity is explicit, independent of translated titles, and follows internal navigation into details, diagnostic parameters and operations. Comodità and Mantieni sveglio belong to Personalizzazione; Spooler belongs to Stampanti; WinRM and Protezioni belong to Strumenti avanzati. Session history is neutral. Back restores the previous page and its colors without recreating its controls. Dynamically added gaming controls receive the same palette.

Navigation buttons retain their area color during hover and press, with a visible keyboard focus indicator. Disabled controls use neutral surfaces. Primary execution buttons retain their separate emphasis; warnings and confirmations retain their warning color and explicit text. Area color does not represent success, failure or safety.

The home grid switches to one column when the available width is below 760 logical pixels (scaled with the font), allowing scrolling at the minimum window size. Card heights account for icon space, the chevron and wrapped descriptions. Existing actions and navigation paths are preserved.

## State and confirmation

Back/Alt+Left preserves the earlier page controls and results. Returning does not rerun actions. Diagnostics cancel and finish before leaving; modifying actions must reach their outcome. Cancel is the default confirmation action.

Personalization offers explicit apply/show/hide choices and restore, with state, scope and recovery warnings. The desktop settings are independent and recover their compound values together. Gaming levels are cumulative checklists with per-action controls; guided settings remain incomplete, even after opening Windows. Hardware-dependent features are checked in Windows rather than inferred as available.

Keep-awake shows active/inactive/error and expiry. Its request survives page navigation and ends when the app closes. WinRM starts in an unverified state and never changes configuration on page entry. Verification and modifications require the local elevated worker.

## Qualification

UI tests use the actual Windows Forms message loop with synthetic identities, registry values, power plans and network interfaces. Render fixtures cover the new pages and existing local operations at simulated 100/150/200% scaling. Simulation is not physical-DPI or OS-matrix qualification.

Area checks cover inheritance, dynamic controls, neutral history, confirmation colors, disabled states and keyboard reachability at the minimum window size. Computed contrast checks require 4.5:1 for normal text and 3:1 for icons and focus indicators across normal, hover and pressed area surfaces. The minimum secondary-text contrast across the palettes is 6.16:1. Minimum-size home captures include both the start and end of the scroll area.

Render runs use a stable SystemAware DPI context and scale the host geometry and fonts together, preventing cross-monitor DPI changes from rescaling the synthetic fixtures. `--render-only` runs just these captures. Runs without render flags retain PerMonitorV2, as does the production app. Neither test mode replaces physical per-monitor DPI qualification.

The current checks do not establish clean-VM behavior of real desktop changes, power/animation writes, firewall rules or external WinRM authentication. Those changes must be qualified on dedicated Windows 10/11 machines. Historical Italian notes and retired enterprise captures describe previous revisions only.
