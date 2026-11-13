# Security Policy

## Supported versions

Only the latest commit on the `master` branch is supported.

## Reporting a vulnerability

Please do not open a public issue for security problems.

Use GitHub's private vulnerability reporting: open the **Security** tab of this repository and choose **Report a vulnerability**. Include the affected component (UI, local elevated worker, checkpoints, embedded diagnostic or WinRM scripts), reproduction steps and the impact you observed. Remove real identities, device names and addresses from any evidence you attach.

You can expect an acknowledgement within a few days. Confirmed issues are fixed on `master` and disclosed after a fix is available.

## Scope notes

- The main UI uses the desktop identity; privileged local actions use a separate worker with a closed catalog. Windows ACLs remain the authorization boundary.
- Local audits and user checkpoints live under `%LOCALAPPDATA%\OnlyTweak`; the administrator WinRM journal lives under `%PROGRAMDATA%\OnlyTweak\LocalRemoting`. No customer credentials are acquired or saved.
- Existing historical customer data is neither imported nor removed. Logs and copied output may contain personal/device information; review before sharing.
- Checkpoint hashes and local logs are not immutable protection against the same user or an administrator.
- WinRM is an opt-in local receiver restricted to an app-owned administrator endpoint, private interface and subnet. Existing conflicting configuration is preserved by refusing the operation. End-to-end remote authentication requires separate qualification.
- Gaming presets do not disable Windows security protections, updates or virtualization.
