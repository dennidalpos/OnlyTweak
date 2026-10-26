using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;

namespace OnlyTweakWindows
{
    internal static partial class Tests
    {
        private const string DiagnosticMocks = """
            $PSSenderInfo=[pscustomobject]@{ConnectedUser='fixture-user'}
            function New-PSSessionOption {}
            function Invoke-Command { param($ScriptBlock,$ArgumentList) if ($global:scenario -eq 'transport') { throw 'FixtureTransport' }; & $ScriptBlock @ArgumentList }
            function Get-ADRootDSE { [pscustomobject]@{defaultNamingContext='DC=corp,DC=example'} }
            function Get-Item { [CmdletBinding()]param($LiteralPath) if ($global:scenario -eq 'share' -and $LiteralPath.EndsWith('SYSVOL')) { throw [UnauthorizedAccessException]::new('SECRET-SENTINEL') }; [pscustomobject]@{PSIsContainer=$true} }
            function Get-CimInstance {
                [CmdletBinding()]param([Parameter(Position=0)]$ClassName,$Namespace)
                if ($global:scenario -eq 'cim' -and $ClassName -eq 'Win32_ComputerSystem') { throw [UnauthorizedAccessException]::new('SECRET-SENTINEL') }
                if ($ClassName -eq 'Win32_ComputerSystem') { [pscustomobject]@{Name='fixture';Domain='corp.example';DomainRole=$(if ($global:scenario -eq 'dc') {5} else {1});PartOfDomain=($global:scenario -ne 'standalone')} }
                elseif ($ClassName -eq 'Win32_OperatingSystem') { [pscustomobject]@{Caption='Fixture OS';Version='1';BuildNumber='1'} }
                elseif ($ClassName -eq 'Win32_Printer') { [pscustomobject]@{Name='Fixture printer'} }
                else { [pscustomobject]@{ReplicationGroupName='Fixture';ReplicatedFolderName='SYSVOL';State=4} }
            }
            function Get-Service { [CmdletBinding()]param($Name) [pscustomobject]@{Name='Spooler';Status='Running';StartType='Automatic'}; if ($global:scenario -eq 'services') { throw [UnauthorizedAccessException]::new('SECRET-SENTINEL') } }
            function Get-HotFix { [pscustomobject]@{HotFixID='Fixture';InstalledOn=[datetime]'2026-10-25'} }
            function Test-ComputerSecureChannel { [CmdletBinding()]param() if ($global:scenario -eq 'channel') { throw [UnauthorizedAccessException]::new('SECRET-SENTINEL') }; if ($global:scenario -eq 'issue') { $false } else { $true } }
            function Invoke-FixtureDiagnosticNative { $global:LASTEXITCODE=if (($global:scenario -eq 'time' -and $args.Count -gt 0) -or ($global:scenario -eq 'tickets' -and $args.Count -eq 0)) {5} else {0}; 'Fixture native evidence' }
            function Get-WinEvent {
                [CmdletBinding()]param($FilterHashtable,$MaxEvents)
                if ($global:scenario -eq 'empty-return') { return }
                if ($global:scenario -eq 'empty') { $PSCmdlet.ThrowTerminatingError([Management.Automation.ErrorRecord]::new([Exception]::new('SECRET-SENTINEL'),'NoMatchingEventsFound',[Management.Automation.ErrorCategory]::ObjectNotFound,$null)) }
                if ($global:scenario -in @('denied','issue')) { throw [UnauthorizedAccessException]::new('SECRET-SENTINEL') }
                if ($global:scenario -eq 'timeout') { throw [TimeoutException]::new('SECRET-SENTINEL') }
                if ($global:scenario -in @('log','provider')) {
                    $id=if ($global:scenario -eq 'log') {'NoMatchingLogsFound'} else {'NoMatchingProvidersFound'}
                    $PSCmdlet.ThrowTerminatingError([Management.Automation.ErrorRecord]::new([Exception]::new('SECRET-SENTINEL'),$id,[Management.Automation.ErrorCategory]::ObjectNotFound,$null))
                }
                if ($global:scenario -eq 'failed') { throw 'SECRET-SENTINEL' }
                [pscustomobject]@{TimeCreated=[datetime]'2026-10-25';Id=1;ProviderName='NETLOGON';LevelDisplayName='Fixture'}
            }
            """;
        private static string MockDiagnosticNatives(string script) => script
            .Replace("& \"$env:windir\\System32\\w32tm.exe\"","Invoke-FixtureDiagnosticNative")
            .Replace("& \"$env:windir\\System32\\klist.exe\"","Invoke-FixtureDiagnosticNative");
        private static string WithoutModuleImport(string script)
        {
            int start=script.IndexOf("function Require-Module",StringComparison.Ordinal),end=script.IndexOf("function Native",start,StringComparison.Ordinal);
            return script.Substring(0,start)+"function Require-Module { param($name) }\n"+script.Substring(end);
        }
        private static StepResult DiagnosticFixture(string id,bool jea,string scenario)
        {
            string mocks="$global:scenario='"+scenario+"'\n"+DiagnosticMocks+"\n";
            string prefix=mocks;
            if (jea) {
                string module=mocks+MockDiagnosticNatives(EmbeddedScripts.Read("OnlyTweakEndpoint.psm1"));
                prefix+="$moduleSource=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"+
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(module))+"')); New-Module -ScriptBlock ([scriptblock]::Create($moduleSource)) | Import-Module -Force\n";
            }
            OperationRequest request=Request(id); request.Profile.JeaEndpoint=jea ? "OnlyTweak" : "";
            string script=prefix+MockDiagnosticNatives(WithoutModuleImport(EmbeddedScripts.Read("Operations.ps1")));
            return new PowerShellBridge(script).ExecuteAsync(request,EnterpriseCatalog.Get(id),CancellationToken.None).GetAwaiter().GetResult();
        }
        private static void DiagnosticTests()
        {
            foreach (string id in new[]{"endpoint.health","domain.auth","domain.sysvol"}) foreach (bool jea in new[]{false,true})
                Test("diagnostiche parziali: "+id+" "+(jea ? "JEA" : "WinRM"),()=> {
                    foreach (string scenario in new[]{"present","empty","empty-return","denied","log","provider","timeout","failed","services","cim"}) {
                        StepResult result=DiagnosticFixture(id,jea,scenario);
                        Assert(result.Data is JsonElement,"Dati persi: "+scenario+" "+Wire.Write(result));
                        JsonElement root=result.Data.Value,data=id=="domain.sysvol" ? root.GetProperty("Remote") : root;
                        bool succeeds=scenario is "present" or "empty" or "empty-return" || (scenario=="cim" && id=="domain.sysvol") || (scenario=="services" && id=="domain.auth");
                        Assert(result.State==(succeeds ? StepState.Success : StepState.Inconclusive),"Esito errato: "+scenario+" "+Wire.Write(result));
                        string expected=scenario switch {"empty" or "empty-return"=>"Empty","denied"=>"Denied","log" or "provider"=>"Unavailable","timeout"=>"TimedOut","failed"=>"Failed",_=>"Completed"};
                        Assert(data.GetProperty("Checks").GetProperty("Events").GetProperty("Status").GetString()==expected,"Classificazione eventi persa: "+Wire.Write(result));
                        if (id=="endpoint.health") {
                            Assert(data.GetProperty("OS").GetProperty("Caption").GetString()=="Fixture OS" && data.GetProperty("Updates").GetArrayLength()==1,"Letture indipendenti perse.");
                            Assert(data.GetProperty("Services").GetArrayLength()==1,"Dati emessi prima dell'errore persi.");
                        } else if (id=="domain.auth") {
                            Assert(data.GetProperty("Time").GetString().Contains("Fixture"),"Orario perso.");
                            Assert(data.GetProperty("Checks").GetProperty("Tickets").GetProperty("Status").GetString()==(jea ? "NotApplicable" : "Completed"),"Esposizione JEA errata.");
                        } else Assert(root.GetProperty("Shares").GetArrayLength()==2 && data.GetProperty("Folders").GetArrayLength()==1,"Share/cartelle perse.");
                        Assert(!Wire.Write(result).Contains("SECRET-SENTINEL"),"Testo eccezione serializzato.");
                        Assert(result.AuthenticationVerified,"Autenticazione riuscita persa per guasto selettivo.");
                    }
                });
            Test("diagnostiche: problema osservato conserva letture parziali e SYSVOL isola share/trasporto",()=> {
                foreach(bool jea in new[]{false,true}) {
                    StepResult issue=DiagnosticFixture("domain.auth",jea,"issue");
                    Assert(issue.State==StepState.ObservedIssue && issue.ErrorCode=="PartialDiagnosticRead","Problema esplicito nascosto.");
                    foreach(string scenario in new[]{"time","tickets","channel","dc","standalone"}) {
                        StepResult auth=DiagnosticFixture("domain.auth",jea,scenario);
                        bool complete=scenario is "dc" or "standalone" || (scenario=="tickets" && jea);
                        Assert(auth.State==(complete ? StepState.Success : StepState.Inconclusive),"Sottocontrollo auth errato: "+scenario+" "+Wire.Write(auth));
                        Assert(auth.Data.Value.GetProperty("Events").GetArrayLength()==1,"Eventi persi dopo guasto auth.");
                        if(scenario is "dc" or "standalone") Assert(auth.Data.Value.GetProperty("Checks").GetProperty("SecureChannel").GetProperty("Status").GetString()=="NotApplicable","Canale applicato a DC/non membro.");
                    }
                    foreach(string scenario in new[]{"share","transport"}) {
                        StepResult result=DiagnosticFixture("domain.sysvol",jea,scenario);
                        Assert(result.State==StepState.Inconclusive && result.Data.Value.GetProperty("Shares").GetArrayLength()==2,"Dati locali persi: "+Wire.Write(result));
                        if(scenario=="share") Assert(result.Data.Value.GetProperty("Remote").GetProperty("Folders").GetArrayLength()==1 &&
                            result.Data.Value.GetProperty("Checks").GetProperty("SYSVOL").GetProperty("Status").GetString()=="Denied","Errore share non isolato.");
                    }
                }
            });
            Test("diagnostiche: contratto rifiuta raccolte ottimistiche e modulo precedente",()=> {
                StepResult valid=DiagnosticFixture("endpoint.health",false,"present");
                string payload=Wire.Write(valid);
                foreach(string invalid in new[]{payload.Replace("\"Checks\":","\"OldChecks\":"),payload.Replace("\"Completed\"","\"Denied\""),
                    payload.Replace("\"Completed\"","\"Unexpected\""),payload.Replace("\"Completed\"","\"Empty\""),payload.Replace("\"ErrorCode\":\"\"","\"ErrorCode\":\"Invalid\"")}) {
                    bool rejected=false;
                    try { OperationResultContract.Read(invalid,Request("endpoint.health")); } catch(FormatException) {rejected=true;}
                    Assert(rejected,"Metadati incompleti/discordanti accettati.");
                }
                string standard=EmbeddedScripts.Read("Operations.ps1").Replace("\r\n","\n").Split("$diagnosticEvidence = {\n",StringSplitOptions.None)[1].Split("\n}\n$gpoEvidence",StringSplitOptions.None)[0];
                string module=EmbeddedScripts.Read("OnlyTweakEndpoint.psm1").Replace("\r\n","\n").Split("function Get-OnlyTweakDiagnosticEvidence {\n",StringSplitOptions.None)[1].Split("\n}\nfunction Get-OnlyTweakGpoEvidence",StringSplitOptions.None)[0];
                Assert(standard==module,"Collector standard/JEA divergenti.");
            });
            DirectoryTargetTests();
        }
        private static void DirectoryTargetTests()
        {
            Test("directory: bind, query e report usano il DC selezionato senza fallback",()=> {
                foreach(string target in new[]{"dc1.corp.example","dc2.corp.example"}) foreach(string kind in new[]{"user","group","computer","authenticate","empty"}) {
                    string id=kind=="authenticate" ? "session.authenticate" : "ad.search";
                    OperationRequest request=Request(id); request.Target=target;
                    if(id=="ad.search") request.Parameters=new Dictionary<string,string>{{"query","Fixture"},{"kind",kind=="empty" ? "user" : kind}};
                    StepResult result=DirectoryFixture(request,"valid",kind=="empty");
                    Assert(result.State==StepState.Success && result.Target==target && result.AuthenticationVerified,"Target incoerente: "+Wire.Write(result));
                    Assert(result.Data.Value.GetProperty("Server").GetString()==target,"Server effettivo perso.");
                    Assert(result.Evidence.Contains("FixtureQueryCalls=1"),"Query effettiva non eseguita.");
                    Assert(result.Evidence.Any(e=>e.Contains(target)),"Bind non attribuito.");
                    string json=ReportExport.Json(request.Profile,new[]{result}),html=ReportExport.Html(request.Profile,new[]{result});
                    Assert(json.Contains("\"Target\":\""+target+"\"") && json.Contains("\"Server\":\""+target+"\"") && html.Contains(target),"Report discordante.");
                    if(kind=="empty") Assert(result.Data.Value.GetProperty("Rows").GetArrayLength()==0,"Ricerca vuota non valida.");
                    string payload=Wire.Write(result).Replace("\"Server\":\""+target+"\"","\"Server\":\"other.corp.example\"");
                    bool rejected=false; try {OperationResultContract.Read(payload,request);} catch(FormatException) {rejected=true;}
                    Assert(rejected,"Server diverso dal target accettato.");
                }
                foreach(string id in new[]{"ad.search","session.authenticate"}) foreach(string failure in new[]{"member","domain","identity","bind","dc-domain","dc-identity"}) {
                    OperationRequest request=Request(id); request.Target="dc2.corp.example";
                    StepResult result=DirectoryFixture(request,failure,false);
                    Assert(result.State==StepState.Inconclusive && !result.AuthenticationVerified && result.Data.Value.ValueKind==JsonValueKind.Array,
                        "Target non valido interrogato: "+failure+" "+Wire.Write(result));
                    Assert(result.Evidence.Contains("FixtureQueryCalls=0"),"Query avviata dopo bind/identità falliti.");
                }
                OperationRequest change=Request("ad.unlock"); change.Target="dc2.corp.example";
                change.Parameters=new Dictionary<string,string>{{"objectGuid",Guid.NewGuid().ToString()},{"expectedDn","CN=Person,OU=People,DC=corp,DC=example"}};
                Throws(()=>EnterpriseCatalog.Get(change.ActionId).Validate(change),"DC esplicito");
            });
        }
        private static void DirectoryUiTests()
        {
            Test("directory: cambiare target invalida righe, selezione e pagina",()=> {
                using EnterpriseDialog form=new EnterpriseDialog(false,false);
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                ComboBox targets=(ComboBox)typeof(EnterpriseDialog).GetField("targets",flags).GetValue(form);
                DataGridView grid=(DataGridView)typeof(EnterpriseDialog).GetField("directory",flags).GetValue(form);
                targets.Items.AddRange(new[]{"dc1.corp.example","dc2.corp.example"}); targets.SelectedIndex=0;
                grid.Rows.Add("Fixture","guid","dn"); grid.Rows[0].Selected=true;
                typeof(EnterpriseDialog).GetField("page",flags).SetValue(form,3);
                targets.SelectedIndex=1;
                Assert(grid.Rows.Count==0 && grid.SelectedRows.Count==0 && (int)typeof(EnterpriseDialog).GetField("page",flags).GetValue(form)==0,"Ricerca precedente ancora selezionabile.");
            });
        }
        private static StepResult DirectoryFixture(OperationRequest request,string failure,bool empty)
        {
            string mocks="""
                function Check-Server($server) { if ($server -cne $global:selected) { throw 'FixtureWrongServer' } }
                function Get-ADRootDSE { [CmdletBinding()]param($Server,$AuthType)
                    Check-Server $Server; if ($global:failure -eq 'bind') { throw [UnauthorizedAccessException]::new('Fixture') }
                    [pscustomobject]@{defaultNamingContext=$(if ($global:failure -eq 'domain') {'DC=other,DC=example'} else {'DC=corp,DC=example'});dnsHostName=$(if ($global:failure -eq 'identity') {'other.corp.example'} else {$Server})}
                }
                function Get-ADDomainController { [CmdletBinding()]param($Identity,$Server,$AuthType)
                    Check-Server $Server; Check-Server $Identity
                    if ($global:failure -eq 'member') { throw 'FixtureNotDc' }
                    [pscustomobject]@{HostName=$(if ($global:failure -eq 'dc-identity') {'other.corp.example'} else {$Server});Domain=$(if ($global:failure -eq 'dc-domain') {'other.example'} else {'corp.example'})}
                }
                function Query-Fixture($Server) { $global:queryCalls++; Check-Server $Server; if ($global:failure -ne 'valid') { throw 'FixtureQueryAfterFailedBind' }
                    if (-not $global:empty) { [pscustomobject]@{ObjectGUID='256da506-400f-4edf-b145-b7c353084231';Name='Fixture';DistinguishedName='CN=Person,OU=People,DC=corp,DC=example'} }
                }
                function Get-ADUser { [CmdletBinding()]param($Server,$LDAPFilter,$ResultPageSize,$ResultSetSize,$Properties) Query-Fixture $Server }
                function Get-ADGroup { [CmdletBinding()]param($Server,$LDAPFilter,$ResultPageSize,$ResultSetSize) Query-Fixture $Server }
                function Get-ADComputer { [CmdletBinding()]param($Server,$LDAPFilter,$ResultPageSize,$ResultSetSize,$Properties) Query-Fixture $Server }
                function Get-ADDomain { [CmdletBinding()]param($Server) $global:queryCalls++; Check-Server $Server; if ($global:failure -ne 'valid') {throw 'FixtureQueryAfterFailedBind'}; [pscustomobject]@{DNSRoot='corp.example'} }
                """;
            mocks="$global:queryCalls=0; $global:selected='"+request.Target+"'; $global:failure='"+failure+"'; $global:empty=$"+(empty ? "true" : "false")+"\n"+mocks+"\n";
            string script=WithoutModuleImport(EmbeddedScripts.Read("Operations.ps1")).Replace("[pscustomobject]@{ State=$state;","Note ('FixtureQueryCalls='+$global:queryCalls); [pscustomobject]@{ State=$state;");
            return new PowerShellBridge(mocks+script)
                .ExecuteAsync(request,EnterpriseCatalog.Get(request.ActionId),CancellationToken.None).GetAwaiter().GetResult();
        }
    }
}
