using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OnlyTweakWindows
{
    internal sealed class EnterpriseDialog : DarkPage
    {
        private readonly ComboBox profiles=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=220,DisplayMember="Customer"};
        private readonly ComboBox targets=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=260};
        private readonly Label identity=Theme.Label("Nessuna sessione di rete. Seleziona un profilo e connetti.",Theme.Muted);
        private readonly WorkspaceSections tabs=new WorkspaceSections {Dock=DockStyle.Fill};
        private Panel workspace;
        private StackPanel index;
        private bool changing;
        private readonly DarkOutput output=Theme.Output();
        private readonly FlowLayoutPanel profileBar=Bar();
        private readonly List<EnvironmentProfile> environments=new List<EnvironmentProfile>();
        private readonly List<StepResult> results=new List<StepResult>();
        private readonly BoundedText text=new BoundedText();
        private readonly Button cancel;
        private readonly DataGridView directory=new DataGridView {Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,
            AllowUserToDeleteRows=false,MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill};
        private CustomerSession session;
        private CancellationTokenSource cancellation;
        private EnvironmentProfile reportProfile;
        private bool busy;
        private int page;
        private readonly TextBox search=Field();
        private readonly ComboBox kind=Choice("user","group","computer");
        private readonly ComboBox peer=Choice();
        private EnvironmentProfile Selected => profiles.SelectedItem as EnvironmentProfile;
        private string Target => targets.SelectedItem as string ?? throw new InvalidOperationException("Seleziona una destinazione.");
        internal EnterpriseDialog(bool technician=false,bool loadProfiles=true) : base("Ambienti aziendali","Cliente, identità e destinazione espliciti per ogni operazione.",Glyphs.Network,null)
        {
            ClientSize=new Size(1120,880);
            StyleChoice(profiles); StyleChoice(targets);
            directory.EnableHeadersVisualStyles=false;
            directory.ColumnHeadersDefaultCellStyle.BackColor=Theme.Surface;
            directory.ColumnHeadersDefaultCellStyle.ForeColor=Theme.Text;
            TableLayoutPanel layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=4};
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,310)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,240));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            layout.Dock=DockStyle.Top; layout.AutoSize=true;
            profileBar.Controls.Add(profiles);
            profileBar.Controls.Add(Theme.Button("Nuovo",async delegate { await EditProfile(true); }));
            profileBar.Controls.Add(Theme.Button("Modifica",async delegate { await EditProfile(false); }));
            profileBar.Controls.Add(Theme.Button("Elimina",async delegate { await DeleteProfile(); }));
            profileBar.Controls.Add(Theme.Button("Identità corrente",async delegate { await Connect(false); }));
            profileBar.Controls.Add(Theme.Button("Credenziali sessione…",async delegate { await Connect(true); }));
            profileBar.Controls.Add(Theme.Button("Disconnetti",delegate { Disconnect(); }));
            FlowLayoutPanel context=Bar(); context.Controls.Add(Theme.Label("Destinazione",Theme.Muted)); context.Controls.Add(targets); context.Controls.Add(identity);
            layout.Controls.Add(profileBar,0,0); layout.Controls.Add(context,0,1); layout.Controls.Add(tabs,0,2); layout.Controls.Add(output,0,3);
            Panel viewport=new Panel {Dock=DockStyle.Fill,AutoScroll=true}; workspace=viewport;
            viewport.Controls.Add(layout); Content.Controls.Add(viewport);
            BuildProcedures(); BuildTechnician(); BuildDirectory(); BuildDfs(); BuildEndpoint(); BuildProvisioning();
            foreach(Panel tab in tabs.TabPages) {
                Control[] controls=tab.Controls.Cast<Control>().OrderBy(c=>c is FlowLayoutPanel ? 0 : c is Label ? 2 : 1).ToArray();
                tab.Controls.Clear();
                TableLayoutPanel page=new TableLayoutPanel {Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,RowCount=controls.Length};
                page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
                for(int i=0;i<controls.Length;i++) {
                    Control child=controls[i]; child.Dock=DockStyle.Fill;
                    if(child is ListBox || child is DataGridView) page.RowStyles.Add(new RowStyle(SizeType.Absolute,220));
                    else page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    page.Controls.Add(child,0,i);
                }
                tab.AutoScroll=true; tab.Controls.Add(page);
            }
            if (technician) tabs.SelectedIndex=1;
            cancel=Theme.Button("Annulla diagnosi",delegate { cancellation?.Cancel(); }); cancel.Enabled=false; Footer.Controls.Add(cancel);
            Footer.Controls.Add(Theme.Button("Esporta JSON",async delegate { await Export(false); }));
            Footer.Controls.Add(Theme.Button("Esporta HTML",async delegate { await Export(true); }));
            profiles.SelectedIndexChanged+=delegate { Disconnect(); RefreshTargets(); };
            targets.SelectedIndexChanged+=delegate { page=0; directory.Rows.Clear(); directory.ClearSelection(); };
            try { if (loadProfiles) environments.AddRange(ProfileStore.Load()); RefreshProfiles(null); }
            catch (Exception ex) { SetFeedback("Archivio profili non caricato: "+ex.Message); }
            ShowIndex(technician);
        }
        protected override void SetFeedback(string text)
        {
            base.SetFeedback(text);
            if(Navigation?.Current is EnterpriseSectionPage section && section.WorkspaceOwner==this) section.ShowWorkspaceFeedback(text);
        }
        internal IEnumerable<Panel> Sections => tabs.TabPages;
        internal void ShowIndex(bool technician=false)
        {
            if(index!=null) return;
            Content.Controls.Clear();
            index=new StackPanel();Content.Controls.Add(index);
            index.Controls.Add(Theme.Label("Seleziona una sezione. Cliente, sessione e risultati vengono conservati tornando indietro.",Theme.Muted));
            foreach(Panel section in tabs.TabPages) {
                Panel selected=section;
                index.Controls.Add(Theme.Card(section.Text,"Apri nella finestra corrente",delegate { OpenSection(selected); },Glyphs.Network));
            }
        }
        internal void OpenSection(Panel section)
        {
            tabs.SelectedIndex=tabs.TabPages.IndexOf(section);
            Navigation.Push(new EnterpriseSectionPage(this,workspace,section.Text));
        }
        internal override async Task<bool> CanLeaveAsync(bool closing)
        {
            if(!busy) return true;
            if(changing) { SetFeedback("Attendi l'esito della modifica prima di uscire.");return false; }
            cancellation?.Cancel();
            while(busy) await Task.Delay(20);
            return true;
        }
        internal IEnumerable<Control> WorkspaceFooter => Footer.Controls.Cast<Control>().Where(c=>c!=CloseAction).ToArray();
        internal void RestoreFooter(Control control) {Footer.Controls.Add(control);}
        private static FlowLayoutPanel Bar() => new FlowLayoutPanel {AutoSize=true,Dock=DockStyle.Fill,WrapContents=true,Margin=new Padding(3)};
        private static TextBox Field() => new TextBox {Width=220,BackColor=Theme.Surface,ForeColor=Theme.Text};
        private static ComboBox Choice(params string[] items)
        { ComboBox box=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=220}; StyleChoice(box); box.Items.AddRange(items); if (items.Length>0) box.SelectedIndex=0; return box; }
        private static void StyleChoice(ComboBox box)
        {
            box.BackColor=Theme.Surface; box.ForeColor=Theme.Text; box.DrawMode=DrawMode.OwnerDrawFixed; box.ItemHeight=26;
            box.DrawItem+=delegate(object sender,DrawItemEventArgs e) {
                using (SolidBrush brush=new SolidBrush((e.State&DrawItemState.Selected)!=0 ? Theme.Background : Theme.Surface)) e.Graphics.FillRectangle(brush,e.Bounds);
                object item=e.Index>=0 ? box.Items[e.Index] : null;
                string label=item is EnvironmentProfile p ? p.Customer : box.GetItemText(item);
                TextRenderer.DrawText(e.Graphics,label,e.Font,e.Bounds,Theme.Text,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                e.DrawFocusRectangle();
            };
        }
        private Panel Tab(string name)
        { Panel tab=new Panel {Text=name,BackColor=Theme.Background,ForeColor=Theme.Text,Padding=new Padding(8)}; tabs.TabPages.Add(tab); return tab; }
        private void BuildProcedures()
        {
            Panel tab=Tab("Problemi comuni");
            ListBox list=new ListBox {Dock=DockStyle.Fill,BackColor=Theme.Surface,ForeColor=Theme.Text};
            list.Items.AddRange(EnterpriseCatalog.Runbooks); list.SelectedIndex=0;
            FlowLayoutPanel buttons=Bar(); buttons.Dock=DockStyle.Bottom;
            buttons.Controls.Add(Theme.Button("Avvia diagnosi",async delegate {
                RunbookDefinition book=(RunbookDefinition)list.SelectedItem;
                await Work(false,async ()=> {
                    RequireSession();
                    Append(book.Title+"\r\n"+book.Interpretation);
                    foreach (string step in book.Steps) {
                        cancellation.Token.ThrowIfCancellationRequested();
                        StepResult result=await Execute(step);
                        if (result.State==StepState.Cancelled || (step=="session.authenticate" && !result.AuthenticationVerified)) break;
                    }
                });
            }));
            buttons.Controls.Add(Theme.Button("DCDiag selettivo",async delegate { await Run("ad.dcdiag"); }));
            tab.Controls.Add(list); tab.Controls.Add(buttons);
        }
        private void BuildTechnician()
        {
            Panel tab=Tab("Tecnico e RSAT"); FlowLayoutPanel bar=Bar(); bar.Dock=DockStyle.Top;
            ComboBox capability=Choice(EnterpriseCatalog.Rsat.Keys.ToArray()); TextBox source=Field(); source.PlaceholderText="Sorgente FoD locale, opzionale";
            bar.Controls.Add(Theme.Button("Verifica prerequisiti",async delegate { await RunLocal("technician.inspect",false); }));
            bar.Controls.Add(Theme.Button("Inventario RSAT",async delegate { await RunLocal("rsat.inspect",true); }));
            bar.Controls.Add(capability); bar.Controls.Add(source);
            bar.Controls.Add(Theme.Button("Installa selezionato…",async delegate {
                await RunLocal("rsat.install",true,new Dictionary<string,string> {["capability"]=(string)capability.SelectedItem,["source"]=source.Text.Trim()});
            }));
            FlowLayoutPanel consoles=Bar(); consoles.Dock=DockStyle.Top;
            foreach (var entry in new Dictionary<string,string> {["Utenti e computer AD"]="dsa.msc",["Siti e servizi"]="dssite.msc",
                ["GPO"]="gpmc.msc",["DNS"]="dnsmgmt.msc",["DFS"]="dfsmgmt.msc",["DHCP"]="dhcpmgmt.msc"})
            {
                string file=entry.Value;
                consoles.Controls.Add(Theme.Button(entry.Key,async delegate {
                    try {
                        if (session==null) throw new InvalidOperationException("Connetti prima una sessione cliente. Le console usano l'identità corrente di Windows: per credenziali alternative usa una sessione amministrativa Windows dedicata.");
                        if (!identity.Text.Contains("rete dichiarata: "+EnvironmentInfo.Account,StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("La console esterna non eredita le credenziali del worker. Aprila da una sessione Windows dedicata al cliente.");
                        if (!await ConfirmDialog.Ask(this,"Apri console Microsoft","La console userà l'identità Windows corrente e manterrà il proprio contesto. Seleziona e verifica il dominio nella console.")) return;
                        new ProcessService().LaunchSystem("mmc.exe",ProcessService.QuoteArgument(new ProcessService().SystemFile(file)),Append);
                    } catch (Exception ex) { SetFeedback(ex.Message); }
                }));
            }
            Label note=Theme.Label("Installazione selettiva con UAC. Le policy WSUS/FoD restano invariate. Nessun riavvio automatico.\nLa compatibilità operativa Windows 10 non equivale a supporto del produttore: verifica LTSC/ESU ed edizione.",Theme.Muted);
            note.Dock=DockStyle.Bottom; tab.Controls.Add(bar); tab.Controls.Add(consoles); tab.Controls.Add(note);
        }
        private void BuildDirectory()
        {
            Panel tab=Tab("Active Directory"); FlowLayoutPanel bar=Bar(); bar.Dock=DockStyle.Top;
            search.PlaceholderText="Nome o account, almeno 2 caratteri";
            bar.Controls.Add(kind); bar.Controls.Add(search);
            bar.Controls.Add(Theme.Button("Cerca",async delegate { page=0; await SearchDirectory(); }));
            bar.Controls.Add(Theme.Button("Pagina successiva",async delegate { page=Math.Min(19,page+1); await SearchDirectory(); }));
            bar.Controls.Add(peer);
            bar.Controls.Add(Theme.Button("Confronta sui DC",async delegate {
                DirectorySelection item=Selection();
                if (item!=null) await Run("ad.object",new Dictionary<string,string> {["objectGuid"]=item.Guid,["peer"]=peer.SelectedItem as string??""});
            }));
            foreach (var entry in new Dictionary<string,string> {["Sblocca…"]="ad.unlock",["Abilita…"]="ad.enable",["Disabilita…"]="ad.disable",["Reset password…"]="ad.password"}) {
                string operation=entry.Value;
                bar.Controls.Add(Theme.Button(entry.Key,async delegate { await UserChange(operation); }));
            }
            directory.BackgroundColor=Theme.Surface; directory.DefaultCellStyle.BackColor=Theme.Surface; directory.DefaultCellStyle.ForeColor=Theme.Text;
            directory.Columns.Add("name","Nome"); directory.Columns.Add("guid","GUID"); directory.Columns.Add("dn","Percorso");
            tab.Controls.Add(directory); tab.Controls.Add(bar);
        }
        private sealed class DirectorySelection { public string Guid,Dn,Name,Kind; }
        private DirectorySelection Selection()
        {
            DirectorySelection item=directory.SelectedRows.Count==1 ? directory.SelectedRows[0].Tag as DirectorySelection : null;
            if (item==null) SetFeedback("Seleziona un oggetto restituito dalla ricerca.");
            return item;
        }
        private async Task SearchDirectory()
        {
            await Work(false,async ()=> {
                directory.Rows.Clear(); RequireSession();
                StepResult result=await Execute("ad.search",new Dictionary<string,string> {
                    ["query"]=search.Text,["kind"]=(string)kind.SelectedItem,["page"]=page.ToString(System.Globalization.CultureInfo.InvariantCulture)});
                if (result.Data is JsonElement data && data.TryGetProperty("Rows",out JsonElement rows) && rows.ValueKind==JsonValueKind.Array)
                    foreach (JsonElement row in rows.EnumerateArray()) {
                        string id=row.GetProperty("ObjectGUID").GetString(),dn=row.GetProperty("DistinguishedName").GetString(),name=row.GetProperty("Name").GetString();
                        int index=directory.Rows.Add(name,id,dn);
                        directory.Rows[index].Tag=new DirectorySelection {Guid=id,Dn=dn,Name=name,Kind=(string)kind.SelectedItem};
                    }
                SetFeedback("Pagina "+(page+1)+". Stato e limiti della ricerca nei risultati; seleziona l'utente prima di un intervento.");
            });
        }
        private async Task UserChange(string action)
        {
            DirectorySelection item=Selection(); if (item==null) return;
            if (item.Kind!="user") { SetFeedback("Le modifiche help desk sono limitate agli utenti ordinari."); return; }
            string secret="";
            if (action=="ad.password") {
                using (PasswordEntry form=new PasswordEntry())
                    if (await Navigation.PresentAsync(form)==DialogResult.OK) secret=form.TakePassword(); else return;
            }
            await Run(action,new Dictionary<string,string> {["objectGuid"]=item.Guid,["expectedDn"]=item.Dn},secret);
        }
        private void BuildDfs()
        {
            Panel tab=Tab("DFS e replica"); FlowLayoutPanel bar=Bar(); bar.Dock=DockStyle.Top;
            TextBox path=Field(),destination=Field(),group=Field(),folder=Field();
            path.PlaceholderText=@"\\dominio\namespace\cartella"; destination.PlaceholderText="Partner FQDN del profilo";
            group.PlaceholderText="Gruppo replica"; folder.PlaceholderText="Cartella replicata";
            bar.Controls.Add(path); bar.Controls.Add(Theme.Button("Namespace e target",async delegate { await Run("dfs.namespace",new Dictionary<string,string> {["path"]=path.Text}); }));
            bar.Controls.Add(destination); bar.Controls.Add(group); bar.Controls.Add(folder);
            bar.Controls.Add(Theme.Button("Due campioni backlog",async delegate { await Run("dfsr.backlog",new Dictionary<string,string> {
                ["peer"]=destination.Text.Trim(),["group"]=group.Text,["folder"]=folder.Text}); }));
            bar.Controls.Add(Theme.Button("Replica AD",async delegate { await Run("ad.replication"); }));
            bar.Controls.Add(Theme.Button("SYSVOL / NETLOGON",async delegate { await Run("domain.sysvol"); }));
            tab.Controls.Add(bar);
        }
        private void BuildEndpoint()
        {
            Panel tab=Tab("Interventi postazione"); FlowLayoutPanel bar=Bar(); bar.Dock=DockStyle.Top;
            ComboBox operations=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=350,DisplayMember="Title"};
            StyleChoice(operations);
            operations.Items.AddRange(EnterpriseCatalog.Actions.Where(a=>a.Risk==ActionRisk.EndpointChange).ToArray()); operations.SelectedIndex=0;
            bar.Controls.Add(operations);
            bar.Controls.Add(Theme.Button("Anteprima intervento…",async delegate {
                ActionDefinition selected=(ActionDefinition)operations.SelectedItem;
                if (selected.Elevated) await RunLocal(selected.Id,true); else await Run(selected.Id);
            }));
            bar.Controls.Add(Theme.Label("Interventi sui DC bloccati. Reset rete: possibile perdita della connessione; annotare prima IP, gateway e DNS.\nIl canale sicuro si ripara solo sul PC attuale con UAC: avvia l'app sul membro interessato.\nSFC/DISM e reset richiedono verifica del riepilogo e, se indicato, riavvio manuale.",Theme.Muted));
            tab.Controls.Add(bar);
        }
        private void BuildProvisioning()
        {
            Panel tab=Tab("Predisposizione remota"); FlowLayoutPanel bar=Bar(); bar.Dock=DockStyle.Top;
            bar.Controls.Add(Theme.Label("Esporta script revisionabili, modulo JEA e istruzioni GPO.\nApplicare in laboratorio e poi tramite strumenti aziendali; nessuna configurazione remota avviata da questa pagina.",Theme.Muted));
            bar.Controls.Add(Theme.Button("Esporta pacchetto…",delegate {
                using (FolderBrowserDialog dialog=new FolderBrowserDialog {Description="Cartella del pacchetto di predisposizione"})
                    if (dialog.ShowDialog(Navigation.Window)==DialogResult.OK)
                        try { RemoteProvisioning.Export(dialog.SelectedPath); SetFeedback("Pacchetto esportato: revisiona README e modalità Inspect/WhatIf prima dell'applicazione."); }
                        catch (Exception ex) { SetFeedback(ex.Message); }
            }));
            tab.Controls.Add(bar);
        }
        private async Task Connect(bool alternate)
        {
            await Work(false,async ()=> {
                if (Selected==null) throw new InvalidOperationException("Crea o seleziona un profilo.");
                Disconnect();
                session=await CustomerSession.Start(Selected,alternate,owner:Navigation.Window.Handle);
                identity.Text=session.Identity+" · autenticazione da verificare sul target";
                Append("Sessione creata per "+Selected.Customer+". L'avvio non prova validità delle credenziali.");
            });
        }
        private void RequireSession()
        { if (session==null || Selected==null) throw new InvalidOperationException("Connetti prima il profilo cliente."); }
        private async Task Run(string id,Dictionary<string,string> args=null,string secret="")
        { await Work(EnterpriseCatalog.Get(id).IsChange,async ()=> { RequireSession(); await Execute(id,args,secret); }); }
        private async Task<StepResult> Execute(string id,Dictionary<string,string> args=null,string secret="")
        {
            ActionDefinition action=EnterpriseCatalog.Get(id);
            OperationRequest request=new OperationRequest {Profile=session.Profile.Copy(),ActionId=id,Target=Target,
                Parameters=args??new Dictionary<string,string>(),ProtectedSecret=secret,Confirmed=action.IsChange};
            action.Validate(request);
            if (action.Risk==ActionRisk.DirectoryChange) {
                StepResult before=await Execute("ad.userstate",new Dictionary<string,string> {["objectGuid"]=request.Parameters["objectGuid"]});
                if (before.State!=StepState.Success || !before.AuthenticationVerified) return before;
            }
            if (action.Risk==ActionRisk.EndpointChange) {
                StepResult before=await Execute("endpoint.verify",new Dictionary<string,string> {["operation"]=action.Id});
                if (before.State!=StepState.Success || !before.AuthenticationVerified) return before;
            }
            if (action.IsChange && !await ConfirmDialog.Ask(this,action.Title,WorkerHost.Preview(request)+"\n\nIdentità: "+session.Identity+"\nL'audit è obbligatorio; nessun ripristino automatico della password."))
                return new StepResult {State=StepState.Cancelled};
            Append("AVVIO · "+action.Title+" · "+request.Profile.Customer+" · "+request.Target);
            Append("Fonte Microsoft: "+action.Source);
            StepResult result=await session.Execute(request,cancellation.Token);
            AddResult(request.Profile,result);
            if (action.Risk==ActionRisk.EndpointChange)
                await Execute("endpoint.verify",new Dictionary<string,string> {["operation"]=action.Id});
            identity.Text=session.Identity+(result.AuthenticationVerified ? " · autenticazione target riuscita" : " · autenticazione non verificata");
            return result;
        }
        private async Task RunLocal(string id,bool elevated,Dictionary<string,string> args=null)
        {
            ActionDefinition definition=EnterpriseCatalog.Get(id);
            if(definition.IsChange && !await ConfirmDialog.Ask(this,definition.Title,"Intervento sul computer locale con UAC.\n"+definition.Title+"\n"+String.Join("\n",(args??new Dictionary<string,string>()).Select(p=>p.Key+": "+p.Value)))) return;
            await Work(definition.IsChange,async ()=> {
                EnvironmentProfile profile=LocalPrivilege.TechnicianProfile();
                OperationRequest request=new OperationRequest {Profile=profile,ActionId=id,Target=profile.DomainController,
                    Parameters=args??new Dictionary<string,string>(),Confirmed=EnterpriseCatalog.Get(id).IsChange};
                EnterpriseCatalog.Get(id).Validate(request);
                using (CustomerSession local=await CustomerSession.Start(profile,false,elevated,Navigation.Window.Handle))
                    AddResult(profile,await local.Execute(request,cancellation.Token));
            });
        }
        private void AddResult(EnvironmentProfile profile,StepResult result)
        {
            reportProfile=profile; results.Add(result); if (results.Count>64) results.RemoveAt(0);
            Append(profile.Customer+" · "+result.Target+" · "+result.State+"\r\n"+result.Summary+"\r\n"+
                String.Join("\r\n",result.Evidence)+"\r\n"+(result.Data?.ToString()??"")+
                (String.IsNullOrEmpty(result.ErrorCode) ? "" : "\r\nCodice: "+result.ErrorCode));
        }
        private async Task Work(bool change,Func<Task> action)
        {
            if (busy) return;
            busy=true;changing=change;BlocksPush=true; profileBar.Enabled=false; targets.Enabled=false; tabs.Enabled=false;
            cancel.Enabled=!change; CloseAction.Enabled=false;Navigation.Current.BackButton.Enabled=!change; cancellation=new CancellationTokenSource();
            SetFeedback(change ? "Intervento in corso: attendi l'esito. Non chiudere l'app." : "Diagnostica in corso…");
            try { await action(); }
            catch (OperationCanceledException) { SetFeedback("Operazione annullata."); }
            catch (Exception ex) { SetFeedback("Operazione non completata: "+ex.Message); Append("Operazione non verificata. Rileggere lo stato prima di ripetere modifiche."); }
            finally { cancellation.Dispose(); cancellation=null; busy=false;BlocksPush=false; profileBar.Enabled=true; targets.Enabled=true; tabs.Enabled=true; cancel.Enabled=false; CloseAction.Enabled=true;Navigation.Current.BackButton.Enabled=true; }
        }
        private void Append(string line) { text.Append(line+"\r\n\r\n"); output.Text=text.Read(); }
        private void Disconnect()
        {
            session?.Dispose(); session=null; results.Clear(); reportProfile=null; text.Drain(); output.Clear(); directory.Rows.Clear(); identity.Text="Nessuna sessione di rete.";
        }
        private void RefreshTargets()
        {
            targets.Items.Clear(); peer.Items.Clear();
            if (Selected==null) return;
            targets.Items.AddRange(Selected.Targets); peer.Items.AddRange(Selected.Targets);
            targets.SelectedItem=Selected.DomainController;
            if (peer.Items.Count>0) peer.SelectedIndex=peer.Items.Count>1 ? 1 : 0;
        }
        private void RefreshProfiles(Guid? id)
        {
            profiles.Items.Clear(); profiles.Items.AddRange(environments.ToArray());
            if (id.HasValue) profiles.SelectedItem=environments.FirstOrDefault(p=>p.Id==id);
            else if (profiles.Items.Count>0) profiles.SelectedIndex=0;
            RefreshTargets();
        }
        private async Task EditProfile(bool create)
        {
            if (!create && Selected==null) { SetFeedback("Seleziona un profilo."); return; }
            using (ProfileEditor editor=new ProfileEditor(create ? new EnvironmentProfile() : Selected.Copy()))
                if (await Navigation.PresentAsync(editor)==DialogResult.OK) {
                    Disconnect(); environments.RemoveAll(p=>p.Id==editor.Profile.Id); environments.Add(editor.Profile);
                    try { ProfileStore.Save(environments); RefreshProfiles(editor.Profile.Id); }
                    catch (Exception ex) { SetFeedback("Profilo non salvato: "+ex.Message); }
                }
        }
        private async Task DeleteProfile()
        {
            if (Selected==null || !await ConfirmDialog.Ask(this,"Elimina profilo",Selected.Customer+"\nSaranno rimossi soltanto i dati del profilo; audit e report rimangono.")) return;
            Guid id=Selected.Id; Disconnect(); environments.RemoveAll(p=>p.Id==id);
            try { ProfileStore.Save(environments); RefreshProfiles(null); } catch (Exception ex) { SetFeedback(ex.Message); }
        }
        private async Task Export(bool html)
        {
            if (busy || reportProfile==null) return;
            StepResult[] subset=results.Where(r=>r.ProfileId==reportProfile.Id).ToArray();
            string preview=ReportExport.Json(reportProfile,subset);
            using (TextDialog form=new TextDialog("Anteprima dati da esportare",preview)) await Navigation.PresentAsync(form);
            if (!await ConfirmDialog.Ask(this,"Esporta dati aziendali","Cliente: "+reportProfile.Customer+"\nIl rapporto include nomi, identità, percorsi e risultati. Conferma la destinazione secondo le policy del cliente.")) return;
            using (SaveFileDialog dialog=new SaveFileDialog {Filter=html ? "Rapporto HTML|*.html" : "Rapporto JSON|*.json",FileName="OnlyTweak-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")})
                if (dialog.ShowDialog(Navigation.Window)==DialogResult.OK)
                    try { File.WriteAllText(dialog.FileName,html ? ReportExport.Html(reportProfile,subset) : preview,new UTF8Encoding(false)); }
                    catch (Exception ex) { SetFeedback("Esportazione fallita: "+ex.Message); }
        }
        protected override void Dispose(bool disposing) { if (disposing) { session?.Dispose(); cancellation?.Dispose();workspace?.Dispose();tabs.Dispose(); } base.Dispose(disposing); }
    }
    internal sealed class ProfileEditor : DarkPage
    {
        internal EnvironmentProfile Profile {get; private set;}
        private bool dirty,saved;
        public ProfileEditor(EnvironmentProfile profile) : base("Profilo cliente","Il profilo non contiene password.",Glyphs.Contact,null)
        {
            Profile=profile; StackPanel stack=new StackPanel(); Content.Controls.Add(stack);
            TextBox customer=Add(stack,"Cliente",profile.Customer),domain=Add(stack,"Dominio DNS",profile.Domain),
                dc=Add(stack,"DC esplicito (FQDN)",profile.DomainController),targets=Add(stack,"Destinazioni consentite, una per riga",String.Join("\r\n",profile.Targets),true),
                allowed=Add(stack,"OU utenti ordinari delegate, una per riga; vuoto blocca le modifiche",String.Join("\r\n",profile.AllowedOus),true),
                excluded=Add(stack,"OU escluse (servizi/amministratori), una per riga",String.Join("\r\n",profile.ExcludedOus),true),
                groups=Add(stack,"SID di ulteriori gruppi privilegiati da escludere",String.Join("\r\n",profile.ProtectedGroupSids),true),
                jea=Add(stack,"Endpoint JEA, opzionale",profile.JeaEndpoint);
            CheckBox https=new CheckBox {Text="WinRM HTTPS con certificato valido",Checked=profile.UseHttps,AutoSize=true,ForeColor=Theme.Text}; stack.Controls.Add(https);
            CheckBox reviewed=new CheckBox {Text="OU e gruppi revisionati: incluse solo persone, esclusi servizi e ruoli privilegiati aziendali",
                Checked=profile.DelegatedUserOusReviewed,AutoSize=true,ForeColor=Theme.Text};
            stack.Controls.Add(reviewed);
            Footer.Controls.Add(Theme.Button("Salva profilo",delegate {
                try {
                    profile.Customer=customer.Text.Trim(); profile.Domain=domain.Text.Trim(); profile.DomainController=dc.Text.Trim();
                    profile.Targets=Lines(targets); profile.AllowedOus=Lines(allowed); profile.ExcludedOus=Lines(excluded);
                    profile.ProtectedGroupSids=Lines(groups); profile.JeaEndpoint=jea.Text.Trim(); profile.UseHttps=https.Checked;
                    profile.DelegatedUserOusReviewed=reviewed.Checked;
                    profile.Validate(); saved=true;DialogResult=DialogResult.OK; Close();
                } catch (Exception ex) { SetFeedback(ex.Message); }
            }));
            foreach(TextBox field in AllFields(stack)) field.TextChanged+=delegate {dirty=true;};
            https.CheckedChanged+=delegate {dirty=true;};reviewed.CheckedChanged+=delegate {dirty=true;};
        }
        private static IEnumerable<TextBox> AllFields(Control parent) {
            foreach(Control child in parent.Controls) {if(child is TextBox field) yield return field;foreach(var field2 in AllFields(child)) yield return field2;}
        }
        internal override async Task<bool> CanLeaveAsync(bool closing) => !dirty || saved || await ConfirmDialog.Ask(this,"Modifiche non salvate","Scartare le modifiche al profilo e uscire dall'editor?");
        private static string[] Lines(TextBox box) => box.Lines.Select(s=>s.Trim()).Where(s=>s.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        private static TextBox Add(StackPanel stack,string title,string value,bool multiple=false)
        {
            stack.Controls.Add(Theme.Label(title,Theme.Muted));
            TextBox field=new TextBox {Text=value,Multiline=multiple,Height=multiple ? 68 : 28,BackColor=Theme.Surface,ForeColor=Theme.Text};
            if (multiple) field.ScrollBars=ScrollBars.Vertical;
            stack.Controls.Add(field); return field;
        }
    }
    internal sealed class PasswordEntry : DarkPage
    {
        private string protectedPassword;
        internal string TakePassword() {string value=protectedPassword;protectedPassword=null;return value;}
        public PasswordEntry() : base("Nuova password","Inserisci due volte la password. Nessun salvataggio negli appunti o nei report.",Glyphs.Shield,null)
        {
            StackPanel stack=new StackPanel(); Content.Controls.Add(stack);
            TextBox first=new TextBox {UseSystemPasswordChar=true,ShortcutsEnabled=false,BackColor=Theme.Surface,ForeColor=Theme.Text},
                second=new TextBox {UseSystemPasswordChar=true,ShortcutsEnabled=false,BackColor=Theme.Surface,ForeColor=Theme.Text};
            stack.Controls.Add(Theme.Label("Nuova password",Theme.Muted)); stack.Controls.Add(first);
            stack.Controls.Add(Theme.Label("Ripeti password",Theme.Muted)); stack.Controls.Add(second);
            Footer.Controls.Add(Theme.Button("Usa password",delegate {
                if (first.Text.Length==0 || first.Text!=second.Text) { SetFeedback("Le password devono coincidere e non essere vuote."); return; }
                using (SecureString secret=new SecureString()) {
                    foreach (char ch in first.Text) secret.AppendChar(ch);
                    secret.MakeReadOnly(); protectedPassword=WindowsCredentials.Protect(secret);
                }
                first.Clear(); second.Clear(); DialogResult=DialogResult.OK; Close();
            }));
            Disposed+=delegate { first.Clear(); second.Clear();protectedPassword=null; };
        }
    }
    internal sealed class EnterpriseSectionPage : DarkPage
    {
        private readonly EnterpriseDialog owner;
        internal EnterpriseDialog WorkspaceOwner => owner;
        internal void ShowWorkspaceFeedback(string text) {SetFeedback(text);}
        internal EnterpriseSectionPage(EnterpriseDialog owner,Panel workspace,string title) : base(title,"Cliente, identità e destinazione indicati nel contesto della sessione.",Glyphs.Network,null)
        {
            this.owner=owner;Content.Controls.Add(workspace);
            foreach(Control control in owner.WorkspaceFooter) Footer.Controls.Add(control);
        }
        internal override Task<bool> CanLeaveAsync(bool closing) => owner.CanLeaveAsync(closing);
        internal override void OnRemoved() {
            Content.Controls.Clear();
            foreach(Control control in Footer.Controls.Cast<Control>().Where(c=>c!=CloseAction).ToArray()) owner.RestoreFooter(control);
        }
        protected override void Dispose(bool disposing) {if(disposing) OnRemoved();base.Dispose(disposing);}
    }
    internal sealed class WorkspaceSections : UserControl
    {
        internal readonly List<Panel> TabPages=new List<Panel>();
        private int selected;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal int SelectedIndex {get=>selected;set {
            if(value<0 || value>=TabPages.Count) return;
            Controls.Clear();selected=value;Panel page=TabPages[value];page.Dock=DockStyle.Fill;Controls.Add(page);
        }}
        protected override void Dispose(bool disposing) {if(disposing) foreach(var page in TabPages) page.Dispose();base.Dispose(disposing);}
    }
}
