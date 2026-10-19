using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace OnlyTweakWindows
{
    internal sealed class SecurityDefinition
    {
        public readonly string Id,Title,Description,Page;
        public string Summary;
        public SecurityDefinition(string id,string title,string description,string page)
        { Id=id; Title=title; Description=description; Page=page; }
    }
    internal sealed class SecurityEnvironment
    {
        public bool DeviceGuardKnown,HvciRunning,KernelRunning,DefenderKnown,DefenderActive,SMode;
        public int Pua=-1;
    }
    internal enum ProtectionState { Unknown,Active,Inactive,Audit }
    internal sealed class SecurityStatus
    {
        public string Configured,Effective;
        public bool ReadFailed;
        // Solo evidenze runtime: una configurazione di registro non certifica l'effetto.
        public ProtectionState Protection;
        public override string ToString()
        { return "Configurazione: "+Configured+"\nEffettivo: "+Effective; }
    }
    internal interface ISecurityBackend
    {
        IRegistryReader Registry { get; }
        SecurityEnvironment Probe();
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
        public readonly SecurityDefinition[] Definitions={
            new SecurityDefinition("sac","Controllo intelligente delle app","Smart App Control può bloccare app non firmate. Modifica nella pagina Windows; la riattivazione dipende da aggiornamenti e disponibilità del dispositivo.","windowsdefender://smartapp/") { Summary="Blocca le app non firmate; si gestisce in Windows." },
            new SecurityDefinition("smartscreen","SmartScreen · Controlla app e file","Controlla la reputazione degli eseguibili. Stato dei criteri locali, distinto dai filtri del browser; modifica in Sicurezza Windows.","windowsdefender://smartscreenpua/") { Summary="Reputazione degli eseguibili; gestione in Windows." },
            new SecurityDefinition("pua","App potenzialmente indesiderate","Protezione PUA di Microsoft Defender Antivirus. Il filtro dei download del browser si gestisce separatamente in Windows.","windowsdefender://smartscreenpua/") { Summary="Protezione PUA di Microsoft Defender." },
            new SecurityDefinition("store","Origine delle applicazioni","Scegli se consentire app da qualsiasi origine o limitarle al Microsoft Store. In Windows: App > Impostazioni avanzate delle app > Scegli dove scaricare le app. Non modifica la modalità S.","ms-settings:appsfeatures") { Summary="App da qualsiasi origine o solo dal Microsoft Store." },
            new SecurityDefinition("hvci","Integrità della memoria","Protezione HVCI nel gruppo Isolamento core. Disattivandola possono cessare anche le protezioni che la richiedono; nessun altro interruttore viene modificato.","windowsdefender://coreisolation/") { Summary="HVCI, nel gruppo Isolamento core." },
            new SecurityDefinition("kernel","Protezione stack kernel","Protegge lo stack in modalità kernel. Richiede hardware compatibile, VBS e integrità della memoria; disponibilità e modifiche si verificano in Windows.","windowsdefender://coreisolation/") { Summary="Richiede hardware compatibile, VBS e HVCI." },
            new SecurityDefinition("drivers","Blocklist driver vulnerabili","L’interruttore dedicato dipende dalla versione Windows. Può essere imposta da HVCI, Smart App Control o modalità S anche se il valore locale è disattivato.","windowsdefender://coreisolation/") { Summary="Stato e pagina Isolamento core." }
        };
        public SecurityService(ISecurityBackend backend) { this.backend=backend; }
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
        public SecurityStatus Read(SecurityDefinition d,SecurityEnvironment e)
        {
            SecurityStatus state=new SecurityStatus { Effective="non verificabile dalla sola configurazione" };
            switch (d.Id)
            {
                case "sac":
                    int sac=Number(CiPolicy,"VerifiedAndReputablePolicyState",-1);
                    state.Configured=sac==0 ? "disattivato" : sac==1 ? "attivato" : sac==2 ? "valutazione" : "sconosciuta"; break;
                case "smartscreen": state.Configured=Config(SmartPolicy,"EnableSmartScreen")+"; livello: "+Config(SmartPolicy,"ShellSmartScreenLevel"); break;
                case "store": state.Configured=Config(InstallPolicy,"ConfigureAppInstallControlEnabled")+"; origine: "+Config(InstallPolicy,"ConfigureAppInstallControl"); break;
                case "pua": state.Configured=e.DefenderKnown ? e.Pua==0 ? "disattivata" : e.Pua==1 ? "blocco attivo" : e.Pua==2 ? "Audit" : "sconosciuta" : "sconosciuta";
                    state.Effective=!e.DefenderKnown ? "non leggibile" : !e.DefenderActive ? "Defender non attivo" : state.Configured;
                    state.Protection=!e.DefenderKnown ? ProtectionState.Unknown : !e.DefenderActive ? ProtectionState.Inactive :
                        e.Pua==1 ? ProtectionState.Active : e.Pua==0 ? ProtectionState.Inactive : e.Pua==2 ? ProtectionState.Audit : ProtectionState.Unknown; break;
                case "hvci": state.Configured=Config(Hvci,"Enabled"); state.Effective=!e.DeviceGuardKnown ? "sconosciuto" : e.HvciRunning ? "HVCI in esecuzione" : "HVCI non in esecuzione";
                    state.Protection=!e.DeviceGuardKnown ? ProtectionState.Unknown : e.HvciRunning ? ProtectionState.Active : ProtectionState.Inactive; break;
                case "kernel": state.Configured=Config(GuardPolicy,"ConfigureKernelShadowStacksLaunch"); state.Effective=!e.DeviceGuardKnown ? "sconosciuto" : e.KernelRunning ? "stack kernel in esecuzione" : "stack kernel non in esecuzione";
                    state.Protection=!e.DeviceGuardKnown ? ProtectionState.Unknown : e.KernelRunning ? ProtectionState.Active : ProtectionState.Inactive; break;
                default: state.Configured=Config(CiConfig,"VulnerableDriverBlocklistEnable");
                    state.Effective=e.HvciRunning || e.SMode ? "blocklist imposta da HVCI/modalità S" : "verifica in Windows; può essere imposta anche da Smart App Control"; break;
            }
            return state;
        }
        public IEnumerable<SecurityDefinition> AvailableDefinitions(FeatureAvailability availability)
        { return Definitions.Where(d=>availability.Security(d.Id)); }
        public SecurityEnvironment Probe() { return backend.Probe(); }
    }
}
