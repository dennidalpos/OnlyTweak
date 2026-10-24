# Windows PowerShell 5.1. Review and sign before enterprise deployment.
[CmdletBinding(SupportsShouldProcess=$true,ConfirmImpact='High')]
param(
    [ValidateSet('Inspect','Apply','Undo')][string]$Mode='Inspect',
    [string[]]$ManagementAddresses,
    [string]$AllowedGroupSid,
    [switch]$UseHttps,
    [string]$CertificateThumbprint
)
$ErrorActionPreference='Stop'
$root=Join-Path $env:ProgramData 'OnlyTweak\Remoting'
$statePath=Join-Path $root 'state.json'
$moduleRoot=Join-Path $env:ProgramFiles 'WindowsPowerShell\Modules\OnlyTweakEndpoint'
$ruleName='OnlyTweak-Remoting'
$endpoint='OnlyTweak'
function Assert-Plain([string]$path) {
    $info=New-Object IO.DirectoryInfo $path
    while ($info) {
        if ($info.Exists -and ($info.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'ReparsePointNotAllowed' }
        $info=$info.Parent
    }
}
function Protect-Directory([string]$path) {
    Assert-Plain $path
    [void][IO.Directory]::CreateDirectory($path)
    $acl=New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true,$false)
    foreach ($sid in @('S-1-5-18','S-1-5-32-544')) {
        $identity=New-Object Security.Principal.SecurityIdentifier $sid
        $access=New-Object Security.AccessControl.FileSystemAccessRule($identity,'FullControl','ContainerInherit,ObjectInherit','None','Allow')
        $acl.AddAccessRule($access)
    }
    Set-Acl -LiteralPath $path -AclObject $acl
}
function Save-State($value) {
    Protect-Directory $root
    $tmp=Join-Path $root ([guid]::NewGuid().ToString('N')+'.tmp')
    try {
        $value | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $tmp -Encoding UTF8
        Move-Item -LiteralPath $tmp -Destination $statePath -Force
    } finally { if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp } }
}
function Fingerprint($value) {
    $json=$value | ConvertTo-Json -Depth 20 -Compress
    $algorithm=[Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($algorithm.ComputeHash([Text.Encoding]::UTF8.GetBytes($json)))).Replace('-','') }
    finally { $algorithm.Dispose() }
}
function Listener-State([string]$transport) {
    @(Get-ChildItem WSMan:\localhost\Listener | Where-Object { $_.Keys -contains ('Transport='+$transport) -and $_.Keys -contains 'Address=*' } |
        ForEach-Object { [pscustomobject]@{Keys=@($_.Keys | Sort-Object); Values=@(Get-ChildItem -LiteralPath $_.PSPath |
            Select-Object Name,Value | Sort-Object Name)} })
}
function Rule-State {
    $rule=Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue
    if ($rule) {
        [pscustomobject]@{Rule=($rule | Select-Object Name,DisplayName,Enabled,Profile,Direction,Action,PolicyStoreSourceType);
            Address=($rule | Get-NetFirewallAddressFilter | Select-Object LocalAddress,RemoteAddress);
            Port=($rule | Get-NetFirewallPortFilter | Select-Object Protocol,LocalPort,RemotePort);
            Service=($rule | Get-NetFirewallServiceFilter | Select-Object Service)}
    }
}
function Endpoint-State {
    $item=Get-PSSessionConfiguration -Name $endpoint -ErrorAction SilentlyContinue
    if ($item) { $item | Select-Object Name,ConfigFilePath,Permission,SecurityDescriptorSddl,RunAsUser,SessionType }
}
$machine=Get-CimInstance Win32_ComputerSystem
$policy=Test-Path -LiteralPath 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WinRM\Service'
if ($Mode -eq 'Inspect') {
    [pscustomobject]@{Computer=$env:COMPUTERNAME; DomainRole=$machine.DomainRole; ManagedByPolicy=$policy;
        Service=(Get-Service WinRM | Select-Object Status,StartType); SnapshotPresent=(Test-Path -LiteralPath $statePath);
        ExistingFirewall=@(Get-NetFirewallRule -Name '*WINRM*','OnlyTweak-*' -ErrorAction SilentlyContinue |
            Select-Object Name,Enabled,Profile,PolicyStoreSourceType)}
    return
}
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole('Administrator')) { throw 'AdministratorRequired' }
if ($machine.DomainRole -ge 4 -or -not $machine.PartOfDomain) { throw 'DomainMemberRequiredNoDomainControllers' }
if ($policy) { throw 'ManagedByGroupPolicyUseCentralConfiguration' }
Assert-Plain $root
Assert-Plain $moduleRoot
if ($Mode -eq 'Undo') {
    if (-not (Test-Path -LiteralPath $statePath)) { throw 'SnapshotMissing' }
    $saved=Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    if ($saved.Computer -ine $env:COMPUTERNAME) { throw 'SnapshotMachineMismatch' }
    if ($saved.Restored) { throw 'SnapshotAlreadyRestored' }
    if ($saved.Phase -ne 'Applied') { throw 'PartialDeploymentNeedsManualRecoveryFromSnapshot' }
    foreach ($entry in $saved.FileHashes) {
        Assert-Plain (Split-Path $entry.Path)
        if (-not (Test-Path -LiteralPath $entry.Path) -or (Get-Item -LiteralPath $entry.Path).Attributes -band [IO.FileAttributes]::ReparsePoint -or
            (Get-FileHash -LiteralPath $entry.Path).Hash -ne $entry.Hash) { throw 'ManagedFileChangedManualReviewRequired' }
    }
    $currentRule=Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue
    if ((Fingerprint (Rule-State)) -ne $saved.FirewallHash) { throw 'FirewallChangedManualReviewRequired' }
    if ((Fingerprint (Endpoint-State)) -ne $saved.EndpointHash) { throw 'EndpointChangedManualReviewRequired' }
    if ($saved.CreatedListener -and (Fingerprint (Listener-State $saved.Transport)) -ne $saved.ListenerHash) { throw 'ListenerChangedManualReviewRequired' }
    $currentService=Get-CimInstance Win32_Service -Filter "Name='WinRM'"
    if ($currentService.StartMode -ne 'Auto') { throw 'ServiceChangedManualReviewRequired' }
    # Restore service only if no other endpoints/listeners were introduced since apply.
    if ((Fingerprint @(Get-PSSessionConfiguration | Select-Object Name | Sort-Object Name)) -ne $saved.EndpointsHash -or
        (Fingerprint @(Get-ChildItem WSMan:\localhost\Listener | Select-Object Name,Keys | Sort-Object Name)) -ne $saved.ListenersHash) { throw 'SharedWinrmConfigurationChangedManualReviewRequired' }
    if ($PSCmdlet.ShouldProcess($env:COMPUTERNAME,'Ripristina solamente gli artefatti OnlyTweak registrati nello snapshot')) {
        if (Get-PSSessionConfiguration -Name $endpoint -ErrorAction SilentlyContinue) { Unregister-PSSessionConfiguration -Name $endpoint -Force }
        if ($currentRule) { Remove-NetFirewallRule -Name $ruleName }
        if ($saved.CreatedListener) {
            $listener=Get-ChildItem WSMan:\localhost\Listener | Where-Object { $_.Keys -contains ('Transport='+$saved.Transport) -and $_.Keys -contains 'Address=*' }
            if ($listener) { $listener | Remove-Item -Recurse -Force }
        }
        # Exact files only; no recursive directory deletion.
        foreach ($relative in @('OnlyTweakEndpoint.psm1','OnlyTweakEndpoint.psd1','RoleCapabilities\OnlyTweakEndpoint.psrc')) {
            $file=Join-Path $moduleRoot $relative
            if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
        }
        $configuration=Join-Path $root 'OnlyTweak.pssc'
        if (Test-Path -LiteralPath $configuration) { Remove-Item -LiteralPath $configuration }
        foreach ($folder in @((Join-Path $moduleRoot 'RoleCapabilities'),$moduleRoot)) {
            if ((Test-Path -LiteralPath $folder) -and @(Get-ChildItem -LiteralPath $folder -Force).Count -eq 0) { [IO.Directory]::Delete($folder) }
        }
        if ($saved.StartMode -eq 'Auto') { Set-Service WinRM -StartupType Automatic }
        elseif ($saved.StartMode -eq 'Manual') { Set-Service WinRM -StartupType Manual }
        else { Set-Service WinRM -StartupType Disabled }
        if ($saved.ServiceState -eq 'Stopped') { Stop-Service WinRM -ErrorAction Stop }
        $saved | Add-Member -NotePropertyName Restored -NotePropertyValue (Get-Date).ToUniversalTime().ToString('o') -Force
        Save-State $saved
    }
    return
}
if (Test-Path -LiteralPath $statePath) { throw 'SnapshotAlreadyPresentInspectOrUndoFirst' }
if (Test-Path -LiteralPath $moduleRoot) { throw 'ModuleAlreadyPresent' }
if (-not $ManagementAddresses -or -not $AllowedGroupSid) { throw 'ManagementAddressesAndDedicatedGroupRequired' }
foreach ($address in $ManagementAddresses) {
    if ($address -notmatch '^([0-9A-Fa-f:.]+)/([0-9]{1,3})$') { throw 'ExplicitCidrRequired' }
    $ip=$null
    if (-not [Net.IPAddress]::TryParse($Matches[1],[ref]$ip)) { throw 'InvalidManagementAddress' }
    $prefix=[int]$Matches[2]
    $max=if ($ip.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetwork) {32} else {128}
    if ($prefix -lt 8 -or $prefix -gt $max) { throw 'ManagementRangeTooBroad' }
}
$sid=New-Object Security.Principal.SecurityIdentifier $AllowedGroupSid
if ($AllowedGroupSid -notmatch '^S-1-5-21-.+-[0-9]+$' -or $AllowedGroupSid -match '-(500|512|513|516|518|519)$') { throw 'DedicatedDelegatedGroupRequired' }
$group=$sid.Translate([Security.Principal.NTAccount]).Value
$transport=if ($UseHttps) {'HTTPS'} else {'HTTP'}
$port=if ($UseHttps) {5986} else {5985}
if ($UseHttps) {
    if ($CertificateThumbprint -notmatch '^[0-9A-Fa-f]{40}$') { throw 'CertificateThumbprintRequired' }
    $cert=Get-Item -LiteralPath ('Cert:\LocalMachine\My\'+$CertificateThumbprint)
    if (-not $cert.HasPrivateKey -or $cert.NotAfter -lt (Get-Date) -or $cert.NotBefore -gt (Get-Date) -or
        @($cert.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq '1.3.6.1.5.5.7.3.1' }).Count -eq 0) { throw 'ValidServerAuthenticationCertificateRequired' }
    $fqdn=[Net.Dns]::GetHostEntry($env:COMPUTERNAME).HostName
    if (@($cert.DnsNameList | Where-Object { $_.Unicode -ieq $fqdn }).Count -eq 0) { throw 'ExactFqdnCertificateRequired' }
    $chain=New-Object Security.Cryptography.X509Certificates.X509Chain
    try { if (-not $chain.Build($cert)) { throw 'CertificateChainNotTrusted' } } finally { $chain.Dispose() }
}
$service=Get-CimInstance Win32_Service -Filter "Name='WinRM'"
if (Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue) { throw 'FirewallNameConflict' }
if (Get-PSSessionConfiguration -Name $endpoint -ErrorAction SilentlyContinue) { throw 'EndpointNameConflict' }
$source=Join-Path $PSScriptRoot 'OnlyTweakEndpoint.psm1'
if (-not (Test-Path -LiteralPath $source)) { throw 'ReviewedModuleMissing' }
if (-not $PSCmdlet.ShouldProcess($env:COMPUTERNAME,('Configura endpoint JEA '+$endpoint+', '+$transport+', firewall Domain da '+($ManagementAddresses -join ',')+', gruppo '+$group))) { return }
$snapshot=[pscustomobject]@{Computer=$env:COMPUTERNAME; StartMode=$service.StartMode; ServiceState=$service.State;
    Transport=$transport; CreatedListener=$false; ModuleHash=(Get-FileHash -LiteralPath $source).Hash;
    FirewallDisplayName='OnlyTweak remoting '+[guid]::NewGuid().ToString('N'); Phase='Snapshot'; Restored=$null;
    FileHashes=@(); FirewallHash=''; EndpointHash=''; ListenerHash=''; EndpointsHash=''; ListenersHash=''}
