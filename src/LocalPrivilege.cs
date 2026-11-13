using System;
using System.Linq;
using System.Security.Principal;
using System.Threading;

namespace OnlyTweakWindows
{
    internal static class LocalPrivilege
    {
        public static bool IsAdministrator {
            get {using WindowsIdentity identity=WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);}
        }
        public static StepResult Execute(string id,Action<string> log,CancellationToken cancellation=default,bool readOnly=false)
        {
            using var session=ElevatedSession.Start().GetAwaiter().GetResult();
            StepResult result=session.ExecuteLocal(id,readOnly,cancellation).GetAwaiter().GetResult();
            foreach(string line in result.Evidence) log(line);
            return result;
        }
        internal static ActionSpec Find(AppServices app,string id)
        {
            if(id.StartsWith("winrm.",StringComparison.Ordinal)) return LocalRemoting.Action(id);
            string[] allowed={"spool.restart","spool.clean","network.dns","network.winsock","network.tcpip",
                "sfc.verify","sfc.repair","dism.scan","dism.repair"};
            if(!allowed.Contains(id,StringComparer.Ordinal)) throw new ArgumentException("Azione locale non consentita.");
            return new[]{app.Catalog.Network,app.Catalog.Maintenance,app.Catalog.Spool}.SelectMany(m=>m.AllActions())
                .SingleOrDefault(a=>a.Id==id)??throw new InvalidOperationException("Componente non disponibile.");
        }
        internal static StepResult RunAuthorized(string id,CancellationToken cancellation=default,bool readOnly=false,bool confirmed=false)
        {
            if(!IsAdministrator) throw new InvalidOperationException("Elevazione locale richiesta.");
            var app=new AppServices();ActionSpec action=Find(app,id);
            if(!action.Applicable) throw new InvalidOperationException("Componente non disponibile.");
            if(readOnly) return app.LocalOperations.Read(action,delegate{},cancellation);
            if(!action.IsDiagnostic && !confirmed)
                return new StepResult {ActionId=id,Target=Environment.MachineName,State=StepState.Cancelled,Summary="Operazione annullata."};
            return app.LocalOperations.Execute(action,delegate{},cancellation);
        }
    }
}
