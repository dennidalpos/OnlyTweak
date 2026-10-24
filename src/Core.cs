using System;
using System.Collections.Generic;
using System.Threading;

namespace OnlyTweakWindows
{
    internal sealed class ActionSpec
    {
        public string Id, Title, Description, Scope, Confirmation, Restart;
        // Riga breve per le card; Description resta il testo completo (tooltip, accessibilità, dettaglio operazione).
        public string Summary;
        public string CardText { get { return String.IsNullOrEmpty(Summary) ? Description : Summary; } }
        public Action<Action<string>> Execute;
        public Func<string> Unavailable;
        public bool OpensWindow;
        public bool RequiresElevation;
        public Func<bool> IsApplicable;
        public bool Applicable { get { return IsApplicable==null || IsApplicable(); } }
        public ActionSpec(string id, string title, string description, string scope, Action<Action<string>> execute)
        { Id = id; Title = title; Description = description; Scope = scope; Execute = execute; }
    }

    internal sealed class OperationCoordinator
    {
        private int busy;
        public bool IsBusy { get { return Interlocked.CompareExchange(ref busy, 0, 0) != 0; } }
        public bool TryEnter() { return Interlocked.CompareExchange(ref busy, 1, 0) == 0; }
        public void Leave() { Interlocked.Exchange(ref busy, 0); }
    }

    internal sealed class SessionHistory
    {
        private readonly List<string> entries = new List<string>();
        public void Add(string text) { lock (entries) { entries.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + (text.Length>4096 ? text.Substring(0,4096) : text)); if (entries.Count>2000) entries.RemoveAt(0); } }
        public string Read() { lock (entries) return entries.Count == 0 ? "Nessuna operazione in questa sessione." : String.Join(Environment.NewLine, entries.ToArray()); }
    }

    internal sealed class AppServices
    {
        public readonly OperationCoordinator Coordinator = new OperationCoordinator();
        public readonly SessionHistory History = new SessionHistory();
        public readonly ProcessService Processes = new ProcessService();
        public readonly PrinterService Printers = new PrinterService();
        public readonly SpoolService Spool;
        public readonly ActionCatalog Catalog;
        public readonly SecurityService Security;
        public readonly OsProfile OS;
        public readonly FeatureAvailability Availability;
        public AppServices() : this(new WindowsSecurityBackend()) { }
        internal AppServices(ISecurityBackend security) : this(security,EnvironmentInfo.Profile,null) { }
        internal AppServices(ISecurityBackend security,OsProfile os,FeatureAvailability availability)
        {
            OS=os;
            Availability=availability??new FeatureAvailability(os,name=>Processes.FileUnavailable(name)==null,target=>Processes.TargetUnavailable(target)==null);
            Security=new SecurityService(security);
            Spool = new SpoolService(new WindowsSpoolBackend());
            Catalog = new ActionCatalog(this);
        }
    }
}
