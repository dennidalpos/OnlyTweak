using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;

namespace OnlyTweakWindows
{
    internal sealed record DiagnosticRequest(string Id,string Target="",int Port=443,string RecordType="A")
    {
        internal void Validate()
        {
            if(!LocalDiagnostics.Ids.Contains(Id,StringComparer.Ordinal)) throw new ArgumentException("Diagnostica non consentita.");
            if(Id is "local.ping" or "local.dns" or "local.tcp") {InputRules.Host(Target);if(System.Net.IPAddress.TryParse(Target,out _)) throw new ArgumentException("Usa un FQDN DNS, non un indirizzo IP.");}
            else if(Target!="") throw new ArgumentException("Target non previsto.");
            if(Port<1 || Port>65535) throw new ArgumentException("Porta non valida: usa 1–65535.");
            if(RecordType is not ("A" or "AAAA" or "CNAME")) throw new ArgumentException("Tipo DNS non valido.");
        }
    }
    internal static class LocalDiagnostics
    {
        internal static readonly string[] Ids={"local.ping","local.dns","local.tcp","local.connections","local.routes","local.proxy","local.identity","local.gpo"};
        internal static (string File,string[] Arguments) Native(DiagnosticRequest request)
        {
            request.Validate();
            return request.Id switch {
                "local.ping"=>("ping.exe",new[]{"-n","4","-w","1000",request.Target}),
                "local.connections"=>("netstat.exe",new[]{"-a","-n","-o"}),
                "local.routes"=>("route.exe",new[]{"print"}),
                "local.proxy"=>("netsh.exe",new[]{"winhttp","show","proxy"}),
                "local.identity"=>("whoami.exe",new[]{"/all"}),
                "local.gpo"=>("gpresult.exe",new[]{"/r","/scope","user"}),
                _=>throw new ArgumentException("Procedura nativa non prevista.")
            };
        }
        internal static ActionSpec Create(AppServices app,DiagnosticRequest request,string title)
        {
            request.Validate();
            bool powershell=request.Id is "local.dns" or "local.tcp";
            string file=powershell ? @"WindowsPowerShell\v1.0\powershell.exe" : Native(request).File;
            var action=new ActionSpec(request.Id,title,"Diagnostica dalla postazione corrente. Un test negativo non dimostra un guasto generale.",
                "Computer locale · "+EnvironmentInfo.Account+(request.Target.Length==0 ? "" : " · destinazione "+request.Target+(request.Id=="local.tcp" ? ":"+request.Port : "")),_=>{}) {
                IsDiagnostic=true,TimeoutSeconds=60,Unavailable=()=>app.Processes.FileUnavailable(file),IsApplicable=()=>app.Availability.File(file)
            };
            action.ExecuteResult=(log,token)=> {
                if(powershell) {
                    OperationRequest wire=new() {Profile=LocalPrivilege.TechnicianProfile(),ActionId=request.Id,Target=request.Target};
                    wire.Parameters["port"]=request.Port.ToString(CultureInfo.InvariantCulture);wire.Parameters["recordType"]=request.RecordType;
                    var definition=new ActionDefinition(request.Id,title,ActionRisk.ReadOnly,false,false,60,"",Array.Empty<string>());
                    StepResult result=new PowerShellBridge(embeddedName:"LocalDiagnostics.ps1").ExecuteAsync(wire,definition,token).GetAwaiter().GetResult();
                    log(result.Summary);if(result.Data!=null) log(result.Data.ToString());return result;
                }
                var native=Native(request);
                NativeObservation observation=app.Processes.RunObserved(native.File,native.Arguments,log,true,token,60);
                StepState state=observation.Truncated || String.IsNullOrWhiteSpace(observation.Output) ? StepState.Inconclusive :
                    observation.ExitCode==0 ? StepState.Success : request.Id=="local.ping" && observation.ExitCode==1 ? StepState.ObservedIssue : StepState.Inconclusive;
                return new StepResult {State=state,Truncated=observation.Truncated,Data=JsonSerializer.SerializeToElement(observation)};
            };
            return action;
        }
        internal static ActionSpec Entry(AppServices app,string id,string title,string summary)
        {
            bool parameters=id is "local.ping" or "local.dns" or "local.tcp";
            ActionSpec action=Create(app,new DiagnosticRequest(id,parameters ? "target.example.invalid" : ""),title);
            action.Description=summary;action.Summary=summary;
            if(parameters) {action.DiagnosticInput=id;action.ExecuteResult=(log,token)=>throw new InvalidOperationException("Indica prima i parametri diagnostici.");}
            return action;
        }
    }
    internal sealed class DiagnosticParametersPage : DarkPage
    {
        internal DiagnosticParametersPage(AppServices app,ActionSpec entry) : base(entry.Title,"Indica la destinazione esplicita. La diagnosi usa l'identità dell'app.",Glyphs.Diagnostic,null)
        {
            StackPanel stack=new();Content.Controls.Add(stack);
            TextBox host=new() {BackColor=Theme.Surface,ForeColor=Theme.Text,PlaceholderText="server.example.com"};
            NumericUpDown port=new() {Minimum=1,Maximum=65535,Value=443,BackColor=Theme.Surface,ForeColor=Theme.Text};
            ComboBox type=new() {DropDownStyle=ComboBoxStyle.DropDownList,BackColor=Theme.Surface,ForeColor=Theme.Text};type.Items.AddRange(new[]{"A","AAAA","CNAME"});type.SelectedIndex=0;
            stack.Controls.Add(Theme.Label("Destinazione FQDN",Theme.Muted));stack.Controls.Add(host);
            if(entry.Id=="local.tcp") {stack.Controls.Add(Theme.Label("Porta TCP",Theme.Muted));stack.Controls.Add(port);}
            if(entry.Id=="local.dns") {stack.Controls.Add(Theme.Label("Tipo record DNS",Theme.Muted));stack.Controls.Add(type);}
            Button run=Theme.Button("Avvia diagnosi",async delegate {
                try {var request=new DiagnosticRequest(entry.Id,host.Text.Trim(),(int)port.Value,(string)type.SelectedItem);
                    await OperationDialog.Open(this,app,LocalDiagnostics.Create(app,request,entry.Title));}
                catch(ArgumentException ex) {SetFeedback(ex.Message);}
            });Footer.Controls.Add(run);AcceptButton=run;
        }
    }
}
