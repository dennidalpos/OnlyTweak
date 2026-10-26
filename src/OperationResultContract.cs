using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace OnlyTweakWindows
{
    // Validate the external PowerShell protocol separately from internal IPC/result construction.
    internal static class OperationResultContract
    {
        private static bool Field(JsonElement value,string name,JsonValueKind kind) =>
            value.ValueKind==JsonValueKind.Object && value.TryGetProperty(name,out JsonElement field) && field.ValueKind==kind;
        private static bool Present(JsonElement value,string name) => value.ValueKind==JsonValueKind.Object &&
            value.TryGetProperty(name,out JsonElement field) && field.ValueKind!=JsonValueKind.Null && field.ValueKind!=JsonValueKind.Undefined;
        private static bool Text(JsonElement value,string name) => Field(value,name,JsonValueKind.String) &&
            !String.IsNullOrWhiteSpace(value.GetProperty(name).GetString());
        private static bool Boolean(JsonElement value,string name) => Field(value,name,JsonValueKind.True) || Field(value,name,JsonValueKind.False);
        public static StepResult Read(string json,OperationRequest request)
        {
            using JsonDocument document=JsonDocument.Parse(json,new JsonDocumentOptions {MaxDepth=32});
            JsonElement root=document.RootElement;
            if (root.ValueKind!=JsonValueKind.Object) throw new FormatException("Risultato non oggetto.");
            var properties=new Dictionary<string,JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in root.EnumerateObject())
                if (!properties.TryAdd(property.Name,property.Value)) throw new FormatException("Campo risultato duplicato.");
            if (!properties.TryGetValue("State",out JsonElement state) || state.ValueKind!=JsonValueKind.String ||
                !Enum.GetNames<StepState>().Contains(state.GetString(),StringComparer.OrdinalIgnoreCase) ||
                !properties.TryGetValue("Summary",out JsonElement summary) || summary.ValueKind!=JsonValueKind.String || String.IsNullOrWhiteSpace(summary.GetString()) ||
                !properties.TryGetValue("Evidence",out JsonElement evidence) || evidence.ValueKind!=JsonValueKind.Array ||
                evidence.EnumerateArray().Any(e=>e.ValueKind!=JsonValueKind.String) ||
                !properties.TryGetValue("AuthenticationVerified",out JsonElement auth) || (auth.ValueKind!=JsonValueKind.True && auth.ValueKind!=JsonValueKind.False) ||
                !properties.TryGetValue("ErrorCode",out JsonElement error) || error.ValueKind!=JsonValueKind.String ||
                !properties.ContainsKey("Data")) throw new FormatException("Contratto risultato incompleto.");
            StepResult parsed=Wire.Read<StepResult>(json);
            if (parsed.Truncated || (parsed.State==StepState.Success &&
                (!String.IsNullOrEmpty(parsed.ErrorCode) || !HasActionData(request,parsed))))
                throw new FormatException("Evidenze insufficienti per un risultato positivo.");
            return parsed;
        }
        internal static bool EndpointReadComplete(OperationRequest request,StepResult result)
        {
            if (result.Truncated || result.Data is not JsonElement data || !request.Parameters.TryGetValue("operation",out string operation) ||
                !Text(data,"Operation") || data.GetProperty("Operation").GetString()!=operation || !Text(data,"Computer") ||
                !Field(data,"ReadComplete",JsonValueKind.True) || !Present(data,"Data") || !Field(data,"Output",JsonValueKind.String) ||
                !data.TryGetProperty("NativeExitCode",out JsonElement code)) return false;
            bool native=operation is "endpoint.gpupdate" or "endpoint.winsock" or "endpoint.sfc" or "endpoint.dism";
            if (native) return code.ValueKind==JsonValueKind.Number && code.TryGetInt32(out int exit) && exit==0 && Text(data,"Output");
            if (code.ValueKind!=JsonValueKind.Null) return false;
            JsonElement observed=data.GetProperty("Data");
            return operation switch {
                "endpoint.flushdns" or "endpoint.tcpip" => observed.ValueKind==JsonValueKind.Array,
                "endpoint.spooler" => Text(observed,"Name") && Text(observed,"Status"),
                "endpoint.securechannel" => observed.ValueKind is JsonValueKind.True or JsonValueKind.False,
                _ => false
            };
        }
        private static bool SelectedServer(JsonElement data,OperationRequest request) => Text(data,"Server") &&
            String.Equals(data.GetProperty("Server").GetString(),request.Target,StringComparison.OrdinalIgnoreCase);
        private static bool CheckSet(JsonElement data,string[] names)
        {
            if (!Field(data,"Checks",JsonValueKind.Object)) return false;
            JsonElement checks=data.GetProperty("Checks");
            JsonProperty[] entries=checks.EnumerateObject().ToArray();
            if (entries.Length!=names.Length || entries.Select(p=>p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=names.Length) return false;
            foreach (string name in names) {
                if (!checks.TryGetProperty(name,out JsonElement check) || !Text(check,"Status") || !Field(check,"ErrorCode",JsonValueKind.String)) return false;
                if (check.EnumerateObject().Count()!=2) return false;
                string status=check.GetProperty("Status").GetString(),code=check.GetProperty("ErrorCode").GetString();
                if (status=="NotApplicable") {
                    if (name=="Tickets" && code=="JeaNotExposed" && Text(data,name)) continue;
                    if (name=="SecureChannel" && code=="NotDomainMember" && Field(data,"Machine",JsonValueKind.Object)) {
                        JsonElement machine=data.GetProperty("Machine");
                        if (Field(data,name,JsonValueKind.Null) && ((Field(machine,"DomainRole",JsonValueKind.Number) && machine.GetProperty("DomainRole").TryGetInt32(out int role) && role>=4) ||
                            Field(machine,"PartOfDomain",JsonValueKind.False))) continue;
                    }
                    return false;
                }
                string field=name switch {"RSOP"=>"Applied","Gpresult"=>"Result",_=>name};
                if (!Present(data,field)) return false;
                JsonElement value=data.GetProperty(field);
                if (status=="Completed") {
                    if (code!="" || (value.ValueKind==JsonValueKind.Array && value.GetArrayLength()==0)) return false;
                } else if (status=="Empty") {
                    if (value.ValueKind!=JsonValueKind.Array || value.GetArrayLength()!=0 ||
                        (code!="" && (name!="Events" || code!="NoMatchingEventsFound"))) return false;
                } else return false; // A partial read can never be advertised as Success.
            }
            return true;
        }
        private static bool DiagnosticData(JsonElement data,string kind,OperationRequest request)
        {
            if (kind=="health") return CheckSet(data,new[]{"OS","Computer","Services","Printers","Updates","Events"}) &&
                Field(data,"OS",JsonValueKind.Object) && Field(data,"Computer",JsonValueKind.Object) &&
                new[]{"Services","Printers","Updates","Events"}.All(n=>Field(data,n,JsonValueKind.Array));
            if (kind=="auth") {
                if (!CheckSet(data,new[]{"Machine","SecureChannel","Time","Tickets","Identity","Events"}) ||
                    !Field(data,"Role",JsonValueKind.Number) || !Text(data,"Computer") || !Text(data,"Time") ||
                    !Text(data,"Tickets") || !Field(data,"Events",JsonValueKind.Array) || !Field(data,"Identity",JsonValueKind.Object)) return false;
                if (!Field(data,"Machine",JsonValueKind.Object) || !Text(data.GetProperty("Identity"),"ExecutionIdentity")) return false;
                JsonElement machine=data.GetProperty("Machine");
                if (!Field(machine,"DomainRole",JsonValueKind.Number) || !Boolean(machine,"PartOfDomain") ||
                    !machine.GetProperty("DomainRole").TryGetInt32(out int role) || role<0 || role>5 ||
                    !data.GetProperty("Role").TryGetInt32(out int reportedRole) || role!=reportedRole) return false;
                if (data.GetProperty("Checks").GetProperty("SecureChannel").GetProperty("Status").GetString()=="Completed" && !Field(data,"SecureChannel",JsonValueKind.True)) return false;
                return data.GetProperty("Checks").GetProperty("Tickets").GetProperty("Status").GetString()!="NotApplicable" ||
                    !String.IsNullOrEmpty(request.Profile.JeaEndpoint);
            }
            return CheckSet(data,new[]{"Services","Folders","Events"}) &&
                new[]{"Services","Folders","Events"}.All(n=>Field(data,n,JsonValueKind.Array));
        }
        private static bool SysvolData(JsonElement data,OperationRequest request)
        {
            if (!Field(data,"Shares",JsonValueKind.Array) || !Field(data,"Remote",JsonValueKind.Object) || !Field(data,"Checks",JsonValueKind.Object) ||
                !DiagnosticData(data.GetProperty("Remote"),"sysvol",request)) return false;
            JsonElement checks=data.GetProperty("Checks");
            string[] names={"SYSVOL","NETLOGON","RemoteServices","RemoteFolders","RemoteEvents"};
            if (checks.EnumerateObject().Count()!=names.Length || names.Any(n=>!Field(checks,n,JsonValueKind.Object))) return false;
            JsonElement[] shares=data.GetProperty("Shares").EnumerateArray().ToArray();
            foreach(string name in new[]{"SYSVOL","NETLOGON"}) {
                JsonElement check=checks.GetProperty(name);
                if (!Text(check,"Status") || check.GetProperty("Status").GetString()!="Completed" || !Field(check,"ErrorCode",JsonValueKind.String) ||
                    check.GetProperty("ErrorCode").GetString()!="" || shares.Count(s=>Text(s,"Name") && s.GetProperty("Name").GetString()==name && Field(s,"Accessible",JsonValueKind.True))!=1) return false;
            }
            return shares.Length==2 && new[]{"Services","Folders","Events"}.All(n=>
                checks.GetProperty("Remote"+n).GetRawText()==data.GetProperty("Remote").GetProperty("Checks").GetProperty(n).GetRawText());
        }
        private static bool HasActionData(OperationRequest request,StepResult result)
        {
            if (result.Data is not JsonElement data || data.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return false;
            return request.ActionId switch {
                "technician.inspect" => Text(data,"Version") && Field(data,"Modules",JsonValueKind.Array) && Field(data,"OS",JsonValueKind.Object),
                "rsat.inspect" => Field(data,"Capabilities",JsonValueKind.Array) && Field(data,"Policy",JsonValueKind.Array) && Text(data,"Source"),
                "rsat.install" => Text(data,"Name") && Text(data,"Before") && Text(data,"After") && Boolean(data,"RestartNeeded") &&
                    Field(data,"Modules",JsonValueKind.Array) && Field(data,"Tools",JsonValueKind.Array),
                "session.authenticate" => SelectedServer(data,request) && Text(data,"DNSRoot") &&
                    String.Equals(data.GetProperty("DNSRoot").GetString(),request.Profile.Domain,StringComparison.OrdinalIgnoreCase),
                "domain.discovery" => Field(data,"Interfaces",JsonValueKind.Array) && Field(data,"Srv",JsonValueKind.Array) &&
                    Field(data,"Ports",JsonValueKind.Array) && Field(data,"Locator",JsonValueKind.Object),
                "domain.auth" => DiagnosticData(data,"auth",request),
                "ad.replication" => Field(data,"Metadata",JsonValueKind.Array) && Field(data,"Failures",JsonValueKind.Array) && Field(data,"Repadmin",JsonValueKind.Object),
                "ad.dcdiag" => data.ValueKind==JsonValueKind.Array && data.GetArrayLength()>0 &&
                    data.EnumerateArray().All(d=>Text(d,"Test") && Field(d,"ExitCode",JsonValueKind.Number) && Field(d,"Output",JsonValueKind.String)),
                "ad.object" => data.ValueKind==JsonValueKind.Array && data.GetArrayLength()==2 &&
                    data.EnumerateArray().All(d=>Text(d,"Server") && Field(d,"Object",JsonValueKind.Object) && Field(d,"Metadata",JsonValueKind.Array)),
                "ad.search" => SelectedServer(data,request) && Field(data,"Rows",JsonValueKind.Array) && Field(data,"Page",JsonValueKind.Number) && Boolean(data,"HasMore") && Boolean(data,"LimitReached"),
                "ad.userstate" => Text(data,"ObjectGUID") && Guid.TryParse(data.GetProperty("ObjectGUID").GetString(),out _) &&
                    Text(data,"DistinguishedName") && Boolean(data,"Enabled") && Boolean(data,"LockedOut") && Field(data,"pwdLastSet",JsonValueKind.Number),
                "endpoint.verify" => EndpointReadComplete(request,result),
                "endpoint.gpo" => GpoData(data),
                "domain.sysvol" => SysvolData(data,request),
                "dfs.namespace" => Field(data,"Targets",JsonValueKind.Array) && Field(data,"Reachability",JsonValueKind.Array),
                "dfsr.backlog" => Field(data,"Samples",JsonValueKind.Array) && data.GetProperty("Samples").GetArrayLength()==2 && Field(data,"State",JsonValueKind.Array),
                "endpoint.health" => DiagnosticData(data,"health",request),
                "ad.unlock" or "ad.enable" or "ad.disable" or "ad.password" => Field(data,"Before",JsonValueKind.Object) &&
                    Field(data,"After",JsonValueKind.Object) && Text(data.GetProperty("After"),"ObjectGUID") && Boolean(data.GetProperty("After"),"Enabled") && Boolean(data.GetProperty("After"),"LockedOut"),
                _ when EnterpriseCatalog.Get(request.ActionId).Risk==ActionRisk.EndpointChange =>
                    Field(data,"Verified",JsonValueKind.True) && Field(data,"ExitCode",JsonValueKind.Number) && data.GetProperty("ExitCode").TryGetInt32(out int code) && code==0 &&
                    Field(data,"Output",JsonValueKind.String) && Text(data,"Computer"),
                _ => false
            };
        }
        private static bool GpoData(JsonElement data)
        {
            if (!Field(data,"Endpoint",JsonValueKind.Object) || !CheckSet(data,new[]{"Versions"}) || !Field(data,"Versions",JsonValueKind.Array)) return false;
            JsonElement endpoint=data.GetProperty("Endpoint");
            return CheckSet(endpoint,new[]{"RSOP","Gpresult"}) && Field(endpoint,"ReadComplete",JsonValueKind.True) &&
                Field(endpoint,"NativeExitCode",JsonValueKind.Number) && endpoint.GetProperty("NativeExitCode").TryGetInt32(out int code) && code==0 &&
                Text(endpoint,"Result") && Field(endpoint,"Applied",JsonValueKind.Array);
        }
    }
}
