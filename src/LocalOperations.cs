using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;

namespace OnlyTweakWindows
{
    internal sealed record NativeObservation(int ExitCode,string Output,bool Truncated);
    internal sealed record LocalObservation(bool ReadComplete,bool? Satisfied,object Data);
    internal sealed class LocalOperationExecutor
    {
        private readonly IOperationAudit audit;
        private readonly HashSet<string> uncertain=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal LocalOperationExecutor(IOperationAudit audit=null) { this.audit=audit??new FileOperationAudit(); }
        internal static bool IsDiagnostic(string id) => Array.IndexOf(LocalDiagnostics.Ids,id)>=0 || id is "spool.status" or "network.info" or "network.ipconfig" or "sfc.verify" or "dism.scan";
        private static string Key(ActionSpec action) => action.Id+"|"+action.Scope;
        internal void Authorize(ActionSpec action) {
            if(!action.IsDiagnostic && uncertain.Contains(Key(action))) throw new InvalidOperationException("Esito incerto: usa Rileggi stato prima di ripetere l'intervento.");
        }
        internal void Observe(ActionSpec action,StepResult result,bool read=false) {
            if(result.ActionId!=action.Id || result.Target!=Environment.MachineName) return;
            if(!action.IsDiagnostic && result.State==StepState.Unverified) uncertain.Add(Key(action));
            if(read && result.State==StepState.Success && !result.Truncated && result.Data is JsonElement data &&
                data.TryGetProperty("ReadComplete",out JsonElement complete) && complete.ValueKind==JsonValueKind.True) uncertain.Remove(Key(action));
        }
        internal StepResult Read(ActionSpec action,Action<string> log,CancellationToken cancellation)
        {
            StepResult result=NewResult(action);
            try {
                cancellation.ThrowIfCancellationRequested();
                LocalObservation observation=action.Observe?.Invoke(log,cancellation);
                bool complete=observation!=null && observation.ReadComplete && observation.Data!=null;
                result.State=complete ? StepState.Success : StepState.Inconclusive;
                result.Summary=complete ? "Stato riletto; non certifica l'effetto della riparazione." : "Rilettura incompleta: retry ancora bloccato.";
                result.Data=JsonSerializer.SerializeToElement(new {ReadComplete=complete,Data=observation?.Data});
            } catch(OperationCanceledException) {result.State=StepState.Cancelled;result.Summary="Rilettura annullata.";}
            catch(Exception ex) {result.State=StepState.Inconclusive;result.ErrorCode=ex.GetType().Name;result.Summary="Rilettura non disponibile.";}
            result.Finished=DateTimeOffset.UtcNow; Observe(action,result,true); return result;
        }
        internal StepResult Execute(ActionSpec action,Action<string> log,CancellationToken cancellation)
        {
            Authorize(action);
            StepResult result=NewResult(action);
            OperationRequest request=new OperationRequest {OperationId=result.OperationId,Profile=LocalPrivilege.TechnicianProfile(),ActionId=action.Id,Target=result.Target,Confirmed=true};
            if(action.Id=="printer.purge") request.Parameters["printer"]=action.Scope;
            BoundedText output=new BoundedText();
            void Write(string line) { output.Append((line??"")+Environment.NewLine);log(line); }
            bool requested=false,completed=false; LocalObservation before=null,after=null; StepResult execution=null;
            try {
                audit.Record(request,result.Identity,"requested"); // Fail closed before any operation, including diagnostics.
                requested=true;
                if(action.IsDiagnostic) cancellation.ThrowIfCancellationRequested();
                else {
                    before=action.Observe?.Invoke(Write,CancellationToken.None);
                    if(before==null || !before.ReadComplete || before.Data==null) throw new InvalidOperationException("Rilettura preventiva incompleta: intervento non avviato.");
                }
                if(action.Unavailable!=null) {string reason=action.Unavailable();if(reason!=null) throw new InvalidOperationException(reason);}
                if(action.ExecuteResult!=null) execution=action.ExecuteResult(Write,action.IsDiagnostic ? cancellation : CancellationToken.None);
                else {action.Execute(Write);execution=new StepResult {State=StepState.Success};}
                completed=execution!=null && (execution.State==StepState.Success || (action.IsDiagnostic && execution.State==StepState.ObservedIssue));
                if(!action.IsDiagnostic) after=action.Observe?.Invoke(Write,CancellationToken.None);
                result.State=action.IsDiagnostic ? (execution?.State??StepState.Inconclusive) :
                    completed && after!=null && after.ReadComplete && after.Satisfied==true ? StepState.Success : StepState.Unverified;
                result.Summary=action.IsDiagnostic ? (!String.IsNullOrWhiteSpace(execution?.Summary) ? execution.Summary :
                    result.State==StepState.Success ? "Diagnostica conclusa: leggere le evidenze." : result.State==StepState.ObservedIssue ? "Test negativo osservato: leggere le evidenze." : "Diagnostica incompleta o indisponibile: nessuna conclusione positiva.") : result.State==StepState.Success ?
                    "Esecuzione conclusa e condizione finale osservata; verificare anche il dispositivo interessato." : "Richiesta/esecuzione distinta dall'effetto: intervento non verificato. Rileggere prima del retry.";
                result.ErrorCode=execution?.ErrorCode??"";
            } catch(OperationCanceledException) {result.State=StepState.Cancelled;result.ErrorCode="DiagnosticCancelled";result.Summary="Diagnostica annullata; esito sconosciuto.";}
            catch(TimeoutException) {result.State=StepState.Inconclusive;result.ErrorCode="DiagnosticTimeout";result.Summary="Timeout diagnostico; esito sconosciuto.";}
            catch(Exception ex) {result.State=action.IsDiagnostic ? StepState.Inconclusive : StepState.Unverified;result.ErrorCode=ex.GetType().Name;result.Summary="Operazione incompleta: "+ex.Message;}
            finally {
                if(!action.IsDiagnostic && before?.ReadComplete==true && after==null) try {after=action.Observe?.Invoke(Write,CancellationToken.None);} catch(Exception) { }
                result.Truncated=output.Truncated || (execution?.Truncated??false);
                if(result.Truncated) {result.State=action.IsDiagnostic ? StepState.Inconclusive : StepState.Unverified;result.ErrorCode="OutputBudgetExceeded";}
                result.Evidence=new[]{output.Read()};result.Finished=DateTimeOffset.UtcNow;
                bool auditComplete=false;
                if(requested) try {audit.Record(request,result.Identity,"finished",result);auditComplete=true;}
                    catch(Exception) {result.State=action.IsDiagnostic ? StepState.Inconclusive : StepState.Unverified;result.ErrorCode="FinalAuditUnavailable";result.Summary="Audit finale non disponibile: risultato non verificato.";}
                result.Data=JsonSerializer.SerializeToElement(new {RequestAccepted=requested,ExecutionCompleted=completed,EffectVerified=!action.IsDiagnostic && result.State==StepState.Success,
                    AuditComplete=auditComplete,Execution=execution?.Data,Before=before,After=after});
                Observe(action,result);
            }
            return result;
        }
        private static StepResult NewResult(ActionSpec action) => new StepResult {OperationId=Guid.NewGuid(),ProfileId=LocalPrivilege.TechnicianProfile().Id,
            ActionId=action.Id,Target=Environment.MachineName,Identity=EnvironmentInfo.Account,Started=DateTimeOffset.UtcNow,State=StepState.Unverified};
    }
}
