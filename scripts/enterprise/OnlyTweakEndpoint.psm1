# Install only on member computers using Provision-Remoting.ps1. Sign with the enterprise publisher.
Set-StrictMode -Version 2
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
    $computer=Get-CimInstance Win32_ComputerSystem
    switch ($Kind) {
        'dns' { Get-DnsClientCache | Select-Object -First 100 Entry,Data,Status }
        'spooler' { Get-Service Spooler | Select-Object Name,Status,StartType }
        'winsock' { & "$env:windir\System32\netsh.exe" winsock show catalog }
        'network' { Get-NetIPConfiguration | Select-Object InterfaceAlias,IPv4Address,IPv4DefaultGateway,DNSServer }
        'sfc' { & "$env:windir\System32\sfc.exe" /verifyonly }
        'dism' { & "$env:windir\System32\dism.exe" /Online /Cleanup-Image /CheckHealth /NoRestart }
        'health' {
            [pscustomobject]@{ OS=(Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber,LastBootUpTime,FreePhysicalMemory);
                Computer=($computer | Select-Object Name,Domain,DomainRole,PartOfDomain);
                Services=@(Get-Service Spooler,WinRM,Netlogon,W32Time,wuauserv | Select-Object Name,Status,StartType);
                Printers=@(Get-CimInstance Win32_Printer | Select-Object -First 100 Name,PrinterStatus,WorkOffline);
                Updates=@(Get-HotFix | Sort-Object InstalledOn -Descending | Select-Object -First 20 HotFixID,InstalledOn) }
        }
        'auth' {
            $channel=$null
            if ($computer.DomainRole -lt 4 -and $computer.PartOfDomain) { $channel=Test-ComputerSecureChannel }
            [pscustomobject]@{ Computer=$env:COMPUTERNAME; Domain=$computer.Domain; Role=$computer.DomainRole; SecureChannel=$channel;
                Time=(& "$env:windir\System32\w32tm.exe" /query /status | Out-String);
                Tickets='Non esposti dal profilo JEA: contesto virtuale diverso dall utente interattivo.' }
        }
        'gpo' {
            [pscustomobject]@{ Computer=$env:COMPUTERNAME; Domain=$computer.Domain;
                Applied=@(Get-CimInstance -Namespace root\RSOP\Computer -ClassName RSOP_GPO | Select-Object name,guidName,version,enabled,accessDenied);
                Result=(& "$env:windir\System32\gpresult.exe" /scope computer /r | Out-String) }
        }
        'sysvol' {
            [pscustomobject]@{ Services=@(Get-Service DFSR,Netlogon | Select-Object Name,Status);
                Folders=@(Get-CimInstance -Namespace root\MicrosoftDFS -ClassName DfsrReplicatedFolderInfo |
                    Select-Object ReplicationGroupName,ReplicatedFolderName,State) }
        }
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
