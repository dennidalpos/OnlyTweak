# Windows PowerShell 5.1. Fixed operations; request data is never evaluated as code.
# Official sources and privileges: EnterpriseCatalog.cs and docs/ENTERPRISE.md.
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding
$OutputEncoding = [Console]::OutputEncoding
$r = $null
$evidence = New-Object 'System.Collections.Generic.List[string]'
$data = @()
$state = 'Success'
$summary = 'Raccolta evidenze completata; non certifica la salute complessiva.'
$authenticated = $false
$errorCode = ''
function Note([string]$text) { if ($evidence.Count -lt 100) { $evidence.Add($text) } }
function Require-Module([string]$name) {
    $path = Join-Path $env:windir ("System32\WindowsPowerShell\v1.0\Modules\{0}\{0}.psd1" -f $name)
    if (-not (Test-Path -LiteralPath $path)) { throw "ModuleUnavailable:$name" }
    Import-Module -Name $path -ErrorAction Stop
}
function Native([string]$name, [string[]]$arguments) {
    $path = Join-Path $env:windir "System32\$name"
    if (-not (Test-Path -LiteralPath $path)) { throw 'NativeToolUnavailable' }
    $output = @(& $path @arguments 2>&1 | ForEach-Object { "$_" })
    $code = $LASTEXITCODE
    if ($output.Count -gt 300) { $output = @('[Campione: prime 300 righe]') + $output[0..299] }
    [pscustomobject]@{ Tool=$name; ExitCode=$code; Output=$output }
}
function Verify-Directory {
    Require-Module 'ActiveDirectory'
    $root = Get-ADRootDSE -Server $r.Profile.DomainController -AuthType Negotiate
    $expected = ($r.Profile.Domain.Split('.') | ForEach-Object { 'DC=' + $_ }) -join ','
    if ($root.defaultNamingContext -ine $expected) { throw 'DomainIdentityMismatch' }
    $script:authenticated = $true
    Note ('Bind AD riuscito su ' + $r.Profile.DomainController + '. Identita di rete dichiarata dalla sessione; permessi verificati per singola operazione.')
}
function Verify-DirectoryTarget([string]$server) {
    $root=Get-ADRootDSE -Server $server -AuthType Negotiate
    $expected=($r.Profile.Domain.Split('.') | ForEach-Object { 'DC='+$_ }) -join ','
    if ($root.defaultNamingContext -ine $expected) { throw 'TargetDomainIdentityMismatch' }
}
function Verify-SelectedDirectory {
    Require-Module 'ActiveDirectory'
    $server=$r.Target
    $root=Get-ADRootDSE -Server $server -AuthType Negotiate -ErrorAction Stop
    $expected=($r.Profile.Domain.Split('.') | ForEach-Object { 'DC='+$_ }) -join ','
    if ($root.defaultNamingContext -ine $expected) { throw 'TargetDomainIdentityMismatch' }
    if ($root.dnsHostName -ine $server) { throw 'TargetDirectoryIdentityMismatch' }
    $dc=Get-ADDomainController -Identity $server -Server $server -AuthType Negotiate -ErrorAction Stop
    if (-not $dc -or $dc.HostName -ine $server -or $dc.Domain -ine $r.Profile.Domain) { throw 'TargetDomainControllerRequired' }
    $script:authenticated=$true
    Note ('Bind AD riuscito sul DC selezionato '+$server+'. Nessun fallback al DC del profilo.')
}
function Endpoint([scriptblock]$command, [object[]]$arguments = @()) {
    $options = @{ ComputerName=$r.Target; Authentication='Kerberos'; ScriptBlock=$command;
        ArgumentList=$arguments; ErrorAction='Stop'; SessionOption=(New-PSSessionOption -OpenTimeout 15000 -OperationTimeout 60000) }
    if ($r.Profile.UseHttps) { $options.UseSSL = $true }
    if ($r.Profile.JeaEndpoint) {
        $options.ConfigurationName = $r.Profile.JeaEndpoint
        # JEA NoLanguage accepts a single named command with fixed literal arguments, not our general scriptblocks.
        $options.ScriptBlock = switch ($r.ActionId) {
            'endpoint.health' { { Get-OnlyTweakEndpointEvidence -Kind 'health' } }
            'domain.auth' { { Get-OnlyTweakEndpointEvidence -Kind 'auth' } }
            'endpoint.gpo' { { Get-OnlyTweakEndpointEvidence -Kind 'gpo' } }
            'domain.sysvol' { { Get-OnlyTweakEndpointEvidence -Kind 'sysvol' } }
            'endpoint.verify' {
                switch ($r.Parameters.operation) {
                    'endpoint.gpupdate' { { Get-OnlyTweakEndpointEvidence -Kind 'verify' -Operation 'gpupdate' } }
                    'endpoint.flushdns' { { Get-OnlyTweakEndpointEvidence -Kind 'verify' -Operation 'flushdns' } }
                    'endpoint.spooler' { { Get-OnlyTweakEndpointEvidence -Kind 'verify' -Operation 'spooler' } }
                    'endpoint.securechannel' { { Get-OnlyTweakEndpointEvidence -Kind 'verify' -Operation 'securechannel' } }
                    'endpoint.winsock' { { Get-OnlyTweakEndpointEvidence -Kind 'verify' -Operation 'winsock' } }
                    'endpoint.tcpip' { { Get-OnlyTweakEndpointEvidence -Kind 'verify' -Operation 'tcpip' } }
                    'endpoint.sfc' { { Get-OnlyTweakEndpointEvidence -Kind 'verify' -Operation 'sfc' } }
                    'endpoint.dism' { { Get-OnlyTweakEndpointEvidence -Kind 'verify' -Operation 'dism' } }
                }
            }
            'endpoint.gpupdate' { { Invoke-OnlyTweakEndpointChange -Kind 'gpupdate' } }
            'endpoint.flushdns' { { Invoke-OnlyTweakEndpointChange -Kind 'flushdns' } }
            'endpoint.spooler' { { Invoke-OnlyTweakEndpointChange -Kind 'spooler' } }
            'endpoint.winsock' { { Invoke-OnlyTweakEndpointChange -Kind 'winsock' } }
            'endpoint.tcpip' { { Invoke-OnlyTweakEndpointChange -Kind 'tcpip' } }
            'endpoint.sfc' { { Invoke-OnlyTweakEndpointChange -Kind 'sfc' } }
            'endpoint.dism' { { Invoke-OnlyTweakEndpointChange -Kind 'dism' } }
            default { throw 'OperationNotExposedByJeaProfile' }
        }
        $options.ArgumentList=@()
    }
    $value = Invoke-Command @options
    $script:authenticated = $true
    return $value
}
function Escape-Ldap([string]$value) {
    $value.Replace('\','\5c').Replace('*','\2a').Replace('(','\28').Replace(')','\29').Replace([string][char]0,'\00')
}
function In-OrganizationalUnit([string]$dn,[string]$ou) {
    $suffix=','+$ou
    if (-not $dn.EndsWith($suffix,[StringComparison]::OrdinalIgnoreCase)) { return $false }
    $index=$dn.Length-$suffix.Length
    $slashes=0
    for ($n=$index-1; $n -ge 0 -and $dn[$n] -eq '\'; $n--) { $slashes++ }
    return ($slashes % 2 -eq 0)
}
# Keep this literal block identical to the private collector in OnlyTweakEndpoint.psm1.
# It is sent only to the fixed WinRM diagnostic operations; no request contains code.
$diagnosticEvidence = {
    param([ValidateSet('health','auth','sysvol')][string]$Kind,[bool]$Jea=$false)
    $ErrorActionPreference='Stop'
    $values=[ordered]@{}
    $checks=[ordered]@{}
    function Read-Part([string]$name,[scriptblock]$read,[switch]$Many,[switch]$Events) {
        $items=New-Object 'System.Collections.Generic.List[object]'
        $status='Completed'; $code=''
        try {
            & $read | ForEach-Object { if ($null -ne $_) { $items.Add($_) } }
            if ($items.Count -eq 0) {
                if ($Many) { $status='Empty' } else { $status='Unavailable'; $code='MissingData' }
            }
        } catch {
            # Do not serialize exception text or arbitrary FullyQualifiedErrorId values.
            $id=($_.FullyQualifiedErrorId -split ',')[0]
            $types=@(); $exception=$_.Exception
            while ($null -ne $exception) { $types+=$exception.GetType().FullName; $exception=$exception.InnerException }
            $category=$_.CategoryInfo.Category.ToString()
            if ($Events -and $id -eq 'NoMatchingEventsFound' -and $items.Count -eq 0) { $status='Empty'; $code='NoMatchingEventsFound' }
            elseif ($category -in @('PermissionDenied','SecurityError') -or $types -contains 'System.UnauthorizedAccessException') { $status='Denied'; $code='AccessDenied' }
            elseif ($category -eq 'OperationTimeout' -or $types -contains 'System.TimeoutException') { $status='TimedOut'; $code='ReadTimeout' }
            elseif ($id -in @('NoMatchingLogsFound','NoMatchingProvidersFound') -or $category -eq 'ObjectNotFound' -or
                $types -contains 'System.Diagnostics.Eventing.Reader.EventLogNotFoundException') { $status='Unavailable'; $code='SourceUnavailable' }
            else { $status='Failed'; $code='ReadFailed' }
        }
        if ($Many) { $values[$name]=@($items.ToArray()) }
        else { $values[$name]=if ($items.Count -gt 0) { $items[0] } else { $null } }
        $checks[$name]=[pscustomobject]@{Status=$status;ErrorCode=$code}
    }
    if ($Kind -eq 'health') {
        Read-Part 'OS' { Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber,LastBootUpTime,FreePhysicalMemory }
        Read-Part 'Computer' { Get-CimInstance Win32_ComputerSystem | Select-Object Name,Domain,DomainRole,PartOfDomain }
        Read-Part 'Services' { Get-Service -Name Spooler,WinRM,Netlogon,W32Time,wuauserv | Select-Object Name,Status,StartType } -Many
        Read-Part 'Printers' { Get-CimInstance Win32_Printer | Select-Object -First 100 Name,PrinterStatus,WorkOffline } -Many
        Read-Part 'Updates' { Get-HotFix | Sort-Object InstalledOn -Descending | Select-Object -First 20 HotFixID,InstalledOn } -Many
    } elseif ($Kind -eq 'auth') {
        $values['Computer']=$env:COMPUTERNAME
        Read-Part 'Machine' {
            $machine=Get-CimInstance Win32_ComputerSystem
            if ($null -eq $machine.DomainRole -or $null -eq $machine.PartOfDomain) { throw 'ComputerRoleUnavailable' }
            $machine | Select-Object Domain,DomainRole,PartOfDomain
        }
        $values['Domain']=$null; $values['Role']=$null
        if ($checks['Machine'].Status -eq 'Completed') {
            $values['Domain']=$values['Machine'].Domain; $values['Role']=$values['Machine'].DomainRole
            if ($values['Machine'].DomainRole -lt 4 -and $values['Machine'].PartOfDomain) {
                Read-Part 'SecureChannel' { Test-ComputerSecureChannel -ErrorAction Stop }
            } else {
                $values['SecureChannel']=$null
                $checks['SecureChannel']=[pscustomobject]@{Status='NotApplicable';ErrorCode='NotDomainMember'}
            }
        } else {
            $values['SecureChannel']=$null
            $checks['SecureChannel']=[pscustomobject]@{Status='Unavailable';ErrorCode='ComputerRoleUnavailable'}
        }
        Read-Part 'Time' {
            $global:LASTEXITCODE=$null
            $text=& "$env:windir\System32\w32tm.exe" /query /status 2>&1 | Out-String
            if ($null -eq $LASTEXITCODE -or $LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($text)) { throw 'NativeReadIncomplete' }
            $text
        }
        if ($Jea) {
            $values['Tickets']='Non esposti dal profilo JEA: contesto virtuale diverso dall utente interattivo.'
            $checks['Tickets']=[pscustomobject]@{Status='NotApplicable';ErrorCode='JeaNotExposed'}
        } else {
            Read-Part 'Tickets' {
                $global:LASTEXITCODE=$null
                $text=& "$env:windir\System32\klist.exe" 2>&1 | Out-String
                if ($null -eq $LASTEXITCODE -or $LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($text)) { throw 'NativeReadIncomplete' }
                $text
            }
        }
        Read-Part 'Identity' {
            [pscustomobject]@{ConnectedUser="$($PSSenderInfo.ConnectedUser)";ExecutionIdentity=[Security.Principal.WindowsIdentity]::GetCurrent().Name}
        }
        $values['ConnectedUser']=$null; $values['ExecutionIdentity']=$null
        if ($null -ne $values['Identity']) {
            $values['ConnectedUser']=$values['Identity'].ConnectedUser; $values['ExecutionIdentity']=$values['Identity'].ExecutionIdentity
        }
    } else {
        Read-Part 'Services' { Get-Service -Name DFSR,Netlogon | Select-Object Name,Status } -Many
        Read-Part 'Folders' { Get-CimInstance -Namespace root\MicrosoftDFS -ClassName DfsrReplicatedFolderInfo |
            Select-Object ReplicationGroupName,ReplicatedFolderName,State } -Many
    }
    Read-Part 'Events' {
        $filter=if ($Kind -eq 'sysvol') { @{LogName='DFS Replication';StartTime=(Get-Date).AddDays(-1)} }
            elseif ($Kind -eq 'health') { @{LogName='System';Level=1,2,3;StartTime=(Get-Date).AddHours(-4)} }
            else { @{LogName='System';StartTime=(Get-Date).AddHours(-4)} }
        $limit=if ($Kind -eq 'sysvol') {100} else {60}
        Get-WinEvent -FilterHashtable $filter -MaxEvents $limit -ErrorAction Stop |
            Where-Object { $Kind -ne 'auth' -or $_.ProviderName -match 'NETLOGON|Time-Service|Kerberos' } |
            Select-Object TimeCreated,Id,ProviderName,LevelDisplayName
    } -Many -Events
    $values['Checks']=[pscustomobject]$checks
    [pscustomobject]$values
}
$gpoEvidence = {
    $ErrorActionPreference='Stop'
    $applied=@(); $rsopComplete=$false; $output=''; $code=$null; $nativeComplete=$false
    try {
        $applied=@(Get-CimInstance -Namespace root\RSOP\Computer -ClassName RSOP_GPO -ErrorAction Stop | Select-Object name,guidName,version,enabled,accessDenied)
        $rsopComplete=$true
    } catch { }
    try {
        $global:LASTEXITCODE=$null
        $output=& "$env:windir\System32\gpresult.exe" /scope computer /r 2>&1 | Out-String
        $code=$LASTEXITCODE
        $nativeComplete=($null -ne $code -and $code -eq 0 -and -not [string]::IsNullOrWhiteSpace($output))
    } catch { }
    $domain=$null
    try { $domain=(Get-CimInstance Win32_ComputerSystem -ErrorAction Stop).Domain } catch { }
    [pscustomobject]@{Computer=$env:COMPUTERNAME;Domain=$domain;Applied=$applied;Result=$output;NativeExitCode=$code;
        ReadComplete=($rsopComplete -and $nativeComplete);Checks=[pscustomobject]@{
            RSOP=[pscustomobject]@{Status=$(if (-not $rsopComplete) {'Failed'} elseif ($applied.Count -eq 0) {'Empty'} else {'Completed'});ErrorCode=$(if ($rsopComplete) {''} else {'RsopReadFailed'})}
            Gpresult=[pscustomobject]@{Status=$(if ($nativeComplete) {'Completed'} else {'Failed'});ErrorCode=$(if ($nativeComplete) {''} else {'GpresultReadIncomplete'})}
        }}
}
function Complete-Diagnostic($value) {
    if ($null -eq $value -or $null -eq $value.Checks) { throw 'DiagnosticChecksRequired' }
    $incomplete=@($value.Checks.PSObject.Properties | Where-Object { $_.Value.Status -notin @('Completed','Empty','NotApplicable') })
    if ($incomplete.Count -gt 0) {
        $script:state='Inconclusive'; $script:errorCode='PartialDiagnosticRead'
        $script:summary='Raccolta parziale: consultare Checks e i dati disponibili; nessuna conclusione di salute.'
        Note ('Sottocontrolli incompleti: '+(($incomplete | ForEach-Object {$_.Name}) -join ', '))
    }
}
function Eligible-User {
    if (-not $r.Profile.DelegatedUserOusReviewed) { throw 'DelegatedScopeNotReviewed' }
    $u = Get-ADUser -Identity ([guid]$r.Parameters.objectGuid) -Server $r.Target -Properties adminCount,servicePrincipalName,primaryGroupID,LockedOut,pwdLastSet,AccountExpirationDate -ErrorAction Stop
    if (-not $r.Parameters.expectedDn -or $u.DistinguishedName -ine $r.Parameters.expectedDn) { throw 'ObjectMovedOrSelectionChanged' }
    if ($u.ObjectClass -ne 'user' -or $u.adminCount -eq 1 -or @($u.servicePrincipalName).Count -gt 0 -or
        $u.SID.Value -match '-(500|501|502)$' -or $u.primaryGroupID -ne 513) { throw 'ProtectedOrServiceAccount' }
    $allowed = $false
    foreach ($ou in $r.Profile.AllowedOus) { if (In-OrganizationalUnit $u.DistinguishedName $ou) { $allowed=$true } }
    foreach ($ou in $r.Profile.ExcludedOus) { if (In-OrganizationalUnit $u.DistinguishedName $ou) { throw 'ExcludedOrganizationalUnit' } }
    if (-not $allowed) { throw 'OutsideDelegatedOrganizationalUnits' }
    $groups = @(Get-ADAccountAuthorizationGroup -Identity $u.ObjectGUID -Server $r.Target -ErrorAction Stop)
    foreach ($group in $groups) {
        $sid = $group.SID.Value
        if ($sid -match '^S-1-5-32-(544|548|549|550|551|552)$' -or $sid -match '^S-1-5-21-.+-(512|516|518|519|520|521|526|527)$' -or
            $r.Profile.ProtectedGroupSids -contains $sid) { throw 'PrivilegedGroupMembership' }
    }
    return $u
}
try {
    $line = [Console]::In.ReadLine()
    if (-not $line -or $line.Length -gt 262144) { throw 'InvalidRequest' }
    $r = $line | ConvertFrom-Json
    switch ($r.ActionId) {
        'technician.inspect' {
            $modules = foreach ($name in @('ActiveDirectory','GroupPolicy','DnsServer','DFSN','DFSR','DhcpServer')) {
                [pscustomobject]@{ Name=$name; Present=(Test-Path -LiteralPath (Join-Path $env:windir "System32\WindowsPowerShell\v1.0\Modules\$name")) }
            }
            $data = [pscustomobject]@{ Version=$PSVersionTable.PSVersion.ToString(); LanguageMode="$($ExecutionContext.SessionState.LanguageMode)";
                Identity=[Security.Principal.WindowsIdentity]::GetCurrent().Name; Modules=@($modules);
                OS=(Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber,OSArchitecture) }
            Note 'Connettivita e identita di dominio si verificano nel profilo cliente; la presenza del modulo non ne certifica il funzionamento.'
        }
        'rsat.inspect' {
            Require-Module 'Dism'
            $caps = @(Get-WindowsCapability -Online -Name 'Rsat.*' | Select-Object Name,State)
            $policy = foreach ($path in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Servicing',
                'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate')) {
                if (Test-Path -LiteralPath $path) { Get-ItemProperty -LiteralPath $path |
                    Select-Object LocalSourcePath,UseWindowsUpdate,RepairContentServerSource,UseUpdateClassPolicySource,
                        SetPolicyDrivenUpdateSourceForFeatureUpdates,SetPolicyDrivenUpdateSourceForQualityUpdates }
            }
            $data = [pscustomobject]@{ Capabilities=$caps; Policy=@($policy); Source='Origine Windows/aziendale configurata' }
            Note 'Inventario e lettura policy soltanto: nessuna modifica WSUS o Windows Update.'
        }
        'rsat.install' {
            if (-not $r.Confirmed) { throw 'ConfirmationRequired' }
            $allowed = @{
                'AD DS/LDS'='Rsat.ActiveDirectory.DS-LDS.Tools~~~~0.0.1.0'; 'Criteri di gruppo'='Rsat.GroupPolicy.Management.Tools~~~~0.0.1.0';
                'DNS'='Rsat.Dns.Tools~~~~0.0.1.0'; 'DFS'='Rsat.FileServices.Tools~~~~0.0.1.0'; 'DHCP'='Rsat.DHCP.Tools~~~~0.0.1.0'
            }
            $modules = @{ 'AD DS/LDS'=@('ActiveDirectory'); 'Criteri di gruppo'=@('GroupPolicy'); 'DNS'=@('DnsServer'); 'DFS'=@('DFSN','DFSR'); 'DHCP'=@('DhcpServer') }
            $consoles = @{ 'AD DS/LDS'=@('dsa.msc','dssite.msc','repadmin.exe','dcdiag.exe'); 'Criteri di gruppo'=@('gpmc.msc');
                'DNS'=@('dnsmgmt.msc'); 'DFS'=@('dfsmgmt.msc'); 'DHCP'=@('dhcpmgmt.msc') }
            $cap = $allowed[$r.Parameters.capability]
            if (-not $cap) { throw 'CapabilityNotAllowed' }
            $edition = (Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').EditionID
            if ($edition -notmatch '^(Professional|Enterprise|IoTEnterprise)') { throw 'RsatEditionNotQualified' }
            Require-Module 'Dism'
            $before = Get-WindowsCapability -Online -Name $cap
            if ($before.State -eq 'Installed') { $install = $null; Note 'Componente gia installato; nessuna reinstallazione.' }
            else {
                $options = @{ Online=$true; Name=$cap; ErrorAction='Stop' }
                if ($r.Parameters.source) {
                    if ($r.Parameters.source -notmatch '^[A-Za-z]:\\' -or -not (Test-Path -LiteralPath $r.Parameters.source -PathType Container)) { throw 'LocalFodSourceRequired' }
                    $options.Source = $r.Parameters.source; $options.LimitAccess = $true
                }
                $install = Add-WindowsCapability @options
            }
            $after = Get-WindowsCapability -Online -Name $cap
            $checks = foreach ($name in $modules[$r.Parameters.capability]) {
                $present = Test-Path -LiteralPath (Join-Path $env:windir "System32\WindowsPowerShell\v1.0\Modules\$name")
                if (-not $present) { $state='Unverified' }
                [pscustomobject]@{ Module=$name; Present=$present }
            }
            $tools = foreach ($tool in $consoles[$r.Parameters.capability]) {
                $present = Test-Path -LiteralPath (Join-Path $env:windir "System32\$tool")
                if (-not $present) { $state='Unverified' }
                [pscustomobject]@{ Tool=$tool; Present=$present }
            }
            if ($after.State -ne 'Installed') { $state='Unverified' }
            $data = [pscustomobject]@{ Name=$cap; Before="$($before.State)"; After="$($after.State)";
                RestartNeeded=[bool]$install.RestartNeeded; Modules=@($checks); Tools=@($tools);
                Source=$(if ($r.Parameters.source) {$r.Parameters.source} else {'Origine aziendale configurata'}) }
            $summary = 'Installazione conclusa; consulta verifica componenti e riavvio richiesto. Nessun riavvio automatico.'
        }
        'session.authenticate' {
            Verify-SelectedDirectory
            $domain=Get-ADDomain -Server $r.Target -ErrorAction Stop
            if ($domain.DNSRoot -ine $r.Profile.Domain) { throw 'TargetDomainIdentityMismatch' }
            $data = $domain | Select-Object @{Name='Server';Expression={$r.Target}},DNSRoot,DomainMode,PDCEmulator,RIDMaster,InfrastructureMaster
        }
        'domain.discovery' {
            $data = [pscustomobject]@{
                Interfaces=@(Get-CimInstance Win32_NetworkAdapterConfiguration -Filter 'IPEnabled=True' |
                    Select-Object Description,IPAddress,DefaultIPGateway,DNSServerSearchOrder,DNSDomain)
                Srv=@(Resolve-DnsName -Name ('_ldap._tcp.dc._msdcs.'+$r.Profile.Domain) -Type SRV -DnsOnly |
                    Select-Object Name,NameTarget,Port,Priority,Weight)
                Locator=(Native 'nltest.exe' @('/dsgetdc:'+$r.Profile.Domain))
                Ports=@(foreach ($port in @(88,135,389,445,9389)) {
                    $ok=Test-NetConnection -ComputerName $r.Profile.DomainController -Port $port -InformationLevel Quiet -WarningAction SilentlyContinue
                    [pscustomobject]@{ Target=$r.Profile.DomainController; Protocol='TCP'; Port=$port; Reachable=$ok }
                })
            }
            Note 'Misure dalla postazione tecnico. TCP aperto non prova LDAP/Kerberos, UDP o porte RPC dinamiche; timeout non prova servizio inattivo.'
        }
        'domain.auth' {
            $data = Endpoint $diagnosticEvidence @('auth',$false)
            Complete-Diagnostic $data
            if ($null -ne $data.SecureChannel -and $data.SecureChannel -eq $false) {
                $state='ObservedIssue'; $summary='Canale sicuro del computer membro non valido; consultare anche Checks per le letture incomplete.'
            }
            Note 'Ticket del contesto remoto amministrativo, non della sessione interattiva dell utente. Nessun test/ripristino canale sicuro sui DC.'
        }
        'ad.replication' {
            Verify-Directory
            $servers = @($r.Target)
            if ($r.Parameters.peer) { $servers += $r.Parameters.peer }
            $metadata = foreach ($server in $servers) {
                Verify-DirectoryTarget $server
                Get-ADReplicationPartnerMetadata -Target $server -Scope Server -Partition '*' -PartnerType Both -ErrorAction Stop |
                    Select-Object Server,Partner,Partition,LastReplicationAttempt,LastReplicationSuccess,LastReplicationResult,ConsecutiveReplicationFailures
            }
            $failures = @(foreach ($server in $servers) {
                Get-ADReplicationFailure -Target $server -Scope Server -ErrorAction Stop |
                    Select-Object Server,Partner,FirstFailureTime,FailureCount,LastError
            })
            if ($failures.Count -gt 0 -or @($metadata | Where-Object LastReplicationResult -ne 0).Count -gt 0) {
                $state='ObservedIssue'; $summary='Errori di replica osservati; esaminare partner e partizioni.'
            }
            if (@($metadata).Count -eq 0) { $state='Inconclusive'; $summary='Nessun metadato di replica: impossibile concludere.' }
            $data = [pscustomobject]@{ Metadata=@($metadata); Failures=$failures; Repadmin=(Native 'repadmin.exe' @('/showrepl',$r.Target,'/csv')) }
        }
        'ad.dcdiag' {
            $data = Endpoint {
                if ((Get-CimInstance Win32_ComputerSystem).DomainRole -lt 4) { throw 'DomainControllerRequired' }
                foreach ($test in @('Connectivity','Advertising','SysVolCheck','NetLogons','Replications')) {
                    $text=& "$env:windir\System32\dcdiag.exe" "/s:$env:COMPUTERNAME" "/test:$test" 2>&1 | Out-String
                    [pscustomobject]@{ Test=$test; ExitCode=$LASTEXITCODE; Output=$text }
                }
            }
            if (@($data | Where-Object ExitCode -ne 0).Count) { $state='ObservedIssue' }
            Note 'Test eseguiti sul DC tramite WinRM, senza secondo salto. Niente /c, /fix, DNS dinamico, CutoffServers o Intersite. Leggere anche l output.'
        }
        'ad.object' {
            Verify-Directory
            if (-not $r.Parameters.peer) { throw 'SecondDomainControllerRequired' }
            $data = @(foreach ($server in @($r.Target,$r.Parameters.peer)) {
                Verify-DirectoryTarget $server
                $object=Get-ADObject -Identity ([guid]$r.Parameters.objectGuid) -Server $server -Properties whenChanged,uSNChanged
                [pscustomobject]@{ Server=$server; Object=($object | Select-Object ObjectGUID,DistinguishedName,whenChanged,uSNChanged);
                    Metadata=@(Get-ADReplicationAttributeMetadata -Object $object.DistinguishedName -Server $server |
                        Select-Object AttributeName,Version,LastOriginatingChangeTime,LastOriginatingChangeDirectoryServerIdentity) }
            })
            Note 'USN e whenChanged sono locali al DC: confrontare anche versione e origine dei metadati, non solo valori numerici.'
        }
        'ad.search' {
            Verify-SelectedDirectory
            $query=Escape-Ldap $r.Parameters.query
            $filter="(|(sAMAccountName=*$query*)(displayName=*$query*)(name=*$query*))"
            $options=@{ LDAPFilter=$filter; Server=$r.Target; ResultPageSize=50; ResultSetSize=1001; ErrorAction='Stop' }
            switch ($r.Parameters.kind) {
                'user' { $items=@(Get-ADUser @options -Properties LockedOut,AccountExpirationDate,PasswordExpired,LastLogonDate,MemberOf |
                    Select-Object ObjectGUID,SID,DistinguishedName,SamAccountName,Name,Enabled,LockedOut,AccountExpirationDate,PasswordExpired,LastLogonDate,MemberOf) }
                'group' { $items=@(Get-ADGroup @options | Select-Object ObjectGUID,SID,DistinguishedName,SamAccountName,Name,GroupScope,GroupCategory) }
                'computer' { $items=@(Get-ADComputer @options -Properties OperatingSystem,LastLogonDate |
                    Select-Object ObjectGUID,SID,DistinguishedName,Name,DNSHostName,Enabled,OperatingSystem,LastLogonDate) }
                default { throw 'InvalidObjectKind' }
            }
            $page=0
            if ($r.Parameters.page) { $page=[int]$r.Parameters.page }
            $sorted=@($items | Sort-Object ObjectGUID | Select-Object -First 1000)
            $data=[pscustomobject]@{ Server=$r.Target; Page=$page; PageSize=50; Rows=@($sorted | Select-Object -Skip ($page*50) -First 50);
                HasMore=(($page+1)*50 -lt $sorted.Count); LimitReached=($items.Count -gt 1000) }
            Note 'Massimo 1000 oggetti per ricerca; raffinare se raggiunto. LastLogonDate e approssimato, lockout letto sul DC selezionato.'
        }
        'ad.userstate' {
            Verify-Directory
            Verify-DirectoryTarget $r.Target
            $data=Get-ADUser -Identity ([guid]$r.Parameters.objectGuid) -Server $r.Target -Properties LockedOut,pwdLastSet,AccountExpirationDate |
                Select-Object ObjectGUID,DistinguishedName,Enabled,LockedOut,pwdLastSet,AccountExpirationDate
        }
        'endpoint.verify' {
            $data=Endpoint {
                param($operation)
                $machine=Get-CimInstance Win32_ComputerSystem
                if ($null -eq $machine.DomainRole -or $machine.DomainRole -notin @(0,1,2,3)) { throw 'EndpointVerificationRequiresKnownMemberRole' }
                $observed=$null; $output=''; $code=$null; $complete=$true
                switch ($operation) {
                    'endpoint.gpupdate' { $global:LASTEXITCODE=$null; $output=& "$env:windir\System32\gpresult.exe" /scope computer /r 2>&1 | Out-String; $code=$LASTEXITCODE }
                    'endpoint.flushdns' { $observed=@(Get-DnsClientCache | Select-Object -First 100 Entry,Data,Status) }
                    'endpoint.spooler' { $observed=Get-Service Spooler | Select-Object Name,Status,StartType }
                    'endpoint.securechannel' {
                        if (-not $machine.PartOfDomain) { throw 'DomainMemberRequired' }
                        $observed=Test-ComputerSecureChannel -ErrorAction Stop
                    }
                    'endpoint.winsock' { $global:LASTEXITCODE=$null; $output=& "$env:windir\System32\netsh.exe" winsock show catalog 2>&1 | Out-String; $code=$LASTEXITCODE }
                    'endpoint.tcpip' { $observed=@(Get-NetIPConfiguration | Select-Object InterfaceAlias,IPv4Address,IPv4DefaultGateway,DNSServer) }
                    'endpoint.sfc' { $global:LASTEXITCODE=$null; $output=& "$env:windir\System32\sfc.exe" /verifyonly 2>&1 | Out-String; $code=$LASTEXITCODE }
                    'endpoint.dism' { $global:LASTEXITCODE=$null; $output=& "$env:windir\System32\dism.exe" /Online /Cleanup-Image /CheckHealth /NoRestart 2>&1 | Out-String; $code=$LASTEXITCODE }
                    default { throw 'VerificationNotAllowed' }
                }
                if ($operation -in @('endpoint.gpupdate','endpoint.winsock','endpoint.sfc','endpoint.dism')) {
                    $complete=($null -ne $code -and $code -eq 0 -and -not [string]::IsNullOrWhiteSpace($output))
                    $observed=$output
                } elseif ($operation -eq 'endpoint.spooler') {
                    $complete=($null -ne $observed -and -not [string]::IsNullOrWhiteSpace("$($observed.Status)"))
                } elseif ($operation -eq 'endpoint.securechannel') { $complete=($observed -is [bool]) }
                [pscustomobject]@{ Operation=$operation; Computer=$env:COMPUTERNAME; ReadComplete=[bool]$complete;
                    NativeExitCode=$code; Output=$output; Data=$observed }
            } @($r.Parameters.operation)
            if ($null -eq $data -or $data.ReadComplete -ne $true -or $data.Operation -ne $r.Parameters.operation) {
                $state='Inconclusive'; $errorCode='EndpointReadIncomplete'
                $summary='Rilettura non completata: il retry resta bloccato. Consultare codice nativo ed evidenze.'
            }
            Note 'Rilettura osservativa prima/dopo intervento: leggere il risultato nativo, non solo il completamento del comando.'
        }
        'endpoint.gpo' {
            $endpointData=Endpoint $gpoEvidence
            $versions=New-Object 'System.Collections.Generic.List[object]'
            $versionsComplete=$false
            try {
                Verify-Directory
                Require-Module 'GroupPolicy'
                foreach ($item in $endpointData.Applied) {
                    Get-GPO -Guid ([guid]$item.guidName.Trim('{}')) -Domain $r.Profile.Domain -Server $r.Profile.DomainController -ErrorAction Stop |
                        Select-Object DisplayName,Id,Computer,User | ForEach-Object { $versions.Add($_) }
                }
                $versionsComplete=$true
            } catch { }
            $data=[pscustomobject]@{Endpoint=$endpointData;Versions=@($versions.ToArray());Checks=[pscustomobject]@{
                Versions=[pscustomobject]@{Status=$(if (-not $versionsComplete) {'Failed'} elseif ($versions.Count -eq 0) {'Empty'} else {'Completed'});ErrorCode=$(if ($versionsComplete) {''} else {'GpoVersionReadFailed'})}
            }}
            if (-not $endpointData.ReadComplete -or -not $versionsComplete) {
                $state='Inconclusive'; $errorCode='PartialGpoRead'; $summary='Raccolta GPO parziale: consultare codice nativo, RSOP e versioni disponibili.'
            }
            Note 'Contesto computer; GPO utente da raccogliere nella sua sessione. Versioni AD/SYSVOL lette direttamente dal tecnico, senza secondo salto.'
        }
        'domain.sysvol' {
            $remote=$null; $remoteCheck=$null
            try { $remote=Endpoint $diagnosticEvidence @('sysvol',$false) }
            catch {
                $category=$_.CategoryInfo.Category.ToString()
                if ($category -in @('PermissionDenied','SecurityError') -or $_.Exception -is [UnauthorizedAccessException]) {
                    $remoteCheck=[pscustomobject]@{Status='Denied';ErrorCode='RemoteReadDenied'}
                } elseif ($category -eq 'OperationTimeout' -or $_.Exception -is [TimeoutException]) {
                    $remoteCheck=[pscustomobject]@{Status='TimedOut';ErrorCode='RemoteReadTimeout'}
                } else { $remoteCheck=[pscustomobject]@{Status='Unavailable';ErrorCode='RemoteReadUnavailable'} }
            }
            $shares=New-Object 'System.Collections.Generic.List[object]'
            $shareChecks=[ordered]@{}
            foreach ($name in @('SYSVOL','NETLOGON')) {
                $shareStatus='Completed'; $shareCode=''
                try {
                    Verify-Directory
                    $item=Get-Item -LiteralPath ('\\'+$r.Profile.DomainController+'\'+$name) -ErrorAction Stop
                    if (-not $item.PSIsContainer) { throw 'ShareNotDirectory' }
                    $shares.Add([pscustomobject]@{Name=$name;Accessible=$true})
                } catch {
                    $category=$_.CategoryInfo.Category.ToString()
                    if ($category -in @('PermissionDenied','SecurityError') -or $_.Exception -is [UnauthorizedAccessException]) { $shareStatus='Denied'; $shareCode='AccessDenied' }
                    elseif ($category -eq 'OperationTimeout' -or $_.Exception -is [TimeoutException]) { $shareStatus='TimedOut'; $shareCode='ReadTimeout' }
                    else { $shareStatus='Unavailable'; $shareCode='ShareReadUnavailable' }
                    $shares.Add([pscustomobject]@{Name=$name;Accessible=$null})
                }
                $shareChecks[$name]=[pscustomobject]@{Status=$shareStatus;ErrorCode=$shareCode}
            }
            if ($null -ne $remote) {
                foreach ($property in $remote.Checks.PSObject.Properties) { $shareChecks['Remote'+$property.Name]=$property.Value }
            } else { $shareChecks['Remote']=$remoteCheck }
            $data=[pscustomobject]@{Shares=@($shares.ToArray());Remote=$remote;Checks=[pscustomobject]$shareChecks}
            Complete-Diagnostic $data
            Note 'Letture SYSVOL/DFSR separate dalla replica AD. Eventi storici e assenza di attivita non provano da soli guasto o convergenza.'
        }
        'dfs.namespace' {
            Require-Module 'DFSN'
            $path=$r.Parameters.path
            if (-not $path -or $path -match '[*?\[\]]' -or $path -notmatch '^\\\\([^\\]+)\\[^\\]+(?:\\.*)?$') { throw 'ExplicitDfsPathRequired' }
            $hostName=$Matches[1]
            if ($hostName -ine $r.Profile.Domain -and $r.Profile.Targets -notcontains $hostName) { throw 'DfsPathOutsideProfile' }
            $targets=@(Get-DfsnFolderTarget -Path $path -ErrorAction Stop | Select-Object Path,TargetPath,State,ReferralPriorityClass,ReferralPriorityRank)
            $authenticated=$true
            $data=[pscustomobject]@{ Targets=$targets; Reachability=@(foreach ($entry in $targets) {
                $hostPart=($entry.TargetPath -split '\\')[2]
                if ($r.Profile.Targets -notcontains $hostPart) { Note ('Target fuori profilo non contattato: '+$hostPart); continue }
                try { $item=Get-Item -LiteralPath $entry.TargetPath -ErrorAction Stop; [pscustomobject]@{Path=$entry.TargetPath; Accessible=[bool]$item.PSIsContainer} }
                catch { [pscustomobject]@{Path=$entry.TargetPath; Accessible=$null; ErrorCode=$_.CategoryInfo.Category.ToString()} }
            }) }
            Note 'Namespace e referral non replicano i file. Replica DFSR da controllare separatamente; misure dalla postazione tecnico.'
        }
        'dfsr.backlog' {
            Require-Module 'DFSR'
            $samples=@(for ($n=0; $n -lt 2; $n++) {
                $options=@{ GroupName=$r.Parameters.group; FolderName=$r.Parameters.folder; SourceComputerName=$r.Target;
                    DestinationComputerName=$r.Parameters.peer; Verbose=$true; ErrorAction='Stop' }
                $records=@(Get-DfsrBacklog @options 4>&1)
                $verbose=@($records | Where-Object { $_ -is [Management.Automation.VerboseRecord] })
                $files=@($records | Where-Object { $_ -isnot [Management.Automation.VerboseRecord] } | Select-Object FileName,FullPathName,UpdateTime)
                [pscustomobject]@{ Time=(Get-Date).ToUniversalTime().ToString('o'); Source=$r.Target; Destination=$r.Parameters.peer;
                    SampleCount=$files.Count; SampleLimit=100; TotalCount=$null; CountEvidence=@($verbose | ForEach-Object {"$_"}); Files=$files }
                if ($n -eq 0) { Start-Sleep -Seconds 3 }
            })
            $data=[pscustomobject]@{ Samples=$samples; State=@(Get-DfsrState -ComputerName $r.Target | Select-Object -First 100) }
            $authenticated=$true
            Note 'Due campioni direzionali; SampleCount non e il totale. Totale, se disponibile, nel Verbose originale: nessun parser dipendente dalla lingua.'
            Note 'Backlog non nullo o invariato in tre secondi non prova guasto: considerare orari, traffico e pianificazione.'
        }
        'endpoint.health' {
            $data=Endpoint $diagnosticEvidence @('health',$false)
            Complete-Diagnostic $data
            Note 'Elenco hotfix non completo per ogni tipo di aggiornamento; assenza di eventi non prova assenza di problemi.'
        }
        { $_ -in @('ad.unlock','ad.enable','ad.disable','ad.password') } {
            if (-not $r.Confirmed) { throw 'ConfirmationRequired' }
            Verify-Directory
            $user=Eligible-User
            $before=$user | Select-Object ObjectGUID,DistinguishedName,Enabled,LockedOut,pwdLastSet
            $user=Eligible-User
            $options=@{ Identity=$user.ObjectGUID; Server=$r.Target; ErrorAction='Stop'; Confirm=$false }
            switch ($r.ActionId) {
                'ad.unlock' { Unlock-ADAccount @options }
                'ad.enable' { Enable-ADAccount @options }
                'ad.disable' { Disable-ADAccount @options }
                'ad.password' {
                    if (-not $r.ProtectedSecret) { throw 'PasswordRequired' }
                    Add-Type -AssemblyName System.Security
                    $bytes=[Security.Cryptography.ProtectedData]::Unprotect([Convert]::FromBase64String($r.ProtectedSecret),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
                    try {
                        $password=New-Object Security.SecureString
                        for ($i=0; $i -lt $bytes.Length; $i+=2) { $password.AppendChar([char]([int]$bytes[$i] -bor ([int]$bytes[$i+1] -shl 8))) }
                        $password.MakeReadOnly()
                        Set-ADAccountPassword @options -Reset -NewPassword $password
                    } finally {
                        if ($password) { $password.Dispose() }
                        if ($bytes) { [Array]::Clear($bytes,0,$bytes.Length) }
                        $r.ProtectedSecret=$null
                    }
                }
            }
            $after=Get-ADUser -Identity $user.ObjectGUID -Server $r.Target -Properties LockedOut,pwdLastSet
            $data=[pscustomobject]@{ Before=$before; After=($after | Select-Object ObjectGUID,Enabled,LockedOut,pwdLastSet) }
            $verified=switch ($r.ActionId) {
                'ad.unlock' { -not $after.LockedOut }; 'ad.enable' { $after.Enabled }; 'ad.disable' { -not $after.Enabled }
                'ad.password' { $after.pwdLastSet -gt $before.pwdLastSet }
            }
            if (-not $verified) { $state='Unverified' }
            $summary='Operazione sul singolo utente conclusa; consulta rilettura sul DC. Nessuna verifica password mediante tentativi di accesso.'
        }
        { $_ -in @('endpoint.gpupdate','endpoint.flushdns','endpoint.spooler','endpoint.securechannel','endpoint.winsock','endpoint.tcpip','endpoint.sfc','endpoint.dism') } {
            if (-not $r.Confirmed) { throw 'ConfirmationRequired' }
            $operation={
                param($id)
                $machine=Get-CimInstance Win32_ComputerSystem
                if ($null -eq $machine.DomainRole -or $machine.DomainRole -notin @(0,1,2,3)) { throw 'EndpointChangesForbiddenOnDomainControllerOrUnknownRole' }
                $before=$null; $after=$null; $exitCode=0; $text=''; $verified=$true
                switch ($id) {
                    'endpoint.gpupdate' { $text=& "$env:windir\System32\gpupdate.exe" /target:computer /wait:120 2>&1 | Out-String; $exitCode=$LASTEXITCODE }
                    'endpoint.flushdns' { $before=@(Get-DnsClientCache).Count; Clear-DnsClientCache; $after=@(Get-DnsClientCache).Count }
                    'endpoint.spooler' { $before=(Get-Service Spooler).Status.ToString(); Restart-Service -Name Spooler -ErrorAction Stop;
                        (Get-Service Spooler).WaitForStatus('Running',[TimeSpan]::FromSeconds(30)); $after=(Get-Service Spooler).Status.ToString() }
                    'endpoint.securechannel' {
                        if (-not $machine.PartOfDomain) { throw 'DomainMemberRequired' }
                        $before=Test-ComputerSecureChannel; $repaired=Test-ComputerSecureChannel -Repair -ErrorAction Stop
                        $after=Test-ComputerSecureChannel -ErrorAction Stop; $verified=$repaired -and $after
                    }
                    'endpoint.winsock' { $text=& "$env:windir\System32\netsh.exe" winsock reset 2>&1 | Out-String; $exitCode=$LASTEXITCODE; $verified=$false }
                    'endpoint.tcpip' { $text=& "$env:windir\System32\netsh.exe" int ip reset 2>&1 | Out-String; $exitCode=$LASTEXITCODE; $verified=$false }
                    'endpoint.sfc' { $text=& "$env:windir\System32\sfc.exe" /scannow 2>&1 | Out-String; $exitCode=$LASTEXITCODE; $verified=$false }
                    'endpoint.dism' { $text=& "$env:windir\System32\dism.exe" /Online /Cleanup-Image /RestoreHealth /NoRestart 2>&1 | Out-String;
                        $exitCode=$LASTEXITCODE; $verified=$false }
                    default { throw 'EndpointActionNotAllowed' }
                }
                [pscustomobject]@{ Computer=$env:COMPUTERNAME; Before=$before; After=$after; ExitCode=$exitCode; Output=$text; Verified=$verified }
            }
            # Secure-channel repair needs a local privileged identity and may contact a DC.
            # Never forward credentials or introduce a WinRM second hop for this action.
            if ($r.ActionId -eq 'endpoint.securechannel') { $data=& $operation $r.ActionId }
            else { $data=Endpoint $operation @($r.ActionId) }
            if (-not $data.Verified -or $data.ExitCode -ne 0) { $state='Unverified' }
            $summary='Intervento terminato; verifica effetti e riepilogo dello strumento. Riavvii sempre manuali.'
        }
        default { throw 'ActionNotAllowed' }
    }
} catch {
    # Never serialize exception text: cmdlet errors can repeat sensitive input.
    $errorCode=$_.FullyQualifiedErrorId
    if ($errorCode.Length -gt 180) { $errorCode=$_.CategoryInfo.Category.ToString() }
    $state=if ($r.ActionId -match '^(ad\.(unlock|enable|disable|password)|endpoint\.(gpupdate|flushdns|spooler|securechannel|winsock|tcpip|sfc|dism)|rsat\.install)$') {'Unverified'} else {'Inconclusive'}
    $summary='Controllo o intervento non verificabile: controllare prerequisiti, autorizzazioni e collegamento prima di riprovare.'
    Note ('Categoria errore: '+$_.CategoryInfo.Category)
}
[pscustomobject]@{ State=$state; Summary=$summary; Evidence=@($evidence); Data=$data;
    AuthenticationVerified=$authenticated; ErrorCode=$errorCode } | ConvertTo-Json -Depth 12 -Compress
