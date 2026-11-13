using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace OnlyTweakWindows
{
    internal static class LocalRemoting
    {
        internal static LocalInterface[] Interfaces()=>NetworkInterface.GetAllNetworkInterfaces()
            .Where(n=>n.OperationalStatus==OperationalStatus.Up && n.NetworkInterfaceType!=NetworkInterfaceType.Loopback)
            .Where(n=>n.GetIPProperties().GetIPv4Properties()!=null && n.GetIPProperties().UnicastAddresses.Any(a=>a.Address.AddressFamily==AddressFamily.InterNetwork))
            .Select(n=>new LocalInterface(n.GetIPProperties().GetIPv4Properties().Index,n.Name)).ToArray();
        internal static int InterfaceIndex(string id) {
            if(id is "winrm.inspect" or "winrm.undo") return 0;
            var match=Regex.Match(id??"",@"^winrm\.enable\.([1-9][0-9]{0,8})$");
            if(!match.Success || !Int32.TryParse(match.Groups[1].Value,out int index)) throw new ArgumentException("Azione WinRM non consentita.");
            return index;
        }
        internal static StepResult Run(string id,CancellationToken cancellation=default) {
            InterfaceIndex(id);
            var request=new OperationRequest {ActionId=id,Target=Environment.MachineName,Confirmed=id!="winrm.inspect"};
            var definition=new ActionDefinition(id,"WinRM locale",id=="winrm.inspect" ? ActionRisk.ReadOnly : ActionRisk.LocalChange,true,60);
            return new PowerShellBridge(embeddedName:"LocalRemoting.ps1").ExecuteAsync(request,definition,cancellation).GetAwaiter().GetResult();
        }
        internal static ActionSpec Action(string id) {
            int index=InterfaceIndex(id);bool inspect=id=="winrm.inspect";bool undo=id=="winrm.undo";
            string title=inspect ? "Verifica WinRM locale" : undo ? "Ripristina precedente" : "Abilita sulla LAN privata";
            var action=new ActionSpec(id,title,"Ricezione PowerShell sul PC locale. Nessuna connessione verso altri PC.","Computer locale",_=>{}) {
                RequiresElevation=true,IsDiagnostic=inspect,
                Confirmation=inspect ? null : undo ? "Ripristinare soltanto servizio, listener, endpoint e regole modificati dall'app? Modifiche esterne bloccano il ripristino." :
                    "Interfaccia: "+index+". Si accettano solo rete privata e un indirizzo IPv4.\nEndpoint OnlyTweak.Local riservato agli amministratori, TCP 5985 limitato alla sottorete privata.\nIl servizio WinRM sarà avviato e riavviato; configurazioni preesistenti in conflitto bloccano l'operazione.",
                ExecuteResult=(log,token)=> {var result=Run(id,token);log(result.Summary);return result;}
            };
            action.Observe=(_,token)=> {
                StepResult read=Run("winrm.inspect",token);
                bool complete=read.State==StepState.Success && read.Data is JsonElement;
                bool? satisfied=null;
                if(complete && read.Data is JsonElement data) satisfied=inspect ||
                    data.TryGetProperty(undo ? "Restored" : "Configured",out var value) && value.ValueKind==JsonValueKind.True;
                return new LocalObservation(complete,satisfied,read.Data);
            };return action;
        }
    }
    internal sealed record LocalInterface(int Index,string Name) {public override string ToString()=>Name;}
    internal sealed class RemotingPage : DarkPage
    {
        private readonly AppServices app;
        private readonly Label state;
        internal override void ResumePage() {
            if(app.LastResult is StepResult result && result.ActionId.StartsWith("winrm.",StringComparison.Ordinal))
                state.Text=result.State+" · "+result.Summary;
        }
        internal RemotingPage(AppServices app) : base("PowerShell Remoting / WinRM","Abilita la ricezione sul PC locale, soltanto sulla LAN privata selezionata.",Glyphs.Network,null) {
            this.app=app;
            var stack=new StackPanel();Content.Controls.Add(stack);
            stack.Controls.Add(Theme.Label("Avanzata · richiede amministratore · Windows PowerShell 5.1\nFonte: Microsoft WSMan e Register-PSSessionConfiguration.\nNon configura altri PC, credenziali, TrustedHosts o autenticazione meno restrittiva.",Theme.Muted));
            stack.Controls.Add(Theme.Label("Endpoint: OnlyTweak.Local · porta TCP 5985 · amministratori locali\nLa connessione da un altro PC domestico richiede configurazione e collaudo separati sul client.",Theme.Muted));
            var interfaces=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat,BackColor=Theme.Surface,ForeColor=Theme.Text,AccessibleName="Interfaccia LAN privata"};
            try {
                interfaces.Items.AddRange(app.RemotingInterfaces());
                if(interfaces.Items.Count>0) interfaces.SelectedIndex=0;
            } catch(Exception ex) {stack.Controls.Add(Theme.Label("Interfacce non leggibili: "+ex.Message,Theme.Warning));}
            stack.Controls.Add(Theme.Label("Scegli l'interfaccia. Il worker verifica che sia privata e abbia un solo IPv4 idoneo.",Theme.Muted));stack.Controls.Add(interfaces);
            state=Theme.Label("Da verificare. Nessuna modifica applicata all'apertura di questa pagina.",Theme.Muted);stack.Controls.Add(state);
            foreach(string mode in new[]{"inspect","enable","undo"}) {
                string selected=mode;
                stack.Controls.Add(Theme.Button(mode=="inspect" ? "Verifica" : mode=="undo" ? "Ripristina precedente" : "Abilita sulla LAN privata",async delegate {
                    try {
                        string id="winrm."+selected;
                        if(selected=="enable") {
                            if(interfaces.SelectedItem is not LocalInterface nic) throw new InvalidOperationException("Seleziona un'interfaccia disponibile.");
                            id+="."+nic.Index;
                        }
                        await OperationDialog.Open(this,app,LocalRemoting.Action(id));
                        state.Text="Consulta il risultato della verifica o dell'operazione. L'apertura della pagina non certifica la connessione remota.";
                    } catch(Exception ex) {SetFeedback(ex.Message);}
                }));
            }
        }
    }
}
