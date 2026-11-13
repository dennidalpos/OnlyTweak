using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace OnlyTweakWindows
{
    internal enum StepState { Success, ObservedIssue, Inconclusive, Unavailable, Failed, Cancelled, Unverified }
    internal enum ActionRisk { ReadOnly, LocalChange }
    internal static class InputRules {
        public static void Host(string value) {
            if(String.IsNullOrEmpty(value) || value.Length>253 || !value.Contains('.') ||
                value.Split('.').Any(s=>!Regex.IsMatch(s,@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?$")))
                throw new ArgumentException("Usa un nome DNS completo valido, senza porte, wildcard o comandi.");
        }
    }
    internal sealed class OperationRequest {
        public string ActionId {get;set;}="";
        public string Target {get;set;}="";
        public Dictionary<string,string> Parameters {get;set;}=new();
        public bool Confirmed {get;set;}
        public Guid OperationId {get;set;}=Guid.NewGuid();
    }
    internal sealed class ActionDefinition {
        public string Id {get;}
        public string Title {get;}
        public ActionRisk Risk {get;}
        public bool Elevated {get;}
        public int TimeoutSeconds {get;}
        public bool IsChange=>Risk!=ActionRisk.ReadOnly;
        public ActionDefinition(string id,string title,ActionRisk risk,bool elevated,int timeout)
        {Id=id;Title=title;Risk=risk;Elevated=elevated;TimeoutSeconds=timeout;}
    }
    internal sealed class StepResult
    {
        public Guid OperationId { get; set; }
        public string ActionId { get; set; } = "";
        public string Target { get; set; } = "";
        public string Identity { get; set; } = "";
        public bool AuthenticationVerified { get; set; }
        public DateTimeOffset Started { get; set; }
        public DateTimeOffset Finished { get; set; }
        public StepState State { get; set; } = StepState.Inconclusive;
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
