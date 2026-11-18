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
            .Select(Describe).Where(i=>i!=null).ToArray();
        // Ogni scheda è letta separatamente: filtri e binding senza IPv4 (errore 10043) non devono azzerare l'elenco.
        internal static LocalInterface Describe(NetworkInterface adapter) {
            try {
                var properties=adapter.GetIPProperties();
                var ipv4=properties.GetIPv4Properties();
                if(ipv4==null || !properties.UnicastAddresses.Any(a=>a.Address.AddressFamily==AddressFamily.InterNetwork)) return null;
                return new LocalInterface(ipv4.Index,adapter.Name);
            } catch(NetworkInformationException) { return null; }
        }
        internal static int InterfaceIndex(string id) {
            if(id is "winrm.inspect" or "winrm.undo") return 0;
            var match=Regex.Match(id??"",@"^winrm\.enable\.([1-9][0-9]{0,8})(?:\?([^?]+))?$");
            if(!match.Success || !Int32.TryParse(match.Groups[1].Value,out int index)) throw new ArgumentException("Azione WinRM non consentita.");
            if(match.Groups[2].Success) try {RemotingSettings.FromQuery(match.Groups[2].Value);} catch(ArgumentException ex) {throw new ArgumentException("Azione WinRM non consentita: "+ex.Message);}
            return index;
        }
        // Identificativo dell'azione senza il suffisso dei parametri, come lo riporta il risultato del worker.
        internal static string BaseId(string id) { int cut=(id??"").IndexOf('?'); return cut<0 ? id : id.Substring(0,cut); }
        internal static string EnableId(int index,RemotingSettings settings) { string query=settings.Validated().Query(); return "winrm.enable."+index+(query.Length==0 ? "" : "?"+query); }
        internal static RemotingSettings SettingsOf(string id) { int cut=(id??"").IndexOf('?'); return cut<0 ? new RemotingSettings() : RemotingSettings.FromQuery(id.Substring(cut+1)); }
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
        internal RemotingPage(AppServices app) : base("PowerShell Remoting / WinRM","Abilita la ricezione sul PC locale, soltanto sulla LAN privata selezionata.",Glyphs.Network,null,AppArea.Advanced) {
            this.app=app;
            var stack=new StackPanel();Content.Controls.Add(stack);
            stack.Controls.Add(Theme.Label("Avanzata · richiede amministratore · Windows PowerShell 5.1\nFonte: Microsoft WSMan e Register-PSSessionConfiguration.\nNon configura altri PC, credenziali, TrustedHosts o autenticazione meno restrittiva.",Theme.Muted));
            stack.Controls.Add(Theme.Label("Predefiniti: endpoint OnlyTweak.Local · porta TCP 5985 · amministratori locali · sottorete privata. Puoi modificarli sotto.\nLa connessione da un altro PC domestico richiede configurazione e collaudo separati sul client.",Theme.Muted));
            var interfaces=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat,BackColor=Theme.Surface,ForeColor=Theme.Text,AccessibleName="Interfaccia LAN privata"};
            try {
                interfaces.Items.AddRange(app.RemotingInterfaces());
                if(interfaces.Items.Count>0) interfaces.SelectedIndex=0;
                else stack.Controls.Add(Theme.Label("Nessuna scheda attiva con un indirizzo IPv4 trovata. Collega la LAN e riapri la pagina.",Theme.Warning));
            } catch(Exception ex) {stack.Controls.Add(Theme.Label("Interfacce non leggibili: "+ex.Message,Theme.Warning));}
            stack.Controls.Add(Theme.Label("Scegli l'interfaccia. Il worker verifica che sia privata e abbia un solo IPv4 idoneo.",Theme.Muted));stack.Controls.Add(interfaces);
            // Impostazioni modificabili: valori validati, salvati per l'utente e passati al worker come suffisso a grammatica chiusa.
            var saved=app.Settings.Remoting();
            if(app.Settings.LoadError!=null) stack.Controls.Add(Theme.Label(app.Settings.LoadError,Theme.Warning));
            stack.Controls.Add(Theme.Section("Impostazioni dell'abilitazione",Glyphs.Settings));
            var port=new NumericUpDown {Minimum=1024,Maximum=65535,Value=saved.Port,BackColor=Theme.Surface,ForeColor=Theme.Text,AccessibleName="Porta TCP"};
            var remote=new TextBox {BackColor=Theme.Surface,ForeColor=Theme.Text,PlaceholderText="Vuoto = sottorete privata della scheda · es. 192.168.1.20, 192.168.1.0/24",Text=String.Join(", ",saved.Remote),AccessibleName="Indirizzi remoti ammessi"};
            var who=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat,BackColor=Theme.Surface,ForeColor=Theme.Text,AccessibleName="Utenti ammessi"};
            who.Items.AddRange(new object[]{"Amministratori locali","Amministratori locali e Remote Management Users"});who.SelectedIndex=saved.Who=="rmu" ? 1 : 0;
            var endpoint=new TextBox {BackColor=Theme.Surface,ForeColor=Theme.Text,Text=saved.EndpointName,AccessibleName="Nome endpoint"};
            var idle=new NumericUpDown {Minimum=0,Maximum=1440,Increment=5,Value=saved.IdleMinutes,BackColor=Theme.Surface,ForeColor=Theme.Text,AccessibleName="Timeout di inattività in minuti"};
            RemotingSettings Collect() => new RemotingSettings {Port=(int)port.Value,Remote=RemotingSettings.ParseRemote(remote.Text),Who=who.SelectedIndex==1 ? "rmu" : "admins",EndpointName=endpoint.Text.Trim(),IdleMinutes=(int)idle.Value}.Validated();
            foreach(var (caption,control) in new (string,Control)[]{("Porta TCP (1024-65535)",port),("Indirizzi remoti ammessi (solo IPv4 privati)",remote),("Utenti ammessi",who),("Nome endpoint",endpoint),("Timeout di inattività in minuti (0 = predefinito di Windows, altrimenti 5-1440)",idle)}) {
                stack.Controls.Add(Theme.Label(caption,Theme.Muted));stack.Controls.Add(control);
            }
            stack.Controls.Add(Theme.Button("Ripristina valori predefiniti",delegate {
                var d=new RemotingSettings();port.Value=d.Port;remote.Text="";who.SelectedIndex=0;endpoint.Text=d.EndpointName;idle.Value=d.IdleMinutes;
            }));
            state=Theme.Label("Da verificare. Nessuna modifica applicata all'apertura di questa pagina.",Theme.Muted);stack.Controls.Add(state);
            foreach(string mode in new[]{"inspect","enable","undo"}) {
                string selected=mode;
                stack.Controls.Add(Theme.Button(mode=="inspect" ? "Verifica" : mode=="undo" ? "Ripristina precedente" : "Abilita sulla LAN privata",async delegate {
                    try {
                        string id="winrm."+selected;
                        if(selected=="enable") {
                            if(interfaces.SelectedItem is not LocalInterface nic) throw new InvalidOperationException("Seleziona un'interfaccia disponibile.");
                            var settings=Collect();
                            id=LocalRemoting.EnableId(nic.Index,settings);
                            app.Settings.Save(settings);
                        }
                        await OperationDialog.Open(this,app,LocalRemoting.Action(id));
                        state.Text="Consulta il risultato della verifica o dell'operazione. L'apertura della pagina non certifica la connessione remota.";
                    } catch(Exception ex) {SetFeedback(ex.Message);}
                }));
            }
        }
    }
}
