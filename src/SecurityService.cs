using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace OnlyTweakWindows
{
    internal sealed class SecurityDefinition
    {
        public readonly string Id,Title,Description,Page;
        public readonly bool Direct,Restart;
        public SecurityDefinition(string id,string title,string description,string page,bool direct,bool restart)
        { Id=id; Title=title; Description=description; Page=page; Direct=direct; Restart=restart; }
    }
    internal sealed class SecurityEnvironment
    {
        public bool PolicyEdition,ManagementKnown,Managed,DeviceGuardKnown,HvciConfigured,HvciRunning,VbsRunning,KernelConfigured,KernelRunning,
            DefenderKnown,DefenderActive,TamperKnown,TamperProtected,SMode;
        public int Pua;
    }
    internal sealed class SecurityStatus
    {
        public string Configured,Effective,EnableUnavailable,DisableUnavailable,RestoreUnavailable;
        public bool RestartPending;
        public override string ToString()
        { return "Configurazione: "+Configured+"\nEffettivo: "+Effective+(RestartPending ? "\nRiavvio manuale richiesto; verifica lo stato dopo il riavvio." : ""); }
    }
    internal interface ISecurityBackend
    {
        IRegistryStore Registry { get; }
        SecurityEnvironment Probe();
        bool HasTemplate(string name,string value);
        void SetPua(int mode);
    }
    internal sealed class SecurityValue
    {
        public string Path,Name;
        public object Data;
        public RegistryValueKind Kind;
        public SecurityValue(string path,string name,object data,RegistryValueKind kind)
        { Path=path; Name=name; Data=data; Kind=kind; }
    }
    internal sealed class SecuritySnapshot
    {
        public readonly List<SecurityValue> Values=new List<SecurityValue>();
        public readonly List<RegistryValueState> States=new List<RegistryValueState>();
        public readonly List<string> MissingKeys=new List<string>();
        public int Pua;
    }
    internal sealed class SecurityService
    {
        internal const string SmartPolicy=@"SOFTWARE\Policies\Microsoft\Windows\System";
        internal const string InstallPolicy=@"SOFTWARE\Policies\Microsoft\Windows Defender\SmartScreen";
        internal const string GuardPolicy=@"SOFTWARE\Policies\Microsoft\Windows\DeviceGuard";
        internal const string Guard=@"SYSTEM\CurrentControlSet\Control\DeviceGuard";
        internal const string Hvci=Guard+@"\Scenarios\HypervisorEnforcedCodeIntegrity";
        internal const string CiPolicy=@"SYSTEM\CurrentControlSet\Control\CI\Policy";
        internal const string CiConfig=@"SYSTEM\CurrentControlSet\Control\CI\Config";
        private readonly ISecurityBackend backend;
        private readonly Dictionary<string,SecuritySnapshot> snapshots=new Dictionary<string,SecuritySnapshot>();
        private readonly Dictionary<string,SecuritySnapshot> expected=new Dictionary<string,SecuritySnapshot>();
        private readonly HashSet<string> pending=new HashSet<string>();
        private bool kernelWasVerified,hvciWasVerified;
        public readonly SecurityDefinition[] Definitions={
            new SecurityDefinition("sac","Controllo intelligente delle app","Smart App Control può bloccare app non firmate. Modifica nella pagina Windows; la riattivazione dipende da aggiornamenti e disponibilità del dispositivo.","windowsdefender://smartapp/",false,false),
            new SecurityDefinition("smartscreen","SmartScreen · Controlla app e file","Controlla la reputazione degli eseguibili. Il comando diretto imposta un criterio locale per il computer, distinto dai filtri del browser.","windowsdefender://smartscreenpua/",true,false),
            new SecurityDefinition("pua","App potenzialmente indesiderate","Protezione PUA di Microsoft Defender Antivirus. Il filtro dei download del browser si gestisce separatamente in Windows.","windowsdefender://smartscreenpua/",true,false),
            new SecurityDefinition("store","Origine delle applicazioni","Scegli se consentire app da qualsiasi origine o limitarle al Microsoft Store. In Windows: App > Impostazioni avanzate delle app > Scegli dove scaricare le app. Non modifica la modalità S.","ms-settings:appsfeatures",true,false),
            new SecurityDefinition("hvci","Integrità della memoria","Protezione HVCI nel gruppo Isolamento core. Disattivandola possono cessare anche le protezioni che la richiedono; nessun altro interruttore viene modificato.","windowsdefender://coreisolation/",true,true),
            new SecurityDefinition("kernel","Protezione stack kernel","Protegge lo stack in modalità kernel. Richiede hardware compatibile, VBS e integrità della memoria; riattivazione diretta solo con supporto già verificato da Windows.","windowsdefender://coreisolation/",true,true),
            new SecurityDefinition("drivers","Blocklist driver vulnerabili","Si modifica in Isolamento core. Può essere imposta da HVCI, Smart App Control o modalità S anche se il valore locale è disattivato.","windowsdefender://coreisolation/",false,true)
        };
        public SecurityService(ISecurityBackend backend) { this.backend=backend; }
        public bool CanRestore(SecurityDefinition definition) { return snapshots.ContainsKey(definition.Id); }
        private int Number(string path,string name,int fallback)
        {
            RegistryValueState state=backend.Registry.Read(path,name);
            return state.ValueExists && state.Kind==RegistryValueKind.DWord ? (int)state.Data : fallback;
        }
        private string Config(string path,string name)
        {
            RegistryValueState state=backend.Registry.Read(path,name);
            if (!state.ValueExists) return "non impostata (predefinita Windows)";
            if (state.Kind==RegistryValueKind.DWord)
            {
                int number=(int)state.Data;
                if (name=="ConfigureKernelShadowStacksLaunch") return number==0 ? "predefinita Windows" : number==1 ? "attivata (criterio locale)" : number==2 ? "Audit (criterio locale)" : number==3 ? "disattivata (criterio locale)" : "personalizzata";
                if (number==0) return "disattivata"; if (number==1) return "attivata";
            }
            if (state.Kind==RegistryValueKind.String)
            {
                string text=Convert.ToString(state.Data);
                if (text=="Anywhere") return "qualsiasi origine"; if (text=="StoreOnly") return "solo Microsoft Store";
                if (text=="Recommendations") return "mostra consigli"; if (text=="PreferStore") return "preferisci Microsoft Store";
                if (text=="Warn") return "avviso superabile"; if (text=="Block") return "blocco senza esclusione";
            }
            return "configurazione personalizzata ("+state.Kind+")";
        }
        private bool HasPolicy(string path,string name) { return backend.Registry.Read(path,name).ValueExists; }
        private bool Locked(string path)
        {
            RegistryValueState state=backend.Registry.Read(path,"Locked");
            return state.ValueExists && (state.Kind!=RegistryValueKind.DWord || !Object.Equals(state.Data,0));
        }
        private bool OwnsCurrent(string id)
        {
            SecuritySnapshot previous; if (!expected.TryGetValue(id,out previous)) return false;
            for (int i=0;i<previous.Values.Count;i++)
            {
                SecurityValue value=previous.Values[i]; RegistryValueState before=previous.States[i],actual=backend.Registry.Read(value.Path,value.Name);
                if (actual.ValueExists!=before.ValueExists || (actual.ValueExists && (actual.Kind!=before.Kind || !Equal(actual.Data,before.Data)))) return false;
            }
            return true;
        }
        private string Block(SecurityDefinition d,bool enable,SecurityEnvironment e)
        {
            if (!d.Direct) return "Usa la pagina Windows dedicata.";
            if (!e.ManagementKnown) return "Impossibile verificare la gestione del dispositivo. Usa Windows.";
            if (e.Managed) return "Dispositivo gestito da dominio/organizzazione: modifica nella gestione ufficiale.";
            if (d.Id!="pua" && expected.ContainsKey(d.Id) && !OwnsCurrent(d.Id)) return "Configurazione modificata fuori da OnlyTweak durante la sessione: usa Windows.";
            if (d.Id=="pua")
            {
                if (!e.DefenderKnown || !e.DefenderActive) return "Microsoft Defender non disponibile o non attivo: usa Windows.";
                if (e.Pua<0 || e.Pua>2 || !e.TamperKnown) return "Stato PUA/antimanomissione non verificabile: usa Windows.";
                if (e.TamperProtected) return "Protezione antimanomissione attiva: usa Windows; non viene disattivata.";
                if (HasPolicy(@"SOFTWARE\Policies\Microsoft\Windows Defender","PUAProtection") ||
                    HasPolicy(@"SOFTWARE\Policies\Microsoft\Windows Defender\MpEngine","MpEnablePus") ||
                    HasPolicy(@"SOFTWARE\Microsoft\PolicyManager\current\device\Defender","PUAProtection"))
                    return "Protezione PUA configurata da un criterio esterno.";
                return null;
            }
            if (d.Id=="smartscreen" || d.Id=="store" || d.Id=="kernel")
            {
                if (!e.PolicyEdition) return "Edizione senza supporto per questo criterio: usa la pagina Windows.";
                string template=d.Id=="kernel" ? "DeviceGuard.admx" : "SmartScreen.admx";
                string value=d.Id=="kernel" ? "ConfigureKernelShadowStacksLaunch" : d.Id=="store" ? "ConfigureAppInstallControl" : "EnableSmartScreen";
                if (!backend.HasTemplate(template,value)) return "Modello amministrativo compatibile non disponibile: usa Windows.";
                if (!OwnsCurrent(d.Id))
                    foreach (SecurityValue item in Values(d,true))
                        if (HasPolicy(item.Path,item.Name)) return "Impostazione già definita da un criterio esterno alla sessione: usa Windows.";
            }
            if (d.Id=="store" && e.SMode) return "Modalità S: gestisci l'eventuale uscita con la procedura Windows.";
            if (d.Id=="hvci" || d.Id=="kernel")
            {
                if (!e.DeviceGuardKnown) return "Stato VBS/HVCI non verificabile: usa Isolamento core.";
                if (Locked(Guard) || Locked(Hvci)) return "Blocco UEFI rilevato o non verificabile: modifica diretta non disponibile.";
                if (HasPolicy(GuardPolicy,"EnableVirtualizationBasedSecurity") || HasPolicy(GuardPolicy,"HypervisorEnforcedCodeIntegrity") ||
                    (d.Id=="hvci" && HasPolicy(GuardPolicy,"ConfigureKernelShadowStacksLaunch") && !OwnsCurrent("kernel")))
                    return "VBS/Isolamento core configurato da un criterio: usa la gestione ufficiale.";
                if (enable && d.Id=="hvci" && !e.VbsRunning && Number(Guard,"EnableVirtualizationBasedSecurity",0)!=1 && !hvciWasVerified)
                    return "Prima attivazione VBS da eseguire in Isolamento core.";
                if (enable && d.Id=="kernel" && (!e.HvciRunning || !e.VbsRunning || !kernelWasVerified))
                    return "Occorrono VBS/HVCI in esecuzione e supporto stack già verificato da Windows. Usa Isolamento core.";
            }
            return null;
        }
        public SecurityStatus Read(SecurityDefinition d,SecurityEnvironment e)
        {
            kernelWasVerified|=e.KernelRunning; hvciWasVerified|=e.HvciRunning;
            SecurityStatus state=new SecurityStatus { Effective="non verificabile dalla sola configurazione",RestartPending=pending.Contains(d.Id) };
            switch (d.Id)
            {
                case "sac":
                    int sac=Number(CiPolicy,"VerifiedAndReputablePolicyState",-1);
                    state.Configured=sac==0 ? "disattivato" : sac==1 ? "attivato" : sac==2 ? "valutazione" : "sconosciuta"; break;
                case "smartscreen": state.Configured=Config(SmartPolicy,"EnableSmartScreen")+"; livello: "+Config(SmartPolicy,"ShellSmartScreenLevel"); break;
                case "store": state.Configured=Config(InstallPolicy,"ConfigureAppInstallControlEnabled")+"; origine: "+Config(InstallPolicy,"ConfigureAppInstallControl"); break;
                case "pua": state.Configured=e.DefenderKnown ? e.Pua==0 ? "disattivata" : e.Pua==1 ? "blocco attivo" : e.Pua==2 ? "Audit" : "sconosciuta" : "sconosciuta";
                    state.Effective=!e.DefenderKnown ? "non leggibile" : !e.DefenderActive ? "Defender non attivo" : state.Configured; break;
                case "hvci": state.Configured=Config(Hvci,"Enabled"); state.Effective=!e.DeviceGuardKnown ? "sconosciuto" : e.HvciRunning ? "HVCI in esecuzione" : "HVCI non in esecuzione"; break;
                case "kernel": state.Configured=Config(GuardPolicy,"ConfigureKernelShadowStacksLaunch"); state.Effective=!e.DeviceGuardKnown ? "sconosciuto" : e.KernelRunning ? "stack kernel in esecuzione" : "stack kernel non in esecuzione"; break;
                default: state.Configured=Config(CiConfig,"VulnerableDriverBlocklistEnable");
                    state.Effective=e.HvciRunning || e.SMode ? "blocklist imposta da HVCI/modalità S" : "verifica in Windows; può essere imposta anche da Smart App Control"; break;
            }
            state.EnableUnavailable=Block(d,true,e); state.DisableUnavailable=Block(d,false,e);
            state.RestoreUnavailable=Block(d,false,e);
            if (CanRestore(d) && d.Id=="hvci" && snapshots[d.Id].States.Any(s=>s.ValueExists && s.Kind==RegistryValueKind.DWord && Object.Equals(s.Data,1)))
                state.RestoreUnavailable=Block(d,true,e);
            if (CanRestore(d) && d.Id=="kernel" && snapshots[d.Id].States.Any(s=>s.ValueExists && s.Kind==RegistryValueKind.DWord && Object.Equals(s.Data,1)))
                state.RestoreUnavailable=Block(d,true,e);
            if (!CanRestore(d)) state.RestoreUnavailable="Nessun valore precedente salvato in questa sessione.";
            return state;
        }
        public SecurityEnvironment Probe() { return backend.Probe(); }
        private static List<SecurityValue> Values(SecurityDefinition d,bool enable)
        {
            List<SecurityValue> values=new List<SecurityValue>();
            if (d.Id=="smartscreen")
            { values.Add(new SecurityValue(SmartPolicy,"EnableSmartScreen",enable ? 1 : 0,RegistryValueKind.DWord)); values.Add(new SecurityValue(SmartPolicy,"ShellSmartScreenLevel","Warn",RegistryValueKind.String)); }
            else if (d.Id=="store")
            { values.Add(new SecurityValue(InstallPolicy,"ConfigureAppInstallControlEnabled",1,RegistryValueKind.DWord)); values.Add(new SecurityValue(InstallPolicy,"ConfigureAppInstallControl",enable ? "StoreOnly" : "Anywhere",RegistryValueKind.String)); }
            else if (d.Id=="hvci") values.Add(new SecurityValue(Hvci,"Enabled",enable ? 1 : 0,RegistryValueKind.DWord));
            else if (d.Id=="kernel") values.Add(new SecurityValue(GuardPolicy,"ConfigureKernelShadowStacksLaunch",enable ? 1 : 3,RegistryValueKind.DWord));
            return values;
        }
        private SecuritySnapshot Capture(SecurityDefinition d,SecurityEnvironment environment)
        {
            SecuritySnapshot snapshot=new SecuritySnapshot { Pua=environment.Pua };
            foreach (SecurityValue value in Values(d,true))
            {
                snapshot.Values.Add(value); RegistryValueState state=backend.Registry.Read(value.Path,value.Name);
                if (state.Data is Array) state.Data=((Array)state.Data).Clone(); snapshot.States.Add(state);
                string path=value.Path;
                while (!String.IsNullOrEmpty(path) && !backend.Registry.Read(path,"").KeyExists)
                {
                    if (!snapshot.MissingKeys.Contains(path)) snapshot.MissingKeys.Add(path);
                    int slash=path.LastIndexOf('\\'); path=slash<0 ? "" : path.Substring(0,slash);
                }
            }
            return snapshot;
        }
        private void RestoreSnapshot(SecurityDefinition d,SecuritySnapshot snapshot)
        {
            if (d.Id=="pua") { backend.SetPua(snapshot.Pua); SecurityEnvironment actual=backend.Probe(); if (!actual.DefenderKnown || actual.Pua!=snapshot.Pua) throw new InvalidOperationException("Ripristino PUA non confermato."); return; }
            List<Exception> errors=new List<Exception>();
            for (int i=0;i<snapshot.Values.Count;i++)
            {
                try
                {
                    SecurityValue value=snapshot.Values[i]; RegistryValueState state=snapshot.States[i];
                    if (state.ValueExists) backend.Registry.Set(value.Path,value.Name,state.Data,state.Kind); else backend.Registry.DeleteValue(value.Path,value.Name);
                    RegistryValueState actual=backend.Registry.Read(value.Path,value.Name);
                    if (actual.ValueExists!=state.ValueExists || (state.ValueExists && (actual.Kind!=state.Kind || !Equal(actual.Data,state.Data))))
                        throw new InvalidOperationException("Ripristino non confermato: "+value.Name);
                }
                catch (Exception ex) { errors.Add(ex); }
            }
            foreach (string key in snapshot.MissingKeys.OrderByDescending(p=>p.Length))
                try { backend.Registry.DeleteKeyIfEmpty(key); } catch (Exception ex) { errors.Add(ex); }
            if (errors.Count>0) throw new AggregateException("Recupero parziale: "+String.Join("; ",errors.Select(x=>x.Message).ToArray()),errors);
        }
        private static bool Equal(object left,object right)
        {
            Array a=left as Array,b=right as Array;
            return a!=null && b!=null ? a.Cast<object>().SequenceEqual(b.Cast<object>()) : Object.Equals(left,right);
        }
        public void Change(SecurityDefinition d,bool enable,bool restore,Action<string> log)
        {
            SecurityEnvironment environment=Probe(); SecurityStatus status=Read(d,environment);
            string blocked=restore ? status.RestoreUnavailable : enable ? status.EnableUnavailable : status.DisableUnavailable;
            if (blocked!=null) throw new InvalidOperationException(blocked);
            SecuritySnapshot before=Capture(d,environment);
            if (!snapshots.ContainsKey(d.Id)) snapshots.Add(d.Id,before);
            try
            {
                if (restore) RestoreSnapshot(d,snapshots[d.Id]);
                else if (d.Id=="pua")
                {
                    backend.SetPua(enable ? 1 : 0); SecurityEnvironment actual=Probe();
                    if (!actual.DefenderKnown || actual.Pua!=(enable ? 1 : 0)) throw new InvalidOperationException("Modifica PUA non confermata dalla rilettura.");
                }
                else foreach (SecurityValue value in Values(d,enable))
                {
                    backend.Registry.Set(value.Path,value.Name,value.Data,value.Kind);
                    RegistryValueState actual=backend.Registry.Read(value.Path,value.Name);
                    if (!actual.ValueExists || actual.Kind!=value.Kind || !Equal(actual.Data,value.Data))
                        throw new InvalidOperationException("Modifica non confermata dalla rilettura: "+value.Name);
                }
            }
            catch
            {
                try { RestoreSnapshot(d,before); expected[d.Id]=before; log("Recuperata la configurazione precedente al tentativo."); }
                catch (Exception recovery) { log("RECUPERO INCOMPLETO: "+recovery.Message); }
                // Keep our known partial result recoverable after permissions become available again.
                try { expected[d.Id]=Capture(d,environment); }
                catch (Exception readback) { log("Configurazione dopo il recupero non leggibile: "+readback.Message); }
                if (d.Restart) pending.Add(d.Id);
                throw;
            }
            if (d.Restart) pending.Add(d.Id);
            expected[d.Id]=Capture(d,environment);
            log(restore ? "Ripristinata la configurazione originale della sessione, inclusi tipi e assenze." : "Configurazione riletta e confermata.");
            log(Read(d,Probe()).ToString());
        }
    }
}
