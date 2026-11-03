# Security Policy

## Supported versions

Only the latest commit on the `master` branch is supported.

## Reporting a vulnerability

Please do not open a public issue for security problems.

Use GitHub's private vulnerability reporting: open the **Security** tab of this repository and choose **Report a vulnerability**. Include the affected component (UI, elevated worker, Active Directory operations, remote preparation scripts), reproduction steps and the impact you observed. Remove real identities, domain names and addresses from any evidence you attach.

You can expect an acknowledgement within a few days. Confirmed issues are fixed on `master` and disclosed after a fix is available.

## Scope notes

- The app acts with the permissions of the connected identity; Windows and Active Directory ACLs and delegations remain the authorization boundary.
- Profiles and audit files are stored under `%LOCALAPPDATA%\OnlyTweak` without passwords; new passwords are protected with DPAPI during local transport.
- Reports and audit output contain enterprise data and must be handled according to the customer's policy.
- Dependencies are monitored with Dependabot and secret scanning with push protection.
