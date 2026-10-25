using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OnlyTweakWindows
{
    internal static partial class Tests
    {
        private static EnvironmentProfile EnterpriseProfile() => new EnvironmentProfile {
            Customer="Cliente campione",Domain="corp.example",DomainController="dc1.corp.example",
            Targets=new[] {"dc1.corp.example","dc2.corp.example","pc1.corp.example"},
            AllowedOus=new[] {"OU=People,DC=corp,DC=example"},DelegatedUserOusReviewed=true
        };
        private static OperationRequest Request(string id) => new OperationRequest {
            Profile=EnterpriseProfile(),ActionId=id,Target="dc1.corp.example",Confirmed=true
        };
        private sealed class FakeAudit : IOperationAudit {
            public bool Fail; public readonly List<string> Entries=new List<string>();
            public void Record(OperationRequest request,string identity,string phase,StepResult result=null) {
                if (Fail) throw new IOException("Audit simulato indisponibile.");
                Entries.Add(phase);
            }
        }
        private static StepResult Simulated(string script,string id="technician.inspect") =>
            new PowerShellBridge(script).ExecuteAsync(Request(id),EnterpriseCatalog.Get(id),CancellationToken.None).GetAwaiter().GetResult();
        private static void EnterpriseTests()
        {
            ResultContractTests();
            EndpointReadTests();
            Test("enterprise: input, wildcard, destinazioni e parametri estranei rifiutati",delegate {
                EnterpriseProfile().Validate();
                foreach (string host in new[] {"dc1 & whoami","*.corp.example","-server","dc1.corp.example:5985","dc1..example"})
                    Throws(()=>InputRules.Host(host),"DNS");
                OperationRequest request=Request("ad.replication"); request.Target="outside.example";
                Throws(()=>EnterpriseCatalog.Get(request.ActionId).Validate(request),"estranea");
                request.Target="dc1.corp.example"; request.Parameters["script"]="Get-Process";
                Throws(()=>EnterpriseCatalog.Get(request.ActionId).Validate(request),"Parametro");
            });
            Test("enterprise: modifiche AD richiedono conferma, GUID, OU revisionate e DN",delegate {
                OperationRequest request=Request("ad.unlock");
                request.Confirmed=false; Throws(()=>EnterpriseCatalog.Get(request.ActionId).Validate(request),"Conferma");
                request.Confirmed=true; request.Profile.DelegatedUserOusReviewed=false;
                Throws(()=>EnterpriseCatalog.Get(request.ActionId).Validate(request),"revisiona");
                request.Profile.DelegatedUserOusReviewed=true;
                request.Parameters["objectGuid"]=Guid.NewGuid().ToString();
                request.Parameters["expectedDn"]="CN=Person,OU=People,DC=corp,DC=example";
                EnterpriseCatalog.Get(request.ActionId).Validate(request);
                request.Target="dc2.corp.example"; Throws(()=>EnterpriseCatalog.Get(request.ActionId).Validate(request),"DC esplicito");
            });
            Test("enterprise: budget singola riga, batch e riepilogo finale",delegate {
                BoundedText output=new BoundedText(); output.Append(new string('x',300000));
                Assert(output.Truncated && output.Read().Length<=200000,"Singola riga senza limite.");
                for (int i=0;i<5000;i++) output.Append(new string('y',100)+"\r\n");
                output.Append("ESITO FINALE");
                Assert(output.Read().Length<=200000 && output.Read().EndsWith("ESITO FINALE"),"Batch o riepilogo persi.");
                Assert(output.Drain().Length<=200000 && output.Read()=="","Drain non azzera.");
            });
            Test("enterprise: audit indisponibile blocca prima di qualsiasi modifica",delegate {
                bool invoked=false; FakeAudit audit=new FakeAudit {Fail=true};
                EnterpriseExecutor executor=new EnterpriseExecutor(audit,(r,a,t)=> {invoked=true; return Task.FromResult(new StepResult());});
                OperationRequest request=Request("ad.unlock");
                request.Parameters["objectGuid"]=Guid.NewGuid().ToString(); request.Parameters["expectedDn"]="CN=Person,OU=People,DC=corp,DC=example";
                Throws(()=>executor.ExecuteAsync(request,"identity",CancellationToken.None).GetAwaiter().GetResult(),"Audit");
                Assert(!invoked,"Modifica avviata senza audit.");
            });
            Test("enterprise: errore trasporto modificante resta non verificato e tracciato",delegate {
                FakeAudit audit=new FakeAudit();
                EnterpriseExecutor executor=new EnterpriseExecutor(audit,(r,a,t)=>throw new IOException("rete assente"));
                OperationRequest request=Request("endpoint.flushdns");
                StepResult result=executor.ExecuteAsync(request,"identity",CancellationToken.None).GetAwaiter().GetResult();
                Assert(result.State==StepState.Unverified && audit.Entries.SequenceEqual(new[] {"requested","finished"}),"Esito ottimistico o audit perso.");
                SessionSafety safety=new SessionSafety(); safety.Observe(request,result);
                Throws(()=>safety.Authorize(request),"rileggi");
                OperationRequest verify=Request("endpoint.verify"); verify.Parameters["operation"]=request.ActionId;
                safety.Observe(verify,VerifiedRead(request.ActionId));
                safety.Authorize(request);
            });
            Test("enterprise: esito positivo senza autenticazione non certifica target",delegate {
                EnterpriseExecutor executor=new EnterpriseExecutor(new FakeAudit(),(r,a,t)=>Task.FromResult(new StepResult {State=StepState.Success}));
                Assert(executor.ExecuteAsync(Request("session.authenticate"),"identity",CancellationToken.None).Result.State==StepState.Inconclusive,"Autenticazione inventata.");
            });
            Test("enterprise: report HTML codificato e profili senza segreti",delegate {
                EnvironmentProfile profile=EnterpriseProfile(); profile.Customer="<script>alert(1)</script>";
                string html=ReportExport.Html(profile,new[] {new StepResult {Summary="<img src=x onerror=alert(1)>"}});
                Assert(!html.Contains("<script>") && !html.Contains("<img ") && html.Contains("Content-Security-Policy"),"HTML attivo.");
                Assert(!Wire.Write(profile).Contains("password",StringComparison.OrdinalIgnoreCase),"Segreto in profilo.");
            });
            Test("enterprise: PowerShell strutturato, errore nativo e output enorme",delegate {
                StepResult valid=Simulated("[Console]::OutputEncoding=New-Object Text.UTF8Encoding; '{\"State\":\"Success\",\"Summary\":\"simulato\",\"Evidence\":[],\"AuthenticationVerified\":false,\"ErrorCode\":\"\",\"Data\":{\"Count\":3,\"Version\":\"5.1\",\"Modules\":[],\"OS\":{}}}'");
                Assert(valid.State==StepState.Success && valid.Data.Value.GetProperty("Count").GetInt32()==3,"Risposta JSON persa.");
                StepResult invalid=Simulated("'non-json'");
                Assert(invalid.State==StepState.Unavailable,"Output malformato accettato.");
                Assert(Simulated("'null'").State==StepState.Unavailable,"JSON nullo non gestito.");
                StepResult enormous=Simulated("[Console]::Write(('x' * 300000))");
                Assert(enormous.Truncated && enormous.State==StepState.Inconclusive,"Budget processo non applicato.");
            });
            Test("enterprise: annullamento diagnostico termina solamente la fixture",delegate {
                using (CancellationTokenSource cancellation=new CancellationTokenSource(300)) {
                    StepResult result=new PowerShellBridge("Start-Sleep -Seconds 20").ExecuteAsync(Request("technician.inspect"),
                        EnterpriseCatalog.Get("technician.inspect"),cancellation.Token).GetAwaiter().GetResult();
                    Assert(result.State==StepState.Cancelled,"Fixture diagnostica non annullata.");
                }
            });
            Test("enterprise: annullamento prima dell'avvio restituisce esito senza processi pendenti",delegate {
                using (CancellationTokenSource cancellation=new CancellationTokenSource()) {
                    cancellation.Cancel();
                    EnterpriseExecutor executor=new EnterpriseExecutor(new FakeAudit(),new PowerShellBridge("Start-Sleep -Seconds 20").ExecuteAsync);
                    StepResult result=executor.ExecuteAsync(Request("technician.inspect"),"identity",cancellation.Token).GetAwaiter().GetResult();
                    Assert(result.State==StepState.Cancelled,"Annullamento prima del processo propagato senza risultato.");
                }
            });
            Test("enterprise: audit persistente non registra password o ciphertext",delegate {
                string root=Path.Combine(Path.GetTempPath(),"OnlyTweak-audit-test-"+Guid.NewGuid().ToString("N"));
                try {
                    OperationRequest request=Request("ad.password"); request.ProtectedSecret="CIPHERTEXT-SENTINEL";
                    request.Parameters["objectGuid"]=Guid.NewGuid().ToString();
                    new FileOperationAudit(root).Record(request,"identity","requested");
                    string path=Directory.GetFiles(Path.Combine(root,request.Profile.Id.ToString("N"))).Single();
                    string body=File.ReadAllText(path);
                    Assert(!body.Contains(request.ProtectedSecret) && body.Contains(request.OperationId.ToString()),"Segreto nell'audit.");
                    File.Delete(path); Directory.Delete(Path.GetDirectoryName(path)); Directory.Delete(root);
                } finally {
                    // The fixture creates only one bounded audit record; no production directories are touched.
                    if (Directory.Exists(root)) {
                        foreach (string folder in Directory.GetDirectories(root)) {
                            foreach (string file in Directory.GetFiles(folder)) File.Delete(file);
                            Directory.Delete(folder,false);
                        }
                        Directory.Delete(root,false);
                    }
                }
            });
            Test("enterprise: DPAPI password vuotata e non serializzata in chiaro",delegate {
                using (SecureString value=new SecureString()) {
                    foreach (char ch in "fixture-only-secret") value.AppendChar(ch);
                    string protectedValue=WindowsCredentials.Protect(value);
                    Assert(!protectedValue.Contains("fixture-only-secret"),"Password in chiaro.");
                }
            });
            Test("enterprise: procedure incorporate e catalogo chiuso",delegate {
                foreach (string file in new[] {"Operations.ps1","Provision-Remoting.ps1","OnlyTweakEndpoint.psm1"})
                    Assert(EmbeddedScripts.Read(file).Length>100,"Risorsa mancante: "+file);
                Assert(EnterpriseCatalog.Actions.Select(a=>a.Id).Distinct().Count()==EnterpriseCatalog.Actions.Length,"ID duplicati.");
                foreach (RunbookDefinition book in EnterpriseCatalog.Runbooks) foreach (string step in book.Steps)
                    Assert(EnterpriseCatalog.Get(step).Risk==ActionRisk.ReadOnly,"Modifica implicita in runbook.");
                Throws(()=>EnterpriseCatalog.Get("ad.syncall"),"catalogo");
            });
            Test("enterprise: estrazione conserva i byte delle procedure firmabili",delegate {
                string path=Path.Combine(Path.GetTempPath(),"OnlyTweak-bytes-"+Guid.NewGuid().ToString("N")+".ps1");
                try {
                    EmbeddedScripts.WriteTo("Operations.ps1",path);
                    using (Stream resource=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("OnlyTweak.Operations.ps1"))
                    using (MemoryStream expected=new MemoryStream()) {
                        resource.CopyTo(expected);
                        Assert(expected.ToArray().SequenceEqual(File.ReadAllBytes(path)),"Firma alterata dall'estrazione.");
                    }
                } finally { if (File.Exists(path)) File.Delete(path); }
            });
            Test("enterprise: sessioni processo isolate e profilo immutabile",delegate {
                EnvironmentProfile first=EnterpriseProfile(),second=EnterpriseProfile();
                second.Customer="Altro cliente";
                using (CustomerSession one=CustomerSession.Start(first,false).GetAwaiter().GetResult())
                using (CustomerSession two=CustomerSession.Start(second,false).GetAwaiter().GetResult()) {
                    OperationRequest wrong=Request("technician.inspect"); wrong.Profile=second;
                    Throws(()=>one.Execute(wrong).GetAwaiter().GetResult(),"profilo");
                    Assert(one.Profile.Id!=two.Profile.Id,"Profili confusi.");
                    for (int step=0;step<2;step++) {
                        OperationRequest local=Request("technician.inspect"); local.Profile=first.Copy();
                        StepResult evidence=one.Execute(local).GetAwaiter().GetResult();
                        Assert(evidence.ProfileId==first.Id && evidence.OperationId==local.OperationId &&
                            (evidence.State==StepState.Success || evidence.State==StepState.Inconclusive),"Risposta associata al cliente errato.");
                    }
                }
            });
            Test("sicurezza: SMode fallito non interrompe CIM e Defender",delegate {
                WindowsSecurityBackend backend=new WindowsSecurityBackend(new DeniedRegistry());
                SecurityEnvironment result=backend.Probe();
                Assert(!result.SModeKnown && !result.SMode,"SMode non letto convertito in certezza.");
            });
            Test("sicurezza: comando reale Defender distingue null, vuoto e modi documentati",delegate {
                foreach (string mode in new[] {"$null","''","'   '","'UnexpectedMode'","'Normal'","'Passive'","'EDR Block Mode'","'SxS Passive Mode'"}) {
                    string fixture="function Get-CimInstance { [pscustomobject]@{SecurityServicesRunning=@()} }; "+
                        "function Get-MpPreference { [pscustomobject]@{PUAProtection=1} }; "+
                        "function Get-MpComputerStatus { [pscustomobject]@{AMRunningMode="+mode+";AMServiceEnabled=$true;AntivirusEnabled=$true} }; ";
                    SecurityEnvironment result=WindowsSecurityBackend.ParseProbe(WindowsSecurityBackend.PowerShell(fixture+WindowsSecurityBackend.ProbeCommand),new SecurityEnvironment());
                    bool expectedKnown=mode=="$null" || mode=="'Normal'" || mode=="'Passive'" || mode=="'EDR Block Mode'" || mode=="'SxS Passive Mode'";
                    Assert(result.DefenderKnown==expectedKnown,"Modo mal interpretato: "+mode);
                    Assert(!result.DefenderKnown || result.DefenderActive==(mode=="$null" || mode=="'Normal'"),"Stato attivo errato: "+mode);
                }
            });
            Test("enterprise: script reale AD rifiuta privilegi e OU estranee senza scritture",delegate {
                foreach (string scenario in new[] {"privileged","outside","moved","service","valid"}) {
                    OperationRequest request=Request("ad.unlock");
                    request.Parameters["objectGuid"]="256da506-400f-4edf-b145-b7c353084231";
                    request.Parameters["expectedDn"]="CN=Person,OU=People,DC=corp,DC=example";
                    string dn=scenario=="outside" ? "CN=Person,OU=Admins,DC=corp,DC=example" : request.Parameters["expectedDn"];
                    if (scenario=="outside") request.Parameters["expectedDn"]=dn;
                    string prefix="function Get-ADRootDSE { [pscustomobject]@{defaultNamingContext='DC=corp,DC=example'} }\n"+
                        "function Get-ADUser { [pscustomobject]@{ObjectClass='user';ObjectGUID=[guid]'256da506-400f-4edf-b145-b7c353084231';"+
                        "DistinguishedName='"+(scenario=="moved" ? "CN=Moved,OU=People,DC=corp,DC=example" : dn)+"';"+
                        "SID=[pscustomobject]@{Value='S-1-5-21-100-200-300-1234'};adminCount="+(scenario=="privileged" ? "1" : "0")+
                        ";primaryGroupID=513;servicePrincipalName=@("+(scenario=="service" ? "'HTTP/service'" : "")+");LockedOut=$false;Enabled=$true;pwdLastSet=123} }\n"+
                        "function Get-ADAccountAuthorizationGroup { [pscustomobject]@{SID=[pscustomobject]@{Value='S-1-5-21-100-200-300-513'}} }\n"+
                        "function Unlock-ADAccount { $script:writes++; }\n$script:writes=0\n";
                    string script=EmbeddedScripts.Read("Operations.ps1");
                    int start=script.IndexOf("function Require-Module",StringComparison.Ordinal),end=script.IndexOf("function Native",StringComparison.Ordinal);
                    script=prefix+script.Substring(0,start)+"function Require-Module { param($name) }\n"+script.Substring(end);
                    // Count is added only by this isolated fixture, never by production operations.
                    script=script.Replace("[pscustomobject]@{ State=$state;","Note ('FixtureWrites='+$script:writes); [pscustomobject]@{ State=$state;");
                    StepResult result=new PowerShellBridge(script).ExecuteAsync(request,EnterpriseCatalog.Get(request.ActionId),CancellationToken.None).GetAwaiter().GetResult();
                    int writes=Int32.Parse(result.Evidence.Single(e=>e.StartsWith("FixtureWrites=",StringComparison.Ordinal)).Substring(14));
                    Assert(writes==(scenario=="valid" ? 1 : 0),"Scrittura inattesa: "+scenario+" "+Wire.Write(result));
                    Assert(scenario!="valid" || result.State==StepState.Success,"Scenario valido non verificato.");
                }
            });
            Test("enterprise: script reale replica conserva errori e partizioni",delegate {
                string prefix="function Get-ADRootDSE { [pscustomobject]@{defaultNamingContext='DC=corp,DC=example'} }\n"+
                    "function Get-ADReplicationPartnerMetadata { param($Partition,$PartnerType) if ($Partition -ne '*' -or $PartnerType -ne 'Both') { throw 'WrongScope' }; "+
                    "[pscustomobject]@{Server='dc1.corp.example';Partition='DC=corp,DC=example';LastReplicationResult=1722;ConsecutiveReplicationFailures=2} }\n"+
                    "function Get-ADReplicationFailure { [pscustomobject]@{LastError=1722;FailureCount=2} }\n";
                string script=EmbeddedScripts.Read("Operations.ps1");
                int start=script.IndexOf("function Require-Module",StringComparison.Ordinal),end=script.IndexOf("function Verify-Directory",StringComparison.Ordinal);
                script=prefix+script.Substring(0,start)+"function Require-Module { param($name) }\nfunction Native { param($name,$arguments) [pscustomobject]@{ExitCode=0} }\n"+script.Substring(end);
                StepResult result=new PowerShellBridge(script).ExecuteAsync(Request("ad.replication"),EnterpriseCatalog.Get("ad.replication"),CancellationToken.None).GetAwaiter().GetResult();
                Assert(result.State==StepState.ObservedIssue && result.AuthenticationVerified,"Errore replica perso: "+Wire.Write(result));
            });
            Test("enterprise: script reale DFSR non confonde campione e guasto",delegate {
                string prefix="function Get-DfsrBacklog { [CmdletBinding()]param($GroupName,$FolderName,$SourceComputerName,$DestinationComputerName) "+
                    "Write-Verbose 'Backlog totale: 150'; [pscustomobject]@{FileName='fixture.txt'} }\n"+
                    "function Get-DfsrState { [pscustomobject]@{State='Idle'} }\nfunction Start-Sleep { }\n";
                string script=EmbeddedScripts.Read("Operations.ps1");
                int start=script.IndexOf("function Require-Module",StringComparison.Ordinal),end=script.IndexOf("function Native",StringComparison.Ordinal);
                script=prefix+script.Substring(0,start)+"function Require-Module { param($name) }\n"+script.Substring(end);
                OperationRequest request=Request("dfsr.backlog");
                request.Parameters=new Dictionary<string,string> {["peer"]="dc2.corp.example",["group"]="Data",["folder"]="Files"};
                StepResult result=new PowerShellBridge(script).ExecuteAsync(request,EnterpriseCatalog.Get(request.ActionId),CancellationToken.None).GetAwaiter().GetResult();
                Assert(result.State==StepState.Success,"Backlog normale dichiarato guasto: "+Wire.Write(result));
                JsonElement sample=result.Data.Value.GetProperty("Samples")[0];
                Assert(sample.GetProperty("SampleCount").GetInt32()==1 && sample.GetProperty("TotalCount").ValueKind==JsonValueKind.Null,"Campione scambiato per totale.");
            });
            Test("enterprise: script reale endpoint impedisce modifiche sui DC e ruolo ignoto",delegate {
                foreach (string role in new[] {"5","$null","1"}) {
                    string prefix="function Invoke-Command { param($ScriptBlock,$ArgumentList) & $ScriptBlock @ArgumentList }\n"+
                        "function New-PSSessionOption { }\nfunction Get-CimInstance { [pscustomobject]@{DomainRole="+role+";PartOfDomain=$true} }\n"+
                        "function Get-DnsClientCache { }\nfunction Clear-DnsClientCache { $script:writes++ }\n$script:writes=0\n";
                    string script=prefix+EmbeddedScripts.Read("Operations.ps1").Replace("[pscustomobject]@{ State=$state;","Note ('FixtureWrites='+$script:writes); [pscustomobject]@{ State=$state;");
                    StepResult result=new PowerShellBridge(script).ExecuteAsync(Request("endpoint.flushdns"),EnterpriseCatalog.Get("endpoint.flushdns"),CancellationToken.None).GetAwaiter().GetResult();
                    Assert(result.Evidence.Contains("FixtureWrites="+(role=="1" ? "1" : "0")),"Protezione DC/ruolo ignoto non valida.");
                }
            });
            Test("enterprise: parser Windows PowerShell 5.1 accetta tutte le procedure",delegate {
                foreach (string file in new[] {"Operations.ps1","Provision-Remoting.ps1","OnlyTweakEndpoint.psm1"}) {
                    string encoded=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(EmbeddedScripts.Read(file)));
                    string command="$source=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"+encoded+"'));"+
                        "$tokens=$null;$errors=$null;$null=[Management.Automation.Language.Parser]::ParseInput($source,[ref]$tokens,[ref]$errors);"+
                        "if ($errors.Count -gt 0) { throw 'ParseFailure' }; '{\"State\":\"Success\",\"Summary\":\"Parse OK\",\"Evidence\":[],\"AuthenticationVerified\":false,\"ErrorCode\":\"\",\"Data\":{\"Version\":\"5.1\",\"Modules\":[],\"OS\":{}}}'";
                    Assert(Simulated(command).State==StepState.Success,"Sintassi non supportata: "+file);
                }
            });
            Test("enterprise: predisposizione WhatIf non invoca API modificanti",delegate {
                string script=EmbeddedScripts.Read("Provision-Remoting.ps1");
                string mocks="\nfunction Get-CimInstance { [pscustomobject]@{DomainRole=1;PartOfDomain=$true;StartMode='Manual';State='Stopped'} }\n"+
                    "function Test-Path { param($LiteralPath) [bool]($LiteralPath -like '*OnlyTweakEndpoint.psm1') }\n"+
                    "function Get-NetFirewallRule { }\nfunction Get-PSSessionConfiguration { }\n"+
                    "function Save-State { $global:mutations++; throw 'UnexpectedWrite' }\n"+
                    "function Set-Service { $global:mutations++; throw 'UnexpectedWrite' }\n"+
                    "function Start-Service { $global:mutations++; throw 'UnexpectedWrite' }\n"+
                    "function Protect-Directory { $global:mutations++; throw 'UnexpectedWrite' }\n";
                script=script.Replace("$machine=Get-CimInstance",mocks+"\n$machine=Get-CimInstance")
                    .Replace("if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole('Administrator'))","if ($false)")
                    .Replace("$group=$sid.Translate([Security.Principal.NTAccount]).Value","$group='CORP\\Delegated'")
                    .Replace("Join-Path $PSScriptRoot 'OnlyTweakEndpoint.psm1'","Join-Path $env:TEMP 'OnlyTweakEndpoint.psm1'");
                string encoded=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(script));
                string command="$global:mutations=0;$source=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"+encoded+"'));"+
                    "& ([scriptblock]::Create($source)) -Mode Apply -ManagementAddresses '10.20.30.0/24' -AllowedGroupSid 'S-1-5-21-100-200-300-1234' -WhatIf;"+
                    "'WRITES='+$global:mutations";
                Assert(WindowsSecurityBackend.PowerShell(command).Contains("WRITES=0"),"WhatIf ha invocato scritture.");
            });
            Test("enterprise: avvio reale postazione tecnico usa procedura incorporata in lettura",delegate {
                StepResult result=new PowerShellBridge().ExecuteAsync(Request("technician.inspect"),EnterpriseCatalog.Get("technician.inspect"),
                    CancellationToken.None).GetAwaiter().GetResult();
                Assert(result.State==StepState.Success || result.State==StepState.Inconclusive,"Risultato strutturato non disponibile.");
                if (result.State==StepState.Success)
                    Assert(result.Data.Value.GetProperty("Version").GetString().StartsWith("5.1",StringComparison.Ordinal),"Host PowerShell errato.");
            });
        }
        private sealed class DeniedRegistry : IRegistryReader {
            public RegistryValueState Read(string path,string name) => throw new UnauthorizedAccessException("fixture");
        }
    }
}
