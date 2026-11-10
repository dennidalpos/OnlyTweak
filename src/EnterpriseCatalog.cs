using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyTweakWindows
{
    internal static class EnterpriseCatalog
    {
        private const string Learn = "https://learn.microsoft.com/en-us/";
        public static readonly Dictionary<string,string> Rsat = new Dictionary<string,string> {
            ["AD DS/LDS"]="Rsat.ActiveDirectory.DS-LDS.Tools~~~~0.0.1.0",
            ["Criteri di gruppo"]="Rsat.GroupPolicy.Management.Tools~~~~0.0.1.0",
            ["DNS"]="Rsat.Dns.Tools~~~~0.0.1.0",
            ["DFS"]="Rsat.FileServices.Tools~~~~0.0.1.0",
            ["DHCP"]="Rsat.DHCP.Tools~~~~0.0.1.0"
        };
        private static ActionDefinition Read(string id,string title,string doc,bool auth=false,bool elevated=false,int timeout=60,params string[] args) =>
            new ActionDefinition(id,title,ActionRisk.ReadOnly,elevated,auth,timeout,Learn+doc,args);
        private static ActionDefinition Change(string id,string title,ActionRisk risk,string doc,bool elevated=false,params string[] args) =>
            new ActionDefinition(id,title,risk,elevated,!elevated,0,Learn+doc,args);
        public static readonly ActionDefinition[] Actions = {
            Read("technician.inspect","Verifica postazione tecnico","windows-server/administration/install-remote-server-administration-tools"),
            Read("rsat.inspect","Inventario RSAT e sorgenti","windows/deployment/update/fod-and-lang-packs",elevated:true),
            Change("rsat.install","Installa componente RSAT",ActionRisk.LocalChange,"windows-server/administration/install-remote-server-administration-tools",true,"capability","source"),
            Read("session.authenticate","Verifica identità e dominio","powershell/module/activedirectory/get-adrootdse",true),
            Read("domain.discovery","DNS, rete e individuazione DC","troubleshoot/windows-server/active-directory/troubleshoot-domain-controller-location-issues"),
            Read("domain.auth","Orario, Kerberos e canale sicuro","powershell/module/microsoft.powershell.management/test-computersecurechannel",true),
            Read("ad.replication","Replica AD e partizioni","powershell/module/activedirectory/get-adreplicationpartnermetadata",true,timeout:180,args:new[]{"peer"}),
            Read("ad.dcdiag","DCDiag: connettività, advertising e SYSVOL","windows-server/administration/windows-commands/dcdiag",true,timeout:180),
            Read("ad.object","Confronta oggetto su due DC","powershell/module/activedirectory/get-adreplicationattributemetadata",true,args:new[]{"objectGuid","peer"}),
            Read("ad.search","Ricerca oggetti AD","powershell/module/activedirectory/get-aduser",true,args:new[]{"query","kind","page"}),
            Read("ad.userstate","Rileggi stato utente","powershell/module/activedirectory/get-aduser",true,args:new[]{"objectGuid"}),
            Read("endpoint.verify","Verifica prima/dopo intervento","powershell/module/microsoft.powershell.management/get-service",true,timeout:180,args:new[]{"operation"}),
            Read("endpoint.gpo","Risultato GPO e versioni SYSVOL","windows-server/administration/windows-commands/gpresult",true,timeout:180),
            Read("domain.sysvol","SYSVOL, NETLOGON e stato DFSR","troubleshoot/windows-server/networking/troubleshoot-missing-sysvol-and-netlogon-shares",true),
            Read("dfs.namespace","Namespace DFS e destinazioni SMB","windows-server/storage/dfs-namespaces/dfs-overview",true,args:new[]{"path"}),
            Read("dfsr.backlog","Backlog DFSR direzionale","powershell/module/dfsr/get-dfsrbacklog",true,timeout:180,args:new[]{"peer","group","folder"}),
            Read("endpoint.health","Servizi, stampa, eventi e aggiornamenti","powershell/module/cimcmdlets/get-ciminstance",true),
            Change("ad.unlock","Sblocca utente",ActionRisk.DirectoryChange,"powershell/module/activedirectory/unlock-adaccount",args:new[]{"objectGuid","expectedDn"}),
            Change("ad.enable","Abilita utente",ActionRisk.DirectoryChange,"powershell/module/activedirectory/enable-adaccount",args:new[]{"objectGuid","expectedDn"}),
            Change("ad.disable","Disabilita utente",ActionRisk.DirectoryChange,"powershell/module/activedirectory/disable-adaccount",args:new[]{"objectGuid","expectedDn"}),
            Change("ad.password","Reimposta password utente",ActionRisk.DirectoryChange,"powershell/module/activedirectory/set-adaccountpassword",args:new[]{"objectGuid","expectedDn"}),
            Change("endpoint.gpupdate","Aggiorna criteri computer",ActionRisk.EndpointChange,"windows-server/administration/windows-commands/gpupdate"),
            Change("endpoint.flushdns","Svuota cache DNS",ActionRisk.EndpointChange,"powershell/module/dnsclient/clear-dnsclientcache"),
            Change("endpoint.spooler","Riavvia spooler senza cancellare lavori",ActionRisk.EndpointChange,"powershell/module/microsoft.powershell.management/restart-service"),
            Change("endpoint.securechannel","Ripara canale del PC attuale (locale)",ActionRisk.EndpointChange,"powershell/module/microsoft.powershell.management/test-computersecurechannel",true),
            Change("endpoint.winsock","Ripristina Winsock",ActionRisk.EndpointChange,"windows-server/networking/technologies/netsh/netsh-winsock"),
            Change("endpoint.tcpip","Ripristina TCP/IP",ActionRisk.EndpointChange,"troubleshoot/windows-server/networking/reset-tcp-ip-net-shell"),
            Change("endpoint.sfc","Ripara file di sistema con SFC",ActionRisk.EndpointChange,"windows-server/administration/windows-commands/sfc"),
            Change("endpoint.dism","Ripara componenti con DISM",ActionRisk.EndpointChange,"windows-hardware/manufacture/desktop/repair-a-windows-image"),
        };
        public static ActionDefinition Get(string id) => Actions.SingleOrDefault(a=>a.Id==id) ??
            throw new ArgumentException("Azione non presente nel catalogo autorizzato.");
        public static readonly RunbookDefinition[] Runbooks = {
            new RunbookDefinition("discovery","Il PC non trova il dominio","Confronta DNS configurati, SRV e DC individuato. ICMP o una sola porta non certificano il servizio.","domain.discovery"),
            new RunbookDefinition("auth","Accesso al dominio non riuscito","Correla orario, ticket, DC e canale sicuro. I controlli locali del tecnico non descrivono un altro utente.","domain.discovery","domain.auth"),
            new RunbookDefinition("replication","Replica tra DC problematica","Leggi errori, partizioni e ultimo successo; considera pianificazione dei collegamenti e fuso orario.","session.authenticate","ad.replication"),
            new RunbookDefinition("gpo","GPO non applicata","Separa criteri computer, utente e versioni AD/SYSVOL. Il risultato computer remoto non prova le policy dell'utente.","endpoint.gpo","domain.sysvol"),
            new RunbookDefinition("sysvol","SYSVOL non disponibile","Correla accesso alle share, eventi DFSR e replica AD; assenza di eventi non prova convergenza.","domain.sysvol","ad.replication"),
            new RunbookDefinition("endpoint","Problema sulla postazione","Correla stato servizi ed eventi. Una query riuscita non certifica l'integrità complessiva.","endpoint.health"),
        };
    }
}
