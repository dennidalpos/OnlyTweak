using System;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace OnlyTweakWindows
{
    // Rende leggibile il Data di un risultato: sezioni brevi al posto di una riga JSON che esce dalla finestra.
    internal static class OperationReport
    {
        private static readonly JsonSerializerOptions Indented=new JsonSerializerOptions {WriteIndented=true};
        internal static string Describe(StepResult result)
        {
            if(result.Data is not JsonElement data || data.ValueKind==JsonValueKind.Null || data.ValueKind==JsonValueKind.Undefined) return "";
            try {return Describe(data,result.ActionId??"");}
            catch(Exception) {return JsonSerializer.Serialize(data,Indented);}
        }
        internal static string Describe(JsonElement data,string actionId)
        {
            if(data.ValueKind!=JsonValueKind.Object) return JsonSerializer.Serialize(data,Indented);
            var text=new StringBuilder();
            if(data.TryGetProperty("RequestAccepted",out _)) {
                text.AppendLine("Esito: richiesta "+Flag(data,"RequestAccepted")+" · esecuzione "+Flag(data,"ExecutionCompleted")+
                    " · effetto verificato "+Flag(data,"EffectVerified")+" · audit completo "+Flag(data,"AuditComplete"));
                JsonElement before=Section(data,"Before"),after=Section(data,"After");
                bool same=before.ValueKind==JsonValueKind.Object && after.ValueKind==JsonValueKind.Object && before.GetRawText()==after.GetRawText();
                if(before.ValueKind==JsonValueKind.Object) {text.AppendLine();text.AppendLine("Rilevato prima:");Append(text,before,actionId);}
                if(same) {text.AppendLine();text.AppendLine("Rilevato dopo: identico a prima, nessuna modifica applicata.");}
                else if(after.ValueKind==JsonValueKind.Object) {text.AppendLine();text.AppendLine("Rilevato dopo:");Append(text,after,actionId);}
                return text.ToString().TrimEnd();
            }
            Append(text,data,actionId);
            return text.ToString().TrimEnd();
        }
        private static string Flag(JsonElement value,string name) =>
            value.TryGetProperty(name,out var field) ? field.ValueKind==JsonValueKind.True ? "sì" : field.ValueKind==JsonValueKind.False ? "no" : "n/d" : "n/d";
        private static JsonElement Section(JsonElement data,string name) =>
            data.TryGetProperty(name,out var section) && section.ValueKind==JsonValueKind.Object && section.TryGetProperty("Data",out var inner) ? inner : default;
        private static void Append(StringBuilder text,JsonElement data,string actionId)
        {
            if(data.ValueKind==JsonValueKind.Object && data.TryGetProperty("Configuration",out var configuration) && configuration.ValueKind==JsonValueKind.Object)
                AppendRemoting(text,data,configuration,actionId);
            else text.AppendLine(JsonSerializer.Serialize(data,Indented));
        }
        private static string Str(JsonElement value,string name) =>
            value.ValueKind==JsonValueKind.Object && value.TryGetProperty(name,out var field) && field.ValueKind!=JsonValueKind.Null ? field.ToString() : "";
        private static void AppendRemoting(StringBuilder text,JsonElement data,JsonElement configuration,string actionId)
        {
            if(configuration.TryGetProperty("Service",out var service) && service.ValueKind==JsonValueKind.Object)
                text.AppendLine("  Servizio WinRM: "+Str(service,"State")+" · avvio "+Str(service,"StartMode"));
            var listeners=configuration.TryGetProperty("Listeners",out var list) && list.ValueKind==JsonValueKind.Array ? list.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
            text.AppendLine("  Listener WinRM: "+(listeners.Length==0 ? "nessuno" : listeners.Length.ToString()));
            foreach(var listener in listeners) {
                var keys=listener.TryGetProperty("Keys",out var k) && k.ValueKind==JsonValueKind.Array ? k.EnumerateArray().Select(x=>x.GetString()).ToArray() : Array.Empty<string>();
                var values=listener.TryGetProperty("Values",out var v) && v.ValueKind==JsonValueKind.Array ? v.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
                string Value(string name)=>values.Where(x=>Str(x,"Name")==name).Select(x=>Str(x,"Value")).FirstOrDefault()??"";
                var on=values.Where(x=>Str(x,"Name").StartsWith("ListeningOn",StringComparison.Ordinal)).Select(x=>Str(x,"Value")).ToArray();
                var ipv4=on.Where(a=>System.Net.IPAddress.TryParse(a,out var ip) && ip.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork).ToArray();
                string address=keys.FirstOrDefault(x=>x!=null && x.StartsWith("Address=",StringComparison.Ordinal))?.Substring(8)??Value("Address");
                text.AppendLine("    • "+Str(listener,"Name")+": "+Value("Transport")+" "+address+":"+Value("Port")+" · attivo "+Value("Enabled"));
                text.AppendLine("      IPv4 in ascolto: "+(ipv4.Length==0 ? "nessuno" : String.Join(", ",ipv4))+(on.Length>ipv4.Length ? " (+"+(on.Length-ipv4.Length)+" IPv6)" : ""));
            }
            bool endpoint=configuration.TryGetProperty("Endpoint",out var e) && e.ValueKind==JsonValueKind.Object;
            bool rule=configuration.TryGetProperty("Rule",out var r) && r.ValueKind==JsonValueKind.Object;
            text.AppendLine("  Endpoint OnlyTweak.Local: "+(endpoint ? "presente" : "assente"));
            text.AppendLine("  Regola firewall OnlyTweak: "+(rule ? "presente" : "assente"));
            string phase=Str(data,"CheckpointPhase");
            if(phase.Length>0) text.AppendLine("  Checkpoint dell'app: "+(phase=="Absent" ? "assente" : phase));
            bool created=phase is "Applied" or "Applying" or "UndoPending";
            if((listeners.Length>0 || endpoint || rule) && !created && actionId.StartsWith("winrm.",StringComparison.Ordinal))
                text.AppendLine("  Conflitto: configurazione WinRM esistente non creata dall'app (listener, endpoint o regola). L'app non la modifica.");
        }
    }
}
