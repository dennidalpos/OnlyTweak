# Synthetic host. Only the production function definitions are evaluated; no production entry point or OS mutations.
$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot '../../scripts/local/LocalRemoting.ps1'))
Invoke-Expression ($source.Split(@('# Entry point:'),[StringSplitOptions]::None)[0])
function Clone($value) {$value | ConvertTo-Json -Depth 28 -Compress | ConvertFrom-Json}
function Assert-Fixture($condition,[string]$message) {if (!$condition) {throw $message}}
function Reset-Fixture {
    $script:Config=[pscustomobject]@{Service=[pscustomobject]@{State='Stopped';StartMode='Manual'};Listeners=@();Endpoint=$null;Rule=$null}
    $script:Saved=$null;$script:Category='Private';$script:Multiple=$false;$script:FailListener=$false;$script:FailEndpoint=$false;$script:Ingress=$false;$script:Writes=0
}
function Assert-Administrator {}
function Read-LocalRemotingConfiguration {Clone $script:Config}
function Save-RemotingState($state) {$script:Saved=Clone $state}
function Load-RemotingState {if($script:Saved){Clone $script:Saved}}
function Get-NetConnectionProfile {param($InterfaceIndex,$ErrorAction)
    [pscustomobject]@{NetworkCategory=$script:Category;InterfaceAlias=('LAN-'+$InterfaceIndex)}
}
function Get-NetIPAddress {param($InterfaceIndex,$AddressFamily)
    [pscustomobject]@{AddressState='Preferred';IPAddress='192.0.2.10';PrefixLength=24}
    if ($script:Multiple) {[pscustomobject]@{AddressState='Preferred';IPAddress='192.0.2.11';PrefixLength=24}}
}
function Get-NetFirewallProfile {param($PolicyStore,$Name) [pscustomobject]@{Enabled='True';DefaultInboundAction='Block'}}
function Get-NetFirewallRule {param($PolicyStore,$Direction,$Enabled,$Action,$Name,$ErrorAction)
    if($Name -eq $script:RuleName){return $script:Config.Rule}
    if($script:Ingress){[pscustomobject]@{Name='foreign-ingress'}}
}
function Get-NetFirewallPortFilter {param([Parameter(ValueFromPipeline)]$InputObject) process {[pscustomobject]@{Protocol='TCP';LocalPort=@('5985')}}}
function Get-NetFirewallServiceFilter {param([Parameter(ValueFromPipeline)]$InputObject) process {[pscustomobject]@{Service='WinRM'}}}
function Get-NetFirewallApplicationFilter {param([Parameter(ValueFromPipeline)]$InputObject) process {[pscustomobject]@{Program='Any'}}}
function Set-Service {param($Name,$StartupType) $script:Writes++;$script:Config.Service.StartMode=switch($StartupType){'Automatic'{'Auto'} 'Manual'{'Manual'} 'Disabled'{'Disabled'}}}
function Start-Service {param($Name) $script:Writes++;$script:Config.Service.State='Running'}
function Stop-Service {param($Name,$ErrorAction) $script:Writes++;$script:Config.Service.State='Stopped'}
function Restart-Service {param($Name,$ErrorAction) $script:Writes++}
function New-Item {param($Path,$Address,$Transport,$Port,[switch]$Force)
    if($script:FailListener){throw 'FixtureListenerFailed'}
    Assert-Fixture ($Address -eq 'IP:192.0.2.10' -and $Transport -eq 'HTTP' -and $Port -eq 5985) 'BroadListener'
    $script:Writes++;$script:Config.Listeners=@([pscustomobject]@{Name='Listener_fixture';Keys=@(('Address='+$Address),'Transport=HTTP');Values=@();PSPath='WSMan:\localhost\Listener\Listener_fixture'})
}
function Register-PSSessionConfiguration {param($Name,$SecurityDescriptorSddl,$AccessMode,[switch]$NoServiceRestart,[switch]$Force)
    Assert-Fixture ($Name -eq 'OnlyTweak.Local' -and $SecurityDescriptorSddl -ceq $script:AdminSddl -and $AccessMode -eq 'Remote') 'EndpointBroadened'
    $script:Writes++;$script:Config.Endpoint=[pscustomobject]@{Name=$Name;Sddl=$SecurityDescriptorSddl}
    if($script:FailEndpoint){throw 'FixtureEndpointPartialFailure'}
}
function Unregister-PSSessionConfiguration {param($Name,[switch]$NoServiceRestart,[switch]$Force) $script:Writes++;$script:Config.Endpoint=$null}
function Get-ChildItem {param($Path) $script:Config.Listeners}
function Remove-Item {param($LiteralPath,[switch]$Recurse) Assert-Fixture ($LiteralPath -eq 'WSMan:\localhost\Listener\Listener_fixture') 'ForeignListenerRemoval';$script:Writes++;$script:Config.Listeners=@()}
function New-NetFirewallRule {
    param($PolicyStore,$Name,$DisplayName,$Direction,$Action,$Enabled,$Profile,$InterfaceAlias,$LocalAddress,$RemoteAddress,$Protocol,$LocalPort,$Service)
    Assert-Fixture ($script:Config.Endpoint -and $script:Config.Listeners.Count -eq 1 -and $Enabled -eq 'False' -and $Profile -eq 'Private' -and
        $InterfaceAlias -eq 'LAN-12' -and $LocalAddress -eq '192.0.2.10' -and $RemoteAddress -eq '192.0.2.0/24' -and $Service -eq 'WinRM') 'FirewallBroadenedOrOpenedEarly'
    $script:Writes++;$script:Config.Rule=[pscustomobject]@{Name=$Name;Enabled=$Enabled;Profile=$Profile;LocalAddress=@($LocalAddress);RemoteAddress=@($RemoteAddress)}
}
function Enable-NetFirewallRule {param($PolicyStore,$Name) $script:Writes++;$script:Config.Rule.Enabled='True'}
function Remove-NetFirewallRule {param($PolicyStore,$Name) Assert-Fixture ($Name -eq $script:RuleName) 'ForeignRuleRemoval';$script:Writes++;$script:Config.Rule=$null}
function Reject-Fixture([scriptblock]$body,[string]$message) {
    $rejected=$false
    try {& $body | Out-Null} catch {if($_.Exception.Message -notlike ('*'+$message+'*')){throw};$rejected=$true}
    Assert-Fixture $rejected ('Expected rejection: '+$message)
}
Reset-Fixture
Assert-Fixture ((Subnet-Canonical '192.0.2.0/255.255.255.0') -eq '192.0.2.0/24') 'SubnetMaskNormalizationFailed'
$before=Hash-Value $script:Config
$result=Invoke-LocalRemoting 'winrm.enable.12' 12
Assert-Fixture ($result.Configured -and $script:Saved.Phase -eq 'Applied' -and $script:Config.Rule.Enabled -eq 'True') 'ApplyNotVerified'
Assert-Fixture (Invoke-LocalRemoting 'winrm.inspect').Configured 'InspectNotConfigured'
$script:Category='Public'
Assert-Fixture (!(Invoke-LocalRemoting 'winrm.inspect').Configured) 'PublicNetworkCertifiedConfigured'
$script:Category='Private'
$result=Invoke-LocalRemoting 'winrm.undo'
Assert-Fixture ($result.Restored -and (Hash-Value $script:Config) -ceq $before) 'OriginalStateNotRestored'
Reset-Fixture;$script:Category='Public'
Reject-Fixture {Invoke-LocalRemoting 'winrm.enable.12' 12} 'PrivateNetworkRequired'
Assert-Fixture ($script:Writes -eq 0) 'PublicNetworkModified'
Reset-Fixture;$script:Multiple=$true
Reject-Fixture {Invoke-LocalRemoting 'winrm.enable.12' 12} 'SinglePrivateIPv4Required'
Assert-Fixture ($script:Writes -eq 0) 'AmbiguousAddressModified'
Reset-Fixture;$script:Config.Listeners=@([pscustomobject]@{Name='foreign'})
Reject-Fixture {Invoke-LocalRemoting 'winrm.enable.12' 12} 'ExistingConfigurationConflict'
Assert-Fixture ($script:Writes -eq 0) 'ExistingListenerModified'
Reset-Fixture;$script:Ingress=$true
Reject-Fixture {Invoke-LocalRemoting 'winrm.enable.12' 12} 'ExistingIngressConflict'
Assert-Fixture ($script:Writes -eq 0) 'ExistingIngressModified'
Reset-Fixture;$script:FailListener=$true;$before=Hash-Value $script:Config
Reject-Fixture {Invoke-LocalRemoting 'winrm.enable.12' 12} 'FixtureListenerFailed'
Assert-Fixture ($null -eq $script:Config.Rule -and $script:Saved.Phase -eq 'Applying') 'PartialFailureOpenedFirewall'
Invoke-LocalRemoting 'winrm.undo' | Out-Null
Assert-Fixture ((Hash-Value $script:Config) -ceq $before) 'PartialStateNotRestored'
Reset-Fixture;$script:FailEndpoint=$true
Reject-Fixture {Invoke-LocalRemoting 'winrm.enable.12' 12} 'FixtureEndpointPartialFailure'
$writes=$script:Writes
Reject-Fixture {Invoke-LocalRemoting 'winrm.undo'} 'ExternalConfigurationChange'
Assert-Fixture ($script:Writes -eq $writes -and !$script:Config.Rule) 'UnjournaledChangeOverwritten'
Reset-Fixture
Invoke-LocalRemoting 'winrm.enable.12' 12 | Out-Null
$script:Config.Rule.RemoteAddress=@('203.0.113.0/24');$writes=$script:Writes
Reject-Fixture {Invoke-LocalRemoting 'winrm.undo'} 'ExternalConfigurationChange'
Assert-Fixture ($script:Writes -eq $writes) 'ExternalFirewallOverwritten'
Reset-Fixture
Reject-Fixture {Invoke-LocalRemoting 'winrm.enable.13' 12} 'InvalidAction'
Assert-Fixture ($script:Writes -eq 0) 'InvalidActionModifiedHost'
'WINRM_FIXTURES_PASSED'
