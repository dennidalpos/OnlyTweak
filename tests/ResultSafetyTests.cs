using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace OnlyTweakWindows
{
    internal static partial class Tests
    {
        private static string ResultPayload(object data,string state="Success") => Wire.Write(new {
            State=state,Summary="Fixture result",Evidence=Array.Empty<string>(),AuthenticationVerified=true,ErrorCode="",Data=data
        });
        private static StepResult VerifiedRead(string operation) => new StepResult {State=StepState.Success,AuthenticationVerified=true,
            Data=JsonSerializer.SerializeToElement(new {Operation=operation,Computer="pc1",ReadComplete=true,NativeExitCode=(int?)null,
                Output="",Data=Array.Empty<object>()})};
        private static void ResultContractTests()
        {
            Test("risultati: schema incompleto, enum e campi duplicati non diventano successo",()=> {
                string valid=ResultPayload(new {Version="5.1",Modules=Array.Empty<object>(),OS=new {}});
                foreach (string malformed in new[] {"{}","null","[]",valid.Replace("\"State\":\"Success\",",""),
                    valid.Replace("\"Success\"","null"),valid.Replace("\"Success\"","999"),valid.Replace("\"Success\"","\"999\""),
                    valid.Replace("\"Success\"","\"NotKnown\""),valid.Replace("\"Summary\":\"Fixture result\"","\"Summary\":\"\""),
                    valid.Replace("\"Evidence\":[]","\"Evidence\":null"),valid.Replace("\"Evidence\":[]","\"Evidence\":[42]"),
                    valid.Replace("\"AuthenticationVerified\":true,",""),valid.Replace("\"ErrorCode\":\"\",",""),
                    valid.Replace("\"ErrorCode\":\"\"","\"ErrorCode\":\"UnexpectedError\""),valid.Replace("\"State\":","\"Truncated\":true,\"State\":"),
                    valid.Replace("\"State\":","\"state\":\"Success\",\"State\":"),ResultPayload(null)}) {
                    bool rejected=false;
                    try { OperationResultContract.Read(malformed,Request("technician.inspect")); }
                    catch (Exception ex) when (ex is FormatException || ex is JsonException) { rejected=true; }
                    Assert(rejected,"Contratto incompleto accettato: "+malformed);
                }
                Assert(OperationResultContract.Read(valid,Request("technician.inspect")).State==StepState.Success,"Contratto valido rifiutato.");
                Assert(new StepResult().State!=StepState.Success,"Default ottimistico.");
            });
            Test("risultati: payload per azione e letture legittimamente vuote",()=> {
                OperationRequest search=Request("ad.search");
                Assert(OperationResultContract.Read(ResultPayload(new {Rows=Array.Empty<object>(),Page=0,HasMore=false,LimitReached=false}),search).State==StepState.Success,"Ricerca vuota rifiutata.");
                foreach (string id in EnterpriseCatalog.Actions.Select(a=>a.Id)) {
                    bool rejected=false;
                    try { OperationResultContract.Read(ResultPayload(new {}),Request(id)); } catch (FormatException) { rejected=true; }
                    Assert(rejected,"Payload vuoto accettato: "+id);
                }
            });
            Test("risultati: bridge e audit conservano incertezza su JSON incompleto e output troncato",()=> {
                foreach (string id in new[] {"technician.inspect","rsat.install"}) {
                    OperationRequest request=Request(id); request.Parameters=id=="rsat.install" ? new Dictionary<string,string>{{"capability","DNS"}} : new Dictionary<string,string>();
                    FakeAudit audit=new FakeAudit();
                    var executor=new EnterpriseExecutor(audit,new PowerShellBridge("'{}'").ExecuteAsync);
                    StepResult result=executor.ExecuteAsync(request,"fixture",CancellationToken.None).GetAwaiter().GetResult();
                    Assert(result.State==(id=="rsat.install" ? StepState.Unverified : StepState.Unavailable),"JSON incompleto positivo.");
                    if(id=="rsat.install") Assert(audit.Entries.SequenceEqual(new[]{"requested","finished"}),"Audit mancante.");
                }
                string valid=ResultPayload(new {Version="5.1",Modules=Array.Empty<object>(),OS=new {}});
                StepResult truncated=Simulated("[Console]::Error.Write(('x'*10000)); '"+valid+"'");
                Assert(truncated.Truncated && truncated.State==StepState.Inconclusive,"stderr troncato diventa successo.");
            });
        }
        private static void EndpointReadTests()
        {
            foreach(bool jea in new[] {false,true}) {
                Test("riletture: "+(jea ? "JEA" : "WinRM")+" preserva codice nativo, dati e blocco retry",()=> {
                    string module=ReplaceVerificationNatives(EmbeddedScripts.Read("OnlyTweakEndpoint.psm1"));
                    string prefix="function Invoke-Command { param($ScriptBlock,$ArgumentList) & $ScriptBlock @ArgumentList }\nfunction New-PSSessionOption {}\n"+
                        "function Get-CimInstance { [pscustomobject]@{DomainRole=1;PartOfDomain=$true} }\n";
                    if(jea) prefix+="$m=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"+Convert.ToBase64String(Encoding.UTF8.GetBytes(module))+"')); New-Module -ScriptBlock ([scriptblock]::Create($m)) | Import-Module -Force\n";
                    foreach (string operation in new[] {"gpupdate","winsock","sfc","dism"})
                        foreach(string scenario in new[]{"valid","failed","empty","unknown"}) {
                            string fake="function Invoke-FixtureNative { $global:LASTEXITCODE="+(scenario=="unknown" ? "$null" : scenario=="failed" ? "5" : "0")+"; "+(scenario=="empty" ? "" : "'Native fixture result'")+" }\n";
                            OperationRequest verify=Request("endpoint.verify"); verify.Target="pc1.corp.example";
                            verify.Profile.JeaEndpoint=jea ? "OnlyTweak" : ""; verify.Parameters["operation"]="endpoint."+operation;
                            StepResult result=new PowerShellBridge(prefix+fake+ReplaceVerificationNatives(EmbeddedScripts.Read("Operations.ps1")))
                                .ExecuteAsync(verify,EnterpriseCatalog.Get(verify.ActionId),CancellationToken.None).GetAwaiter().GetResult();
                            bool complete=scenario=="valid";
                            Assert((result.State==StepState.Success)==complete,"Stato errato: "+operation+"/"+scenario+" "+Wire.Write(result));
                            Assert(result.AuthenticationVerified,"Trasporto simulato non autenticato.");
                            Assert(result.Data.Value.GetProperty("ReadComplete").GetBoolean()==complete,"Completezza persa.");
                            JsonElement native=result.Data.Value.GetProperty("NativeExitCode");
                            Assert(scenario=="unknown" ? native.ValueKind==JsonValueKind.Null : native.GetInt32()==(scenario=="failed" ? 5 : 0),"Codice nativo perso.");
                            OperationRequest change=Request("endpoint."+operation); change.Target=verify.Target;
                            SessionSafety safety=new SessionSafety(); safety.Observe(change,new StepResult {State=StepState.Unverified});
                            safety.Observe(verify,result);
                            if(complete) safety.Authorize(change); else Throws(()=>safety.Authorize(change),"rileggi");
                        }
                    OperationRequest dns=Request("endpoint.verify"); dns.Profile.JeaEndpoint=jea ? "OnlyTweak" : ""; dns.Parameters["operation"]="endpoint.flushdns";
                    StepResult empty=new PowerShellBridge(prefix+"function Get-DnsClientCache {}\n"+EmbeddedScripts.Read("Operations.ps1"))
                        .ExecuteAsync(dns,EnterpriseCatalog.Get(dns.ActionId),CancellationToken.None).GetAwaiter().GetResult();
                    Assert(empty.State==StepState.Success && OperationResultContract.EndpointReadComplete(dns,empty),"Cache vuota non accettata: "+Wire.Write(empty));
                    foreach(string scenario in new[]{"spooler","spooler-missing","network","channel-false","channel-missing"}) {
                        OperationRequest read=Request("endpoint.verify"); read.Profile.JeaEndpoint=dns.Profile.JeaEndpoint;
                        read.Parameters["operation"]=scenario.StartsWith("spooler",StringComparison.Ordinal) ? "endpoint.spooler" : scenario=="network" ? "endpoint.tcpip" : "endpoint.securechannel";
                        string mocks=scenario=="spooler" ? "function Get-Service { [pscustomobject]@{Name='Spooler';Status='Running';StartType='Automatic'} }\n" :
                            scenario=="spooler-missing" ? "function Get-Service {}\n" : scenario=="network" ? "function Get-NetIPConfiguration {}\n" :
                            scenario=="channel-false" ? "function Test-ComputerSecureChannel { $false }\n" : "function Test-ComputerSecureChannel {}\n";
                        StepResult observed=new PowerShellBridge(prefix+mocks+EmbeddedScripts.Read("Operations.ps1"))
                            .ExecuteAsync(read,EnterpriseCatalog.Get(read.ActionId),CancellationToken.None).GetAwaiter().GetResult();
                        Assert((observed.State==StepState.Success)==!scenario.EndsWith("missing",StringComparison.Ordinal),"Lettura gestita errata: "+scenario+" "+Wire.Write(observed));
                    }
                });
            }
            Test("riletture: dati assenti, estranei, parziali o troncati non sbloccano retry",()=> {
                OperationRequest change=Request("endpoint.sfc"),verify=Request("endpoint.verify"); verify.Parameters["operation"]=change.ActionId;
                foreach(StepResult result in new[] {new StepResult{State=StepState.Success,AuthenticationVerified=true},VerifiedRead("endpoint.dism"),
                    new StepResult{State=StepState.Success,AuthenticationVerified=true,Truncated=true,Data=VerifiedRead(change.ActionId).Data}}) {
                    SessionSafety safety=new SessionSafety(); safety.Observe(change,new StepResult{State=StepState.Unverified}); safety.Observe(verify,result);
                    Throws(()=>safety.Authorize(change),"rileggi");
                }
            });
        }
        private static string ReplaceVerificationNatives(string source)
        {
            foreach(string name in new[]{"gpresult","netsh","sfc","dism"}) source=source.Replace("& \"$env:windir\\System32\\"+name+".exe\"","Invoke-FixtureNative");
            return source;
        }
    }
}
