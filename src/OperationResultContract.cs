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
            if(request.ActionId is "local.dns" or "local.tcp" && parsed.State is StepState.Success or StepState.ObservedIssue && !LocalDiagnosticData(request,parsed))
                throw new FormatException("Evidenze diagnostiche insufficienti.");
            if (parsed.Truncated || (parsed.State==StepState.Success &&
                (!String.IsNullOrEmpty(parsed.ErrorCode) || !HasActionData(request,parsed))))
                throw new FormatException("Evidenze insufficienti per un risultato positivo.");
            return parsed;
        }
        private static bool LocalDiagnosticData(OperationRequest request,StepResult result)
        {
            if(result.AuthenticationVerified || result.Data is not JsonElement data || !Text(data,"Target") || data.GetProperty("Target").GetString()!=request.Target || !Field(data,"ReadComplete",JsonValueKind.True)) return false;
            bool success=result.State==StepState.Success;
            if(request.ActionId=="local.tcp") return Field(data,"Port",JsonValueKind.Number) && data.GetProperty("Port").TryGetInt32(out int port) &&
                request.Parameters.TryGetValue("port",out string requestedPort) && port.ToString(System.Globalization.CultureInfo.InvariantCulture)==requestedPort &&
                Field(data,"TcpTestSucceeded",success ? JsonValueKind.True : JsonValueKind.False);
            return Text(data,"RecordType") && request.Parameters.TryGetValue("recordType",out string type) && data.GetProperty("RecordType").GetString()==type &&
                Field(data,"Records",JsonValueKind.Array) && (success ? data.GetProperty("Records").GetArrayLength()>0 : data.GetProperty("Records").GetArrayLength()==0);
        }
        private static bool HasActionData(OperationRequest request,StepResult result) {
            if(request.ActionId is "local.dns" or "local.tcp") return LocalDiagnosticData(request,result);
            if(request.ActionId.StartsWith("winrm.",StringComparison.Ordinal) && result.Data is JsonElement data) {
                return Field(data,"ReadComplete",JsonValueKind.True) && Boolean(data,"Configured") &&
                    Text(data,"Computer") && data.GetProperty("Computer").GetString()==Environment.MachineName &&
                    Text(data,"Action") && data.GetProperty("Action").GetString()==request.ActionId &&
                    (request.ActionId=="winrm.inspect" || request.ActionId=="winrm.undo" && Field(data,"Restored",JsonValueKind.True) ||
                     request.ActionId.StartsWith("winrm.enable.",StringComparison.Ordinal) && Field(data,"Configured",JsonValueKind.True));
            }
            return false;
        }
    }
}