Save-State $snapshot
try {
    Set-Service WinRM -StartupType Automatic
    Start-Service WinRM
    $listener=Get-ChildItem WSMan:\localhost\Listener | Where-Object { $_.Keys -contains ('Transport='+$transport) -and $_.Keys -contains 'Address=*' }
    if (-not $listener) {
        $values=@{Address='*'; Transport=$transport; Force=$true}
        if ($UseHttps) { $values.CertificateThumbprint=$CertificateThumbprint }
        New-Item WSMan:\localhost\Listener @values | Out-Null
        $snapshot.CreatedListener=$true; Save-State $snapshot
    }
    Protect-Directory $moduleRoot
    Copy-Item -LiteralPath $source -Destination (Join-Path $moduleRoot 'OnlyTweakEndpoint.psm1')
    New-ModuleManifest -Path (Join-Path $moduleRoot 'OnlyTweakEndpoint.psd1') -RootModule 'OnlyTweakEndpoint.psm1' -ModuleVersion '2.0.0' -FunctionsToExport Get-OnlyTweakEndpointEvidence,Invoke-OnlyTweakEndpointChange
    $roles=Join-Path $moduleRoot 'RoleCapabilities'; Protect-Directory $roles
    New-PSRoleCapabilityFile -Path (Join-Path $roles 'OnlyTweakEndpoint.psrc') -ModulesToImport OnlyTweakEndpoint -VisibleFunctions Get-OnlyTweakEndpointEvidence,Invoke-OnlyTweakEndpointChange
    $rolesMap=@{}; $rolesMap[$group]=@{RoleCapabilities='OnlyTweakEndpoint'}
    $configuration=Join-Path $root 'OnlyTweak.pssc'
    $transcripts=Join-Path $root 'Transcripts'; Protect-Directory $transcripts
    New-PSSessionConfigurationFile -Path $configuration -SessionType RestrictedRemoteServer -RunAsVirtualAccount -RoleDefinitions $rolesMap -TranscriptDirectory $transcripts
    Register-PSSessionConfiguration -Name $endpoint -Path $configuration -Force
    New-NetFirewallRule -Name $ruleName -DisplayName $snapshot.FirewallDisplayName -Direction Inbound -Action Allow -Enabled True -Profile Domain -Protocol TCP -LocalPort $port -RemoteAddress $ManagementAddresses -Service WinRM | Out-Null
    $snapshot.FileHashes=@(foreach ($file in @((Join-Path $moduleRoot 'OnlyTweakEndpoint.psm1'),(Join-Path $moduleRoot 'OnlyTweakEndpoint.psd1'),
        (Join-Path $roles 'OnlyTweakEndpoint.psrc'),$configuration)) {
        [pscustomobject]@{Path=$file; Hash=(Get-FileHash -LiteralPath $file).Hash}
    })
    $snapshot.FirewallHash=Fingerprint (Rule-State)
    $snapshot.EndpointHash=Fingerprint (Endpoint-State)
    $snapshot.ListenerHash=Fingerprint (Listener-State $transport)
    $snapshot.EndpointsHash=Fingerprint @(Get-PSSessionConfiguration | Select-Object Name | Sort-Object Name)
    $snapshot.ListenersHash=Fingerprint @(Get-ChildItem WSMan:\localhost\Listener | Select-Object Name,Keys | Sort-Object Name)
    $snapshot.Phase='Applied'; Save-State $snapshot
    'Configurazione applicata. Verificare accessi consentiti/negati e configurazioni firewall preesistenti.'
} catch {
    $snapshot.Phase='Partial'; Save-State $snapshot
    throw 'ProvisioningPartialInspectSnapshotAndUseUndoNoAutomaticRollback'
}
