#requires -Version 5.1
# Local receiver only. Never calls Enable-PSRemoting or enables existing endpoints.
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$script:RemotingRoot=Join-Path $env:ProgramData 'OnlyTweak\LocalRemoting'
$script:EndpointName='OnlyTweak.Local'
$script:RuleName='OnlyTweak-Private-LocalRemoting'
$script:AdminSddl='O:NSG:BAD:P(A;;GA;;;BA)(A;;GA;;;SY)'
function Assert-Administrator {
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    try {if (!(New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {throw 'ElevationRequired'}} finally {$identity.Dispose()}
}
function Assert-PlainPath([string]$path) {
    $item=New-Object IO.DirectoryInfo($path)
    while ($item) {if ($item.Exists -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {throw 'ReparsePointNotAllowed'};$item=$item.Parent}
    if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)) {throw 'ReparsePointNotAllowed'}
}
function Hash-Value($value) {
    $algorithm=[Security.Cryptography.SHA256]::Create()
    try {([BitConverter]::ToString($algorithm.ComputeHash([Text.Encoding]::UTF8.GetBytes(($value | ConvertTo-Json -Depth 24 -Compress))))).Replace('-','')}
    finally {$algorithm.Dispose()}
}
function Save-RemotingState($state) {
    Assert-PlainPath $script:RemotingRoot
    [void][IO.Directory]::CreateDirectory($script:RemotingRoot)
    $acl=New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true,$false)
    foreach ($sid in @('S-1-5-18','S-1-5-32-544')) {
        $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier($sid)),'FullControl','ContainerInherit,ObjectInherit','None','Allow')))
    }
    Set-Acl -LiteralPath $script:RemotingRoot -AclObject $acl
    $path=Join-Path $script:RemotingRoot 'checkpoint.json';Assert-PlainPath $path
    $envelope=@{Content=($state | ConvertTo-Json -Depth 24 -Compress);Sha256=(Hash-Value $state)}
    $temp=Join-Path $script:RemotingRoot ([guid]::NewGuid().ToString('N')+'.tmp')
    try {
        $bytes=[Text.Encoding]::UTF8.GetBytes(($envelope | ConvertTo-Json -Compress))
        $stream=[IO.File]::Open($temp,'CreateNew','Write','None')
        try {$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)} finally {$stream.Dispose()}
        if ([IO.File]::Exists($path)) {[IO.File]::Replace($temp,$path,$null)} else {[IO.File]::Move($temp,$path)}
    } finally {if ([IO.File]::Exists($temp)) {[IO.File]::Delete($temp)}}
}
function Load-RemotingState {
    $path=Join-Path $script:RemotingRoot 'checkpoint.json'
    if (!(Test-Path -LiteralPath $path)) {return $null}
    Assert-PlainPath $path
    if ((Get-Item -LiteralPath $path).Length -gt 262144) {throw 'CheckpointTooLarge'}
    $envelope=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    $state=$envelope.Content | ConvertFrom-Json
    if ($state.SchemaVersion -ne 1 -or $state.Computer -cne $env:COMPUTERNAME -or (Hash-Value $state) -cne $envelope.Sha256 -or
        $state.Phase -notin @('Applying','Applied','UndoPending','Restored') -or !$state.Before -or !$state.Last) {throw 'InvalidCheckpoint'}
    return $state
}
function Read-LocalRemotingConfiguration {
    $service=Get-CimInstance Win32_Service -Filter "Name='WinRM'"
    if (!$service -or $service.State -notin @('Running','Stopped')) {throw 'ServiceStateUnavailable'}
    $listeners=@();$endpoint=$null
    if ($service.State -eq 'Running') {
        $listeners=@(Get-ChildItem WSMan:\localhost\Listener | Sort-Object Name | ForEach-Object {
            [ordered]@{Name=$_.Name;Keys=@($_.Keys | Sort-Object);Values=@(Get-ChildItem -LiteralPath $_.PSPath | Sort-Object Name | Select-Object Name,Value)}
        })
        $item=Get-PSSessionConfiguration -Name $script:EndpointName -ErrorAction SilentlyContinue
        if ($item) {$endpoint=[ordered]@{Name=$item.Name;Sddl=[string]$item.SecurityDescriptorSddl}}
    } else {
        if (@(Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WSMAN\Listener' -ErrorAction SilentlyContinue).Count -gt 0 -or
            (Test-Path ('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WSMAN\Plugin\'+$script:EndpointName))) {throw 'StoppedServiceWithExistingConfiguration'}
    }
    $rule=$null;$item=Get-NetFirewallRule -PolicyStore PersistentStore -Name $script:RuleName -ErrorAction SilentlyContinue
    if ($item) {
        $address=$item | Get-NetFirewallAddressFilter;$port=$item | Get-NetFirewallPortFilter;$interface=$item | Get-NetFirewallInterfaceFilter
        $serviceFilter=$item | Get-NetFirewallServiceFilter;$application=$item | Get-NetFirewallApplicationFilter
        $rule=[ordered]@{Name=$item.Name;Enabled=[string]$item.Enabled;Profile=[string]$item.Profile;Direction=[string]$item.Direction;Action=[string]$item.Action;
            LocalAddress=@($address.LocalAddress);RemoteAddress=@($address.RemoteAddress);Protocol=[string]$port.Protocol;LocalPort=@($port.LocalPort);RemotePort=@($port.RemotePort);
            InterfaceAlias=@($interface.InterfaceAlias);Service=[string]$serviceFilter.Service;Program=[string]$application.Program;EdgeTraversalPolicy=[string]$item.EdgeTraversalPolicy}
    }
    [ordered]@{Service=[ordered]@{State=$service.State;StartMode=$service.StartMode};Listeners=@($listeners);Endpoint=$endpoint;Rule=$rule}
}
function Get-PrivateRemotingNetwork([int]$index) {
    if ($index -lt 1) {throw 'InvalidInterface'}
    $profile=@(Get-NetConnectionProfile -InterfaceIndex $index -ErrorAction Stop)
    if ($profile.Count -ne 1 -or [string]$profile[0].NetworkCategory -ne 'Private') {throw 'PrivateNetworkRequired'}
    $addresses=@(Get-NetIPAddress -InterfaceIndex $index -AddressFamily IPv4 | Where-Object {$_.AddressState -eq 'Preferred' -and $_.IPAddress -notlike '169.254.*'})
    if ($addresses.Count -ne 1 -or $addresses[0].PrefixLength -lt 8 -or $addresses[0].PrefixLength -gt 30) {throw 'SinglePrivateIPv4Required'}
    $ip=$addresses[0].IPAddress;$length=[int]$addresses[0].PrefixLength
    $bytes=[Net.IPAddress]::Parse($ip).GetAddressBytes();$network=New-Object byte[] 4
    for ($i=0;$i -lt 4;$i++) {$bits=[Math]::Min(8,[Math]::Max(0,$length-8*$i));$mask=if($bits -eq 0){0}else{256-[Math]::Pow(2,8-$bits)};$network[$i]=[byte]($bytes[$i] -band [int]$mask)}
    [ordered]@{InterfaceIndex=$index;Alias=[string]$profile[0].InterfaceAlias;Address=$ip;Subnet=([Net.IPAddress]::new($network).ToString()+'/'+$length)}
}
function Assert-RemotingFirewall {
    $profile=Get-NetFirewallProfile -PolicyStore ActiveStore -Name Private
    if ([string]$profile.Enabled -ne 'True' -or [string]$profile.DefaultInboundAction -ne 'Block' -or
        [string]$profile.AllowLocalFirewallRules -eq 'False' -or [string]$profile.AllowInboundRules -eq 'False') {throw 'PrivateFirewallRequired'}
    foreach ($rule in @(Get-NetFirewallRule -PolicyStore ActiveStore -Direction Inbound -Enabled True -Action Allow)) {
        $port=$rule | Get-NetFirewallPortFilter;$service=$rule | Get-NetFirewallServiceFilter;$application=$rule | Get-NetFirewallApplicationFilter
        if ([string]$port.Protocol -in @('TCP','6','Any','256') -and
            (@($port.LocalPort | Where-Object {$_ -in @('5985','5986') -or $_ -match '^\d+-\d+$'}).Count -gt 0 -or
             ('Any' -in @($port.LocalPort) -and ([string]$service.Service -eq 'WinRM' -or
              ([string]$service.Service -eq 'Any' -and [string]$application.Program -in @('Any','System')))))) {throw 'ExistingIngressConflict'}
    }
}
function Update-RemotingJournal($state) {$state.Last=Read-LocalRemotingConfiguration;Save-RemotingState $state}
function Subnet-Canonical([string]$value) {
    $parts=$value.Split('/');if($parts.Count -ne 2){return ''}
    $ip=$null;if(![Net.IPAddress]::TryParse($parts[0],[ref]$ip) -or $ip.AddressFamily -ne 'InterNetwork'){return ''}
    $length=0
    if($parts[1].Contains('.')) {
        $mask=$null;if(![Net.IPAddress]::TryParse($parts[1],[ref]$mask) -or $mask.AddressFamily -ne 'InterNetwork'){return ''}
        $bits=(@($mask.GetAddressBytes() | ForEach-Object {[Convert]::ToString($_,2).PadLeft(8,'0')}) -join '')
        if($bits -notmatch '^1*0*$'){return ''};$length=$bits.Replace('0','').Length
    } elseif(![int]::TryParse($parts[1],[ref]$length) -or $length -lt 0 -or $length -gt 32){return ''}
    return $ip.ToString()+'/'+$length
}
function Invoke-LocalRemoting([string]$action,[int]$index=0) {
    Assert-Administrator
    $current=Read-LocalRemotingConfiguration;$saved=Load-RemotingState
    if ($action -eq 'winrm.inspect') {
        $configured=$false
        if ($saved -and $saved.Phase -eq 'Applied' -and (Hash-Value $current) -ceq (Hash-Value $saved.Last)) {
            try {
                $effective=Get-NetFirewallRule -PolicyStore ActiveStore -Name $script:RuleName -ErrorAction Stop
                $configured=($effective -and [string]$effective.Enabled -eq 'True' -and
                    (Hash-Value (Get-PrivateRemotingNetwork $saved.Network.InterfaceIndex)) -ceq (Hash-Value $saved.Network))
            } catch {$configured=$false}
        }
        return @{Action=$action;Computer=$env:COMPUTERNAME;ReadComplete=$true;Configured=[bool]$configured;
            Restored=($saved -and $saved.Phase -eq 'Restored' -and (Hash-Value $current) -ceq (Hash-Value $saved.Before));
            Configuration=$current;CheckpointPhase=$(if($saved){$saved.Phase}else{'Absent'})}
    }
    if ($action -eq 'winrm.undo') {
        if (!$saved -or $saved.Phase -eq 'Restored') {throw 'NoActiveCheckpoint'}
        if ((Hash-Value $current) -cne (Hash-Value $saved.Last)) {throw 'ExternalConfigurationChange'}
        $saved.Phase='UndoPending';Save-RemotingState $saved
        if ($current.Rule) {Remove-NetFirewallRule -PolicyStore PersistentStore -Name $script:RuleName;Update-RemotingJournal $saved}
        if ($current.Endpoint) {Unregister-PSSessionConfiguration -Name $script:EndpointName -NoServiceRestart -Force;Update-RemotingJournal $saved}
        foreach ($listener in @($current.Listeners)) {
            if ($listener.Keys -notcontains ('Address=IP:'+$saved.Network.Address) -or $listener.Keys -notcontains 'Transport=HTTP') {throw 'ListenerOwnershipConflict'}
            $owned=@(Get-ChildItem WSMan:\localhost\Listener | Where-Object {$_.Name -ceq $listener.Name})
            if ($owned.Count -ne 1) {throw 'ListenerOwnershipConflict'}
            Remove-Item -LiteralPath $owned[0].PSPath -Recurse;Update-RemotingJournal $saved
        }
        $startup=switch ($saved.Before.Service.StartMode) {'Auto' {'Automatic'} 'Manual' {'Manual'} 'Disabled' {'Disabled'} default {throw 'UnknownStartupMode'}}
        if ($saved.Before.Service.State -eq 'Stopped') {Stop-Service WinRM -ErrorAction Stop}
        Set-Service WinRM -StartupType $startup
        $current=Read-LocalRemotingConfiguration
        if ((Hash-Value $current) -cne (Hash-Value $saved.Before)) {throw 'RestoreVerificationFailed'}
        $saved.Last=$current;$saved.Phase='Restored';Save-RemotingState $saved
        return @{Action=$action;Computer=$env:COMPUTERNAME;ReadComplete=$true;Configured=$false;Restored=$true;Configuration=$current}
    }
    if ($action -cne ('winrm.enable.'+$index)) {throw 'InvalidAction'}
    $network=Get-PrivateRemotingNetwork $index
    if ($saved -and $saved.Phase -ne 'Restored') {throw 'ActiveCheckpointRequiresRestore'}
    if (@($current.Listeners).Count -gt 0 -or $current.Endpoint -or $current.Rule) {throw 'ExistingConfigurationConflict'}
    Assert-RemotingFirewall
    $state=[pscustomobject]@{SchemaVersion=1;Computer=$env:COMPUTERNAME;Phase='Applying';Network=$network;Before=$current;Last=$current}
    Save-RemotingState $state
    Set-Service WinRM -StartupType Automatic;Start-Service WinRM;Update-RemotingJournal $state
    # Bind only to the selected private address. No wildcard listener or automatic firewall exception.
    New-Item -Path WSMan:\localhost\Listener -Address ('IP:'+$network.Address) -Transport HTTP -Port 5985 -Force | Out-Null
    Update-RemotingJournal $state
    Register-PSSessionConfiguration -Name $script:EndpointName -SecurityDescriptorSddl $script:AdminSddl -AccessMode Remote -NoServiceRestart -Force | Out-Null
    Update-RemotingJournal $state
    Restart-Service WinRM -ErrorAction Stop;Update-RemotingJournal $state
    # Firewall opening is the last mutation, after endpoint restrictions and selected network are reread.
    if ((Hash-Value (Get-PrivateRemotingNetwork $index)) -cne (Hash-Value $network)) {throw 'NetworkChanged'}
    if (!$state.Last.Endpoint -or !$state.Last.Endpoint.Sddl) {throw 'EndpointSecurityNotReadable'}
    $actual=New-Object Security.AccessControl.RawSecurityDescriptor($state.Last.Endpoint.Sddl)
    $expected=New-Object Security.AccessControl.RawSecurityDescriptor($script:AdminSddl)
    if ($actual.GetSddlForm('All') -cne $expected.GetSddlForm('All')) {throw 'EndpointSecurityMismatch'}
    Assert-RemotingFirewall
    New-NetFirewallRule -PolicyStore PersistentStore -Name $script:RuleName -DisplayName 'OnlyTweak: PowerShell LAN privata' -Direction Inbound -Action Allow -Enabled False `
        -Profile Private -InterfaceAlias $network.Alias -LocalAddress $network.Address -RemoteAddress $network.Subnet -Protocol TCP -LocalPort 5985 -Service WinRM | Out-Null
    Update-RemotingJournal $state
    Enable-NetFirewallRule -PolicyStore PersistentStore -Name $script:RuleName;Update-RemotingJournal $state
    $effective=Get-NetFirewallRule -PolicyStore ActiveStore -Name $script:RuleName -ErrorAction Stop
    if (!$state.Last.Rule -or $state.Last.Rule.Enabled -ne 'True' -or $state.Last.Rule.Profile -ne 'Private' -or
        $state.Last.Rule.LocalAddress -notcontains $network.Address -or
        @($state.Last.Rule.RemoteAddress | Where-Object {(Subnet-Canonical $_) -ceq $network.Subnet}).Count -ne 1 -or
        !$effective -or [string]$effective.Enabled -ne 'True') {throw 'FirewallVerificationFailed'}
    $state.Phase='Applied';Save-RemotingState $state
    return @{Action=$action;Computer=$env:COMPUTERNAME;ReadComplete=$true;Configured=$true;Restored=$false;Endpoint=$script:EndpointName;Network=$network;Configuration=$state.Last}
}
# Entry point: a single bounded, typed request on stdin; no command text or external script path.
$result=@{State='Inconclusive';Summary='WinRM non verificato.';Evidence=@();AuthenticationVerified=$false;ErrorCode='';Data=$null}
try {
    $line=[Console]::In.ReadLine()
    if (!$line -or $line.Length -gt 16384) {throw 'InvalidRequest'}
    $request=$line | ConvertFrom-Json;$action=[string]$request.ActionId;$index=0
    if ($action -match '^winrm\.enable\.([1-9][0-9]{0,8})$') {$index=[int]$Matches[1]}
    elseif ($action -notin @('winrm.inspect','winrm.undo')) {throw 'InvalidAction'}
    if ($action -ne 'winrm.inspect' -and $request.Confirmed -ne $true) {throw 'ConfirmationRequired'}
    $result.Data=Invoke-LocalRemoting $action $index
    $result.State='Success';$result.Summary=if($action -eq 'winrm.inspect'){'Configurazione locale letta; autenticazione da un altro PC non collaudata.'}elseif($action -eq 'winrm.undo'){'Configurazione precedente ripristinata e riletta.'}else{'WinRM configurato sulla LAN privata; connessione e credenziali remote da collaudare.'}
} catch {
    $result.State=if($action -eq 'winrm.inspect'){'Inconclusive'}else{'Unverified'}
    $result.ErrorCode='LocalRemotingNotVerified';$result.Summary='Operazione non verificata: '+$_.Exception.Message+'. Verifica stato e checkpoint prima di riprovare.'
}
$result | ConvertTo-Json -Depth 28 -Compress
