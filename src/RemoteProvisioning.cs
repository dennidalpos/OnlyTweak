using System;
using System.IO;
using System.Text;

namespace OnlyTweakWindows
{
    internal static class RemoteProvisioning
    {
        public static void Export(string parent)
        {
            string folder=Path.Combine(Path.GetFullPath(parent),"OnlyTweak-Remoting-"+Guid.NewGuid().ToString("N"));
            SecureFiles.EnsureDirectory(folder);
            foreach (string file in new[] {"Provision-Remoting.ps1","OnlyTweakEndpoint.psm1"})
                EmbeddedScripts.WriteTo(file,Path.Combine(folder,file));
            File.WriteAllText(Path.Combine(folder,"README.txt"),Instructions,new UTF8Encoding(true));
        }
        internal const string Instructions=@"OnlyTweak - predisposizione remota (da revisionare e firmare)

1. Usare solo membri Windows 10/11 in laboratorio; i DC sono esclusi.
2. Firmare gli script con il certificato aziendale e distribuire tramite canale amministrativo esistente.
3. Ispezione: .\Provision-Remoting.ps1 -Mode Inspect
4. Simulazione: .\Provision-Remoting.ps1 -Mode Apply -ManagementAddresses 10.20.30.0/24 -AllowedGroupSid S-1-5-21-111-222-333-4567 -WhatIf
5. Applicazione: stesso comando senza -WhatIf, dopo revisione della conferma. Il gruppo deve essere dedicato e delegato.
6. Opzionale HTTPS: aggiungere -UseHttps -CertificateThumbprint <certificato macchina valido per Server Authentication e nome host>.
7. Verificare da una postazione autorizzata e da una esclusa dal firewall; provare identità delegate e comandi negati.
8. Ripristino: .\Provision-Remoting.ps1 -Mode Undo -WhatIf; poi eseguire senza WhatIf dopo revisione.
9. Dopo Undo verificato si puo eseguire una nuova Apply: il checkpoint precedente viene conservato e verificato in History. Undo ripetuto non modifica nulla.

Lo script crea un solo endpoint JEA OnlyTweak, una regola firewall Domain limitata alle sorgenti indicate e, se assente, un listener.
Lo stato precedente resta in ProgramData\OnlyTweak\Remoting, protetto per Administrators/SYSTEM. Non riutilizzare snapshot di altre macchine.
Un conflitto o una configurazione GPO impedisce applicazione/ripristino automatico: non sovrascrivere policy o impostazioni successive.
Listener preesistenti devono corrispondere a trasporto, indirizzo, porta, stato, prefisso e, per HTTPS, certificato e FQDN richiesti. Inspect espone la compatibilita; WhatIf non crea checkpoint.
Checkpoint attivi, Partial, alterati, legacy senza SchemaVersion 2 o di altra macchina richiedono revisione/recupero manuale. Non cancellare state.json o trascrizioni per aggirare un conflitto.
Non disabilita regole preesistenti: eventuali esposizioni preesistenti devono essere valutate separatamente.
Il modulo JEA copre evidenze endpoint e azioni selezionate; replica AD/DFSR usa RSAT direttamente dal tecnico.
Non concedere accesso a ruoli Tier 0 su normali PC di assistenza; utilizzare PAW e identità separate.

Distribuzione GPO:
- Prima scegliere OU pilota e gruppo dedicato. Salvare backup della GPO esistente.
- Per una distribuzione gestita usare GPO computer e script startup firmato con parametri espliciti.
- In alternativa configurare tramite GPO il servizio WinRM, i listener e il firewall Domain con RemoteAddress delle sole reti amministrative.
- Indirizzi filtro listener = indirizzi locali di ascolto; non sostituiscono la restrizione delle sorgenti nel firewall.
- Distribuire modulo e configurazione JEA con il canale aziendale. Nessun collegamento automatico di GPO dalla UI.
- Dopo applicazione GPO verificare criterio risultante e accesso effettivo; rollback dalla stessa fonte di gestione.

Fonti ufficiali:
https://learn.microsoft.com/en-us/powershell/scripting/security/remoting/winrm-security
https://learn.microsoft.com/en-us/powershell/scripting/security/remoting/jea/overview
https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-remotemanagement
";
    }
}
