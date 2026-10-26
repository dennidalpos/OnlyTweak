# Install only on member computers using Provision-Remoting.ps1. Sign with the enterprise publisher.
Set-StrictMode -Version 2
function Get-OnlyTweakDiagnosticEvidence {
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
function Get-OnlyTweakGpoEvidence {
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
function Get-OnlyTweakEndpointEvidence {
    [CmdletBinding()]
    param([ValidateSet('health','auth','gpo','sysvol','dns','spooler','winsock','network','sfc','dism','verify')][string]$Kind='health',
        [ValidateSet('gpupdate','flushdns','spooler','securechannel','winsock','tcpip','sfc','dism')][string]$Operation)
    $ErrorActionPreference='Stop'
    if ($Kind -eq 'verify') {
        if (-not $Operation) { throw 'VerificationOperationRequired' }
        $operationId='endpoint.'+$Operation
        $machine=Get-CimInstance Win32_ComputerSystem
        if ($null -eq $machine.DomainRole -or $machine.DomainRole -notin @(0,1,2,3)) { throw 'EndpointVerificationRequiresKnownMemberRole' }
        $observed=$null; $output=''; $code=$null; $complete=$true
        switch ($operationId) {
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
        if ($operationId -in @('endpoint.gpupdate','endpoint.winsock','endpoint.sfc','endpoint.dism')) {
            $complete=($null -ne $code -and $code -eq 0 -and -not [string]::IsNullOrWhiteSpace($output))
            $observed=$output
        } elseif ($operationId -eq 'endpoint.spooler') {
            $complete=($null -ne $observed -and -not [string]::IsNullOrWhiteSpace("$($observed.Status)"))
        } elseif ($operationId -eq 'endpoint.securechannel') { $complete=($observed -is [bool]) }
        [pscustomobject]@{ Operation=$operationId; Computer=$env:COMPUTERNAME; ReadComplete=[bool]$complete;
            NativeExitCode=$code; Output=$output; Data=$observed }
        return
    }
    if ($Kind -in @('health','auth','sysvol')) { Get-OnlyTweakDiagnosticEvidence -Kind $Kind -Jea $true; return }
    if ($Kind -eq 'gpo') { Get-OnlyTweakGpoEvidence; return }
    $computer=Get-CimInstance Win32_ComputerSystem
    switch ($Kind) {
        'dns' { Get-DnsClientCache | Select-Object -First 100 Entry,Data,Status }
        'spooler' { Get-Service Spooler | Select-Object Name,Status,StartType }
        'winsock' { & "$env:windir\System32\netsh.exe" winsock show catalog }
        'network' { Get-NetIPConfiguration | Select-Object InterfaceAlias,IPv4Address,IPv4DefaultGateway,DNSServer }
        'sfc' { & "$env:windir\System32\sfc.exe" /verifyonly }
        'dism' { & "$env:windir\System32\dism.exe" /Online /Cleanup-Image /CheckHealth /NoRestart }

    }
}
function Invoke-OnlyTweakEndpointChange {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateSet('gpupdate','flushdns','spooler','winsock','tcpip','sfc','dism')][string]$Kind)
    $ErrorActionPreference='Stop'
    $role=(Get-CimInstance Win32_ComputerSystem).DomainRole
    if ($null -eq $role -or $role -notin @(0,1,2,3)) { throw 'EndpointChangesForbiddenOnDomainControllerOrUnknownRole' }
    $before=$null; $after=$null; $exitCode=0; $output=''; $verified=$true
    switch ($Kind) {
        'gpupdate' { $output=& "$env:windir\System32\gpupdate.exe" /target:computer /wait:120 2>&1 | Out-String; $exitCode=$LASTEXITCODE }
        'flushdns' { $before=@(Get-DnsClientCache).Count; Clear-DnsClientCache; $after=@(Get-DnsClientCache).Count }
        'spooler' { $before=(Get-Service Spooler).Status.ToString(); Restart-Service Spooler;
            (Get-Service Spooler).WaitForStatus('Running',[TimeSpan]::FromSeconds(30)); $after=(Get-Service Spooler).Status.ToString() }
        'winsock' { $output=& "$env:windir\System32\netsh.exe" winsock reset 2>&1 | Out-String; $exitCode=$LASTEXITCODE; $verified=$false }
        'tcpip' { $output=& "$env:windir\System32\netsh.exe" int ip reset 2>&1 | Out-String; $exitCode=$LASTEXITCODE; $verified=$false }
        'sfc' { $output=& "$env:windir\System32\sfc.exe" /scannow 2>&1 | Out-String; $exitCode=$LASTEXITCODE; $verified=$false }
        'dism' { $output=& "$env:windir\System32\dism.exe" /Online /Cleanup-Image /RestoreHealth /NoRestart 2>&1 | Out-String;
            $exitCode=$LASTEXITCODE; $verified=$false }
    }
    [pscustomobject]@{Computer=$env:COMPUTERNAME; Before=$before; After=$after; ExitCode=$exitCode; Output=$output; Verified=$verified}
}
Export-ModuleMember -Function Get-OnlyTweakEndpointEvidence,Invoke-OnlyTweakEndpointChange
