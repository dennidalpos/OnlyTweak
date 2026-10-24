using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OnlyTweakWindows
{
    internal enum StepState { Success, ObservedIssue, Inconclusive, Unavailable, Failed, Cancelled, Unverified }
    internal enum ActionRisk { ReadOnly, LocalChange, DirectoryChange, EndpointChange }
    internal sealed class EnvironmentProfile
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Customer { get; set; } = "";
        public string Domain { get; set; } = "";
        public string DomainController { get; set; } = "";
        public string[] Targets { get; set; } = Array.Empty<string>();
        public string[] AllowedOus { get; set; } = Array.Empty<string>();
        public string[] ExcludedOus { get; set; } = Array.Empty<string>();
        public string[] ProtectedGroupSids { get; set; } = Array.Empty<string>();
        public bool DelegatedUserOusReviewed { get; set; }
        public bool UseHttps { get; set; }
        public string JeaEndpoint { get; set; } = "";
        public void Validate()
        {
            if (Id == Guid.Empty || String.IsNullOrWhiteSpace(Customer) || Customer.Length > 120 || Customer.Any(Char.IsControl))
                throw new ArgumentException("Cliente non valido.");
            InputRules.Host(Domain); InputRules.Host(DomainController);
            if (!Domain.Contains('.') || Targets == null || Targets.Length < 1 || Targets.Length > 64)
                throw new ArgumentException("Indica dominio DNS e da 1 a 64 destinazioni esplicite.");
            foreach (string host in Targets) InputRules.Host(host);
            if (!Allows(DomainController)) throw new ArgumentException("Il DC deve essere nell'elenco destinazioni.");
            if (AllowedOus == null || ExcludedOus == null || AllowedOus.Length + ExcludedOus.Length > 64)
                throw new ArgumentException("Elenco OU non valido.");
            string suffix = String.Join(",", Domain.Split('.').Select(s => "DC=" + s));
            foreach (string ou in AllowedOus.Concat(ExcludedOus))
                if (String.IsNullOrWhiteSpace(ou) || ou.Length > 2048 || ou.Any(Char.IsControl) ||
                    !ou.StartsWith("OU=", StringComparison.OrdinalIgnoreCase) ||
                    !ou.EndsWith("," + suffix, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("OU non valida o estranea al dominio.");
            if (!Regex.IsMatch(JeaEndpoint ?? "", @"^[A-Za-z0-9_.-]{0,100}$"))
                throw new ArgumentException("Nome endpoint JEA non valido.");
            if (ProtectedGroupSids == null || ProtectedGroupSids.Length>64 ||
                ProtectedGroupSids.Any(s=>!Regex.IsMatch(s??"", @"^S-1-(?:[0-9]+-){1,14}[0-9]+$")))
                throw new ArgumentException("SID dei gruppi protetti non validi.");
        }
        public bool Allows(string target) => Targets != null && Targets.Contains(target, StringComparer.OrdinalIgnoreCase);
        public EnvironmentProfile Copy() => Wire.Read<EnvironmentProfile>(Wire.Write(this));
    }
    internal static class InputRules
    {
        public static void Host(string value)
        {
            if (String.IsNullOrEmpty(value) || value.Length > 253 || !value.Contains('.') ||
                value.Split('.').Any(s => !Regex.IsMatch(s, @"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?$")))
                throw new ArgumentException("Usa un nome DNS completo valido, senza porte, wildcard o comandi.");
        }
        public static void Text(string value, int maximum = 256)
        {
            if (value == null || value.Length > maximum || value.Any(Char.IsControl))
                throw new ArgumentException("Parametro non valido.");
        }
    }
    internal sealed class OperationRequest
    {
        public EnvironmentProfile Profile { get; set; }
        public string ActionId { get; set; } = "";
        public string Target { get; set; } = "";
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
        public string ProtectedSecret { get; set; } = "";
        public bool Confirmed { get; set; }
        public Guid OperationId { get; set; } = Guid.NewGuid();
    }
    internal sealed class ActionDefinition
    {
        public string Id { get; }
        public string Title { get; }
        public ActionRisk Risk { get; }
        public bool Elevated { get; }
        public bool AuthenticationRequired { get; }
        public int TimeoutSeconds { get; }
        public string Source { get; }
        public string[] Parameters { get; }
        public bool IsChange => Risk != ActionRisk.ReadOnly;
        public ActionDefinition(string id, string title, ActionRisk risk, bool elevated, bool authenticated,
            int timeout, string source, params string[] parameters)
        { Id=id; Title=title; Risk=risk; Elevated=elevated; AuthenticationRequired=authenticated;
            TimeoutSeconds=timeout; Source=source; Parameters=parameters; }
        public void Validate(OperationRequest request)
        {
            if (request == null || request.Profile==null || request.OperationId == Guid.Empty || request.ActionId != Id)
                throw new ArgumentException("Richiesta operativa non valida.");
            request.Profile.Validate();
            InputRules.Host(request.Target);
            if (!Elevated && !request.Profile.Allows(request.Target))
                throw new ArgumentException("Destinazione estranea al profilo.");
            if (request.Parameters == null || request.Parameters.Count > Parameters.Length)
                throw new ArgumentException("Parametri inattesi.");
            foreach (var pair in request.Parameters)
            {
                if (!Parameters.Contains(pair.Key, StringComparer.Ordinal)) throw new ArgumentException("Parametro non consentito.");
                InputRules.Text(pair.Value, pair.Key == "source" ? 1024 : 2048);
            }
            if (IsChange && !request.Confirmed) throw new InvalidOperationException("Conferma esplicita richiesta.");
            if (Risk == ActionRisk.DirectoryChange)
            {
                if (request.Profile.AllowedOus.Length == 0 || !request.Profile.DelegatedUserOusReviewed)
                    throw new InvalidOperationException("Configura e revisiona le OU delegate per soli utenti ordinari prima di modificare account.");
                if (!request.Parameters.TryGetValue("objectGuid", out string id) || !Guid.TryParse(id, out _))
                    throw new ArgumentException("Seleziona l'utente tramite GUID.");
                if (!request.Parameters.TryGetValue("expectedDn",out string dn) || String.IsNullOrWhiteSpace(dn))
                    throw new ArgumentException("Percorso dell'utente selezionato richiesto.");
                if (!String.Equals(request.Target, request.Profile.DomainController, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Le modifiche AD richiedono il DC esplicito del profilo.");
            }
            if ((Id == "ad.object" || Id == "ad.replication") && request.Parameters.TryGetValue("peer", out string peer))
            {
                InputRules.Host(peer);
                if (!request.Profile.Allows(peer)) throw new ArgumentException("Il secondo DC deve essere nel profilo.");
            }
            if (Id == "ad.object" && (!request.Parameters.TryGetValue("objectGuid", out string objectId) || !Guid.TryParse(objectId, out _)))
                throw new ArgumentException("GUID oggetto richiesto.");
            if (Id == "ad.search")
            {
                if (!request.Parameters.TryGetValue("query", out string query) || query.Length < 2 || query.Length > 100)
                    throw new ArgumentException("Ricerca: da 2 a 100 caratteri.");
                if (!request.Parameters.TryGetValue("kind", out string kind) || !new[] {"user","group","computer"}.Contains(kind))
                    throw new ArgumentException("Tipo di oggetto non valido.");
                if (request.Parameters.TryGetValue("page", out string page) && (!Int32.TryParse(page,out int index) || index < 0 || index > 19))
                    throw new ArgumentException("Pagina non valida.");
            }
            if (Id == "rsat.install" && (!request.Parameters.TryGetValue("capability", out string cap) || !EnterpriseCatalog.Rsat.ContainsKey(cap)))
                throw new ArgumentException("Componente RSAT non consentito.");
            if (Id == "dfsr.backlog" && (!request.Parameters.ContainsKey("group") || !request.Parameters.ContainsKey("folder") ||
                !request.Parameters.TryGetValue("peer", out string destination) || !request.Profile.Allows(destination) ||
                String.Equals(destination,request.Target,StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Indica gruppo, cartella e partner DFSR distinto presente nel profilo.");
            if (Id=="dfsr.backlog" && new[] {"group","folder"}.Any(k=>String.IsNullOrWhiteSpace(request.Parameters[k]) ||
                request.Parameters[k].IndexOfAny(new[] {'*','?','[',']'})>=0))
                throw new ArgumentException("Gruppo e cartella DFSR espliciti, senza wildcard.");
            if (Id=="ad.userstate" && (!request.Parameters.TryGetValue("objectGuid",out string guid) || !Guid.TryParse(guid,out _)))
                throw new ArgumentException("GUID utente richiesto.");
            if (Id=="endpoint.verify" && (!request.Parameters.TryGetValue("operation",out string operation) ||
                !EnterpriseCatalog.Actions.Any(a=>a.Id==operation && a.Risk==ActionRisk.EndpointChange)))
                throw new ArgumentException("Intervento da verificare non consentito.");
            if (!String.IsNullOrEmpty(request.ProtectedSecret) && Id != "ad.password")
                throw new ArgumentException("Segreto non ammesso per questa azione.");
        }
    }
    internal sealed class RunbookDefinition
    {
        public string Id { get; }
        public string Title { get; }
        public string[] Steps { get; }
        public string Interpretation { get; }
        public RunbookDefinition(string id,string title,string interpretation,params string[] steps)
        { Id=id; Title=title; Interpretation=interpretation; Steps=steps; }
        public override string ToString() => Title;
    }
    internal sealed class StepResult
    {
        public Guid OperationId { get; set; }
        public string ActionId { get; set; } = "";
        public Guid ProfileId { get; set; }
        public string Target { get; set; } = "";
        public string Identity { get; set; } = "";
        public bool AuthenticationVerified { get; set; }
        public DateTimeOffset Started { get; set; }
        public DateTimeOffset Finished { get; set; }
        public StepState State { get; set; }
        public string Summary { get; set; } = "";
        public string[] Evidence { get; set; } = Array.Empty<string>();
        public JsonElement? Data { get; set; }
        public string ErrorCode { get; set; } = "";
        public bool Truncated { get; set; }
    }
    internal static class Wire
    {
        internal static readonly JsonSerializerOptions Options = new JsonSerializerOptions {
            PropertyNameCaseInsensitive=true, Converters={new JsonStringEnumConverter()}, MaxDepth=32
        };
        public static string Write<T>(T value) => JsonSerializer.Serialize(value,Options);
        public static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value,Options) ?? throw new FormatException("Messaggio vuoto.");
    }
}
