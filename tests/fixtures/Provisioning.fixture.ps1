# Only simulated storage, PKI, services, WSMan and firewall. No real Apply/Undo.
$ErrorActionPreference='Stop'
$provisionSource=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__PROVISION_SOURCE_BASE64__'))
$provisionMocks=@'
function Assert-Plain {param($path)}
function Protect-Directory {param($path) $global:writes++}
function Save-State {param($value) $global:writes++; $value | Add-Member NoteProperty CheckpointHash (Checkpoint-Hash $value) -Force; $global:files[$statePath]=$value | ConvertTo-Json -Depth 12}
function Test-Path {param($LiteralPath) $global:files.ContainsKey($LiteralPath)}
function Get-Content {param($LiteralPath,[switch]$Raw) $global:files[$LiteralPath]}
function Get-FileHash {param($LiteralPath) $alg=[Security.Cryptography.SHA256]::Create();try {[pscustomobject]@{Hash=([BitConverter]::ToString($alg.ComputeHash([Text.Encoding]::UTF8.GetBytes($global:files[$LiteralPath])))).Replace('-','')}}finally{$alg.Dispose()}}
function Get-CimInstance {param($ClassName,$Filter) if($ClassName -eq 'Win32_ComputerSystem'){[pscustomobject]@{DomainRole=1;PartOfDomain=$true}}else{[pscustomobject]@{StartMode=$global:startMode;State=$global:serviceState}}}
function Get-Service {[pscustomobject]@{Status=$global:serviceState;StartType=$global:startMode}}
function Set-Service {param($Name,$StartupType) $global:writes++;$global:startMode=switch($StartupType){'Automatic'{'Auto'} 'Manual'{'Manual'} default{'Disabled'}}}
function Start-Service {$global:writes++;$global:serviceState='Running'}
function Stop-Service {$global:writes++;$global:serviceState='Stopped'}
function Get-Item {param($LiteralPath) if($LiteralPath -like 'Cert:*'){[pscustomobject]@{HasPrivateKey=$true;NotAfter=(Get-Date).AddDays(1);NotBefore=(Get-Date).AddDays(-1);EnhancedKeyUsageList=@([pscustomobject]@{ObjectId='1.3.6.1.5.5.7.3.1'});DnsNameList=@([pscustomobject]@{Unicode='host.corp.example'})}}else{[pscustomobject]@{Attributes=0}}}
function New-Object {param($TypeName,$ArgumentList) if($TypeName -eq 'Security.Cryptography.X509Certificates.X509Chain'){$c=[pscustomobject]@{};$c|Add-Member ScriptMethod Build {$true};$c|Add-Member ScriptMethod Dispose {};return $c};Microsoft.PowerShell.Utility\New-Object -TypeName $TypeName -ArgumentList $ArgumentList}
function Get-ChildItem {param($Path,$LiteralPath,[switch]$Force) if($Path -eq 'WSMan:\localhost\Listener'){if($global:listenerPresent){[pscustomobject]@{Name='Listener_fixture';Keys=@('Transport=HTTPS','Address=*');PSPath='FixtureListener'}}}elseif($LiteralPath -eq 'FixtureListener'){foreach($n in $global:listenerSettings.Keys){[pscustomobject]@{Name=$n;Value=$global:listenerSettings[$n]}}}}
function Get-NetFirewallRule {if($global:firewall){[pscustomobject]@{Name='OnlyTweak-Remoting';DisplayName=$global:firewallName;Enabled=$true;Profile='Domain';Direction='Inbound';Action='Allow';PolicyStoreSourceType='Local'}}}
function Get-NetFirewallAddressFilter {[pscustomobject]@{LocalAddress='Any';RemoteAddress='10.20.30.0/24'}}
function Get-NetFirewallPortFilter {[pscustomobject]@{Protocol='TCP';LocalPort='5986';RemotePort='Any'}}
function Get-NetFirewallServiceFilter {[pscustomobject]@{Service='WinRM'}}
function Get-PSSessionConfiguration {if($global:endpointPresent){[pscustomobject]@{Name='OnlyTweak';ConfigFilePath='fixture';Permission='fixture';SecurityDescriptorSddl='fixture';RunAsUser='fixture';SessionType='RestrictedRemoteServer'}}}
function Copy-Item {param($LiteralPath,$Destination) $global:writes++;$global:files[$Destination]=$global:files[$LiteralPath]}
function New-ModuleManifest {param($Path,$RootModule,$ModuleVersion,$FunctionsToExport) $global:writes++;$global:files[$Path]='manifest'}
function New-PSRoleCapabilityFile {param($Path,$ModulesToImport,$VisibleFunctions) $global:writes++;$global:files[$Path]='role'}
function New-PSSessionConfigurationFile {param($Path,$SessionType,[switch]$RunAsVirtualAccount,$RoleDefinitions,$TranscriptDirectory) $global:writes++;$global:files[$Path]='configuration'}
function Register-PSSessionConfiguration {param($Name,$Path,[switch]$Force) $global:writes++;$global:endpointPresent=$true}
function Unregister-PSSessionConfiguration {param($Name,[switch]$Force) $global:writes++;$global:endpointPresent=$false}
function New-NetFirewallRule {param($Name,$DisplayName,$Direction,$Action,$Enabled,$Profile,$Protocol,$LocalPort,$RemoteAddress,$Service) $global:writes++;$global:firewall=$true;$global:firewallName=$DisplayName}
function Remove-NetFirewallRule {param($Name) $global:writes++;$global:firewall=$false}
function New-Item {throw 'FixtureUnexpectedListenerCreation'}
function Remove-Item {param($LiteralPath) $global:writes++;$global:files.Remove($LiteralPath)}
'@
$provisionSource=$provisionSource.Replace('$machine=Get-CimInstance',$provisionMocks+"`n"+'$machine=Get-CimInstance').Replace("if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole('Administrator'))",'if ($false)').Replace('$group=$sid.Translate([Security.Principal.NTAccount]).Value',"`$group='CORP\Delegated'").Replace('[Net.Dns]::GetHostEntry($env:COMPUTERNAME).HostName',"'host.corp.example'").Replace('Join-Path $PSScriptRoot', 'Join-Path $env:TEMP')
$apply=[scriptblock]::Create($provisionSource)
function Reset-Fixture {
    $global:writes=0;$global:files=@{};$global:files[(Join-Path $env:TEMP 'OnlyTweakEndpoint.psm1')]='module';$global:startMode='Manual';$global:serviceState='Stopped';$global:firewall=$false;$global:endpointPresent=$false;$global:listenerPresent=$true
    $global:listenerSettings=@{Enabled='true';Port='5986';Transport='HTTPS';Hostname='host.corp.example';CertificateThumbprint=('A'*40);URLPrefix='wsman'}
}
function Apply-Fixture([switch]$WhatIf) {
    & $apply -Mode Apply -UseHttps -CertificateThumbprint ('A'*40) -ManagementAddresses '10.20.30.0/24' -AllowedGroupSid 'S-1-5-21-100-200-300-1234' -Confirm:$false -WhatIf:$WhatIf 6>$null | Out-Null
}
foreach($conflict in @('cert','port','disabled','hostname','prefix','transport')) {
    Reset-Fixture
    switch($conflict){'cert'{$global:listenerSettings.CertificateThumbprint='B'*40} 'port'{$global:listenerSettings.Port='6000'} 'disabled'{$global:listenerSettings.Enabled='false'} 'hostname'{$global:listenerSettings.Hostname='other.example'} 'prefix'{$global:listenerSettings.URLPrefix='other'} 'transport'{$global:listenerSettings.Transport='HTTP'}}
    $blocked=$false;try{Apply-Fixture}catch{$blocked=$true}
    if(-not $blocked -or $global:writes -ne 0){throw ('PreflightDidNotBlock:'+ $conflict)}
}
Reset-Fixture;Apply-Fixture -WhatIf
if($global:writes -ne 0){throw 'WhatIfWrote'}
Apply-Fixture
$statePath=Join-Path $env:ProgramData 'OnlyTweak\Remoting\state.json'
$first=$global:files[$statePath]|ConvertFrom-Json
if($first.Phase -ne 'Applied' -or $first.CreatedListener){throw 'CompatibleListenerNotReused'}
$count=$global:writes;$blocked=$false;try{Apply-Fixture}catch{$blocked=$true}
if(-not $blocked -or $count -ne $global:writes){throw 'ActiveCheckpointReused'}
& $apply -Mode Undo -Confirm:$false -WhatIf 6>$null | Out-Null
if($global:writes -ne $count){throw 'UndoWhatIfWrote'}
& $apply -Mode Undo -Confirm:$false | Out-Null
$restored=$global:files[$statePath];$count=$global:writes
& $apply -Mode Undo -Confirm:$false | Out-Null
if($global:writes -ne $count){throw 'RepeatedUndoWrote'}
Apply-Fixture -WhatIf
if($global:writes -ne $count){throw 'ReapplyWhatIfWrote'}
Apply-Fixture
$next=$global:files[$statePath]|ConvertFrom-Json
$archive=Join-Path (Split-Path $statePath) ('History\'+$first.Generation+'.json')
if($next.Generation -eq $first.Generation -or $global:files[$archive] -cne $restored){throw 'CheckpointHistoryLost'}
$validCheckpoint=$global:files[$statePath]
foreach($kind in @('tampered','foreign','partial','legacy')) {
    $value=$validCheckpoint|ConvertFrom-Json
    switch($kind){'tampered'{$value.StartMode='Disabled'} 'foreign'{$value.Computer='OTHER'} 'partial'{
        $value.Phase='Partial'
        $json=$value|Select-Object * -ExcludeProperty CheckpointHash|ConvertTo-Json -Depth 20 -Compress
        $alg=[Security.Cryptography.SHA256]::Create();try{$value.CheckpointHash=([BitConverter]::ToString($alg.ComputeHash([Text.Encoding]::UTF8.GetBytes($json)))).Replace('-','')}finally{$alg.Dispose()}
    } 'legacy'{$value.SchemaVersion=1}}
    $global:files[$statePath]=$value|ConvertTo-Json -Depth 12
    $count=$global:writes;$blocked=$false;try{& $apply -Mode Undo -Confirm:$false | Out-Null}catch{$blocked=$true}
    if(-not $blocked -or $count -ne $global:writes){throw ('UnsafeCheckpointAccepted:'+ $kind)}
}
'PROVISION_FIXTURE_PASS'
