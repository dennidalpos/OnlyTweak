# Local architecture and sources

The main process runs as the desktop user. A separate elevated process accepts only catalogued local action IDs over a bounded named pipe. Both sides verify peer process IDs; the worker verifies the caller executable. The pipe denies network identities. Network logon, customer profiles, AD/JEA/GPO/RSAT operations and remote-worker requests are absent.

## Local operations

ActionSpec connects previews, applicability, native execution and observations. LocalOperationExecutor records the request before execution and the final outcome afterward. Incomplete pre-reads block changes; unavailable final audit, truncated output or uncertain effects remain unverified. A complete reread is required before retrying an uncertain action.

Registry settings preserve original value type/absence, created-key ownership and machine/user identity. Desktop icon settings capture both desktop branches. API preferences have separate persistent checkpoints. Checksums detect accidental corruption, not malicious rewriting by the same user or an administrator. Keep-awake is scoped to a dedicated thread and releases its execution-state request in finally.

The PowerShell bridge extracts embedded script bytes without changing encoding or line endings, sends a bounded typed request through stdin, validates structured results and never bypasses execution policy. Only LocalDiagnostics.ps1 and LocalRemoting.ps1 are bundled. Diagnostic scripts can time out or be cancelled; modifying scripts are allowed to finish and report uncertainty.

WinRM uses a dedicated named endpoint and an address-specific listener. Its firewall opening is the last configuration mutation. Its protected journal records each completed stage. Conflicting unmanaged configuration and external changes are preserved by refusing the operation. The inspect action does not prove remote authentication or an actual external connection.

## Primary references

- [Microsoft: desktop icons](https://support.microsoft.com/en-us/windows/experience/personalization/customize-the-desktop-icons-in-windows)
- [Microsoft: PC performance](https://support.microsoft.com/en-us/windows/experience/performance-optimization/tips-to-improve-pc-performance-in-windows)
- [Microsoft: windowed-game optimizations](https://support.microsoft.com/en-us/windows/hardware/display-graphics/optimizations-for-windowed-games-in-windows-11)
- [Microsoft: clipboard](https://support.microsoft.com/en-us/windows/apps/using-the-clipboard)
- [SystemParametersInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow)
- [PowerSetActiveScheme](https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powersetactivescheme)
- [SetThreadExecutionState](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-setthreadexecutionstate)
- [PowerToys Awake](https://learn.microsoft.com/en-us/windows/powertoys/awake)
- [WSMan provider](https://learn.microsoft.com/en-us/powershell/module/microsoft.wsman.management/about/about_wsman_provider?view=powershell-5.1)
- [Register-PSSessionConfiguration](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/register-pssessionconfiguration?view=powershell-5.1)
- [Enable-PSRemoting and its wider side effects](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/enable-psremoting?view=powershell-5.1)
- [Disable-PSRemoting and incomplete reversal](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/disable-psremoting?view=powershell-7.6)

These sources explain Windows features and APIs. They do not certify every registry workaround, driver combination or Windows build. Classic-menu and Explorer registry behavior require visual VM qualification.

## Evidence and historical material

The custom test harness uses fake backends and synthetic PowerShell host functions; live read-only checks are separately requested. Clean-VM writes, real WinRM connections and physical-DPI checks remain separate qualification work. No administrative changes are performed on the development host by the tests.

[Retired enterprise guide](history/ENTERPRISE-2026-11-13.md) and the dated Italian reference pages are historical material, not current feature specifications.
