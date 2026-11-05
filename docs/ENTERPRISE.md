# Enterprise architecture and qualification

[Preserved Italian technical reference](ENTERPRISE.it.md) · [User guide](GUIDE.md)

## Processes and contracts

The Windows Forms UI runs asInvoker. .NET 10 implements catalog/validation/storage/audit and asynchronous coordination; Windows/RSAT modules execute in Windows PowerShell 5.1. No proprietary target-side service is installed.

An immutable session copy of EnvironmentProfile identifies customer, DNS domain, DC, allowed FQDNs, allowed/excluded OUs, protected SIDs, reviewed ordinary-user scope and WinRM/JEA mode. Reconfiguration requires reconnection. Profiles are data, not code.

ActionDefinition fixes IDs, allowed typed parameters, risk, privilege, timeout and primary-source reference. Runbooks compose permitted reads and stop after failed preliminary binding. Results preserve target/profile/identity, authentication state, status, timing, bounded evidence and truncation.

Each customer uses a separate worker process. Alternate credentials use LOGON_NETCREDENTIALS_ONLY; process startup does not authenticate a target. Passwords are not command-line/file arguments. Named-pipe transport checks ACLs, peer identity and executable path, refuses Network SID and caps a message at 2 MiB. Elevated workers accept only the closed RSAT/local-maintenance catalog.

Embedded PowerShell resources run with explicit executable/module selection and JSON stdin. Output/error budgets are bounded. A crashed process can leave a private lease requiring review, not indiscriminate cleanup.

## Evidence and intervention safety

Success means the requested read/execution completed, not general host health. ObservedIssue retains a confirmed signal. Inconclusive/Unavailable distinguish insufficient evidence/access/modules. Unverified changes require reread, and Cancelled is not rollback.

Independent domain.auth/domain.sysvol/endpoint.health collections retain completed subresults. Checks statuses distinguish Completed, Empty, NotApplicable, Denied, Unavailable, TimedOut and Failed with sanitized error codes. Truncation, malformed/duplicate/missing fields or unknown state cannot be upgraded to Success.

LocalOperationExecutor records RequestAccepted, ExecutionCompleted, EffectVerified and AuditComplete separately, including native exit information. SFC/DISM/reset may remain Unverified despite a zero or reboot-required code. The UI blocks retry until an explicit successful reread; a reread alone does not prove repair. Diagnostics have cancellation/timeouts; changes finish without generic process-tree termination.

Standard diagnostics normally use 60 seconds; SFC verify/DISM scan use the longer implemented 1,800-second limit. Audit precedes a write. A final audit/transport failure keeps an uncertain result. Profiles/audit use current-user/SYSTEM ACLs; local records are not immutable. Voluntary reports encode HTML and require content/destination review.

## AD and replication

Bind/query explicitly target the selected DC and verify naming context, DNS hostname, DC identity and profile domain. Aliases, members, mismatched domains and unverified targets are refused without silent fallback. Changing target invalidates previous selection.

LDAP filters are escaped. Search is capped at 1,000 objects with UI pages of 50; repeated page queries are not a transactional directory snapshot. LastLogonDate is approximate and lockout is observed on the selected DC.

User changes reread GUID/DN, reviewed ordinary-user OU scope and eligibility. Protected/admin/service/SPN/special-SID cases are refused where detectable; customer review must also exclude non-SPN services and custom privileged roles. Windows/AD permissions remain authoritative and cannot eliminate read/write races.

Password reset changes only the intended action, does not test login or restore the previous password, and checks pwdLastSet on the selected DC. Local transport uses DPAPI CurrentUser and clears native buffers where possible; WinForms may leave managed-memory copies.

Replication covers explicit partitions/partners and repadmin observations. DCDiag uses the selected limited tests; no forced syncall/KCC/authoritative repair is performed. Access/network/protocol limits produce inconclusive outcomes.

## GPO, DFS and endpoints

Computer GPO combines target RSOP/gpresult and technician-side AD/SYSVOL version reads. User-policy evidence must come from that user's session. Native gpresult requires explicit zero exit and nonempty output; localized wording is not a generic success parser. Independent evidence survives partial failure.

SYSVOL/NETLOGON SMB observations differ from AD replication. DFS shows configured targets/referrals and technician access, not another client's actual referral path. Out-of-profile targets are not contacted. DFSR uses two directional samples three seconds apart and at most 100 files; a nonzero sample alone is not a fault, and unavailable language-independent totals remain null with original evidence.

Endpoint changes refuse DCs/unknown machine role, require confirmation/audit and do not reboot implicitly. Secure-channel repair is local to the affected member, avoiding a remote second hop.

## Remote preparation and recovery

Exported Provision-Remoting defaults to Inspect; WhatIf/Apply/Undo are explicit administration actions. It needs a preexisting approved distribution/access channel.

Apply checks domain membership, explicit CIDRs, dedicated group and listener/GPO conflicts before writes, repeating preflight after confirmation. It creates the allowlisted JEA role/endpoint, restricted Domain firewall rule and transcripts. HTTPS checks exact host, private key, EKU, validity and trust. Existing listeners are not silently replaced.

No TrustedHosts/Basic/CredSSP/AllowUnencrypted relaxation occurs. A narrow new rule does not close older broad exposure. DCs are refused for the virtual-account endpoint.

Machine-bound schema-2 checkpoints record generations/hashes under ProgramData with administrator/SYSTEM ACLs. Undo validates current artifacts before removing only its own recorded state; snapshots/transcripts remain. A verified Undo permits a later Apply to preserve its prior generation in History. Active/partial/altered/foreign/legacy state needs manual recovery; no migration/reset/deletion is automatic. Checksums are integrity checks, not administrator-proof history.

## Qualification boundary

Sign scripts before building for AllSigned/App Control environments. Fixtures and local readonly checks do not qualify domain/DFS infrastructure, clean client machines, physical DPI, real printers, signed policy or enterprise pilot. Keep OT-ENT-LAB/OT-ENT-SIGN/OT-ENT-PILOT and host-VM tasks open until their specific evidence exists.

The retained Italian reference preserves dated source consultations and prior technical receipts. They are not a fresh validation of every Microsoft policy/version. Current sources are the [project](../OnlyTweakWindows.csproj), [enterprise scripts](../scripts/enterprise/), [tests](../tests/) and their pinned dependencies.
