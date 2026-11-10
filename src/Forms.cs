using System;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OnlyTweakWindows
{
    internal sealed class HomePage : DarkPage
    {
        public HomePage(AppServices app) : base("OnlyTweakWindows","Diagnostica di dominio e assistenza alle postazioni aziendali.")
        {
            ClientSize=new Size(960,760);
            StackPanel stack=new StackPanel(); Content.Controls.Add(stack);
            stack.Controls.Add(Theme.Label(app.OS.Summary+"\n"+EnvironmentInfo.Account+"  ·  "+(LocalPrivilege.IsAdministrator ? "Amministratore locale" : "Utente standard"),Theme.Muted));
            // Quattro righe da 88: le schede restano alte almeno quanto Theme.Card e la nota finale resta visibile al 100%.
            TableLayoutPanel grid=new TableLayoutPanel { Height=4*88,ColumnCount=2,RowCount=4,BackColor=Theme.Background,Margin=new Padding(0,10,0,14) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            for (int i=0;i<4;i++) grid.RowStyles.Add(new RowStyle(SizeType.Percent,25F));
            AddTile(grid,"Ambienti aziendali","Clienti, dominio, procedure e azioni help desk",Glyphs.Network,delegate { Navigation.OpenEnterprise(false); });
            AddTile(grid,"Postazione tecnico","Prerequisiti, RSAT e predisposizione remota",Glyphs.Settings,delegate { Navigation.OpenEnterprise(true); });
            if (app.Availability.File("winspool.drv")) AddTile(grid,"Stampanti","Stampanti, code e spooler",Glyphs.Print,delegate { Navigation.Push(new PrinterDialog(app)); });
            if (app.Catalog.Network.AllActions().Any()) AddTile(grid,"Rete","Indirizzi, DNS e ripristino",Glyphs.Network,delegate { Navigation.Push(new MenuDialog(app,app.Catalog.Network)); });
            if (app.Catalog.Maintenance.AllActions().Any()) AddTile(grid,"Manutenzione","Integrità Windows e spazio su disco",Glyphs.Repair,delegate { Navigation.Push(new MenuDialog(app,app.Catalog.Maintenance)); });
            if (app.Security.AvailableDefinitions(app.Availability).Any()) AddTile(grid,"Compatibilità e protezioni","Blocchi app, memoria e driver",Glyphs.Shield,delegate { Navigation.Push(new SecurityDialog(app)); });
            AddTile(grid,"Tweak Windows","Esplora file e menu classico",Glyphs.Settings,delegate { Navigation.Push(new TweakPage(app)); });
            Button tools=app.Catalog.Tools.AllActions().Any() ? AddTile(grid,"Strumenti Windows","Gestione computer, dispositivi e servizi",Glyphs.Apps,delegate { Navigation.Push(new MenuDialog(app,app.Catalog.Tools)); }) : null;
            if (tools!=null && grid.Controls.Count%2==1) { grid.SetColumnSpan(tools,2); tools.Margin=new Padding(0,0,0,10); }
            grid.RowCount=Math.Max(1,(grid.Controls.Count+1)/2); grid.Height=grid.RowCount*88;
            grid.RowStyles.Clear(); for (int row=0;row<grid.RowCount;row++) grid.RowStyles.Add(new RowStyle(SizeType.Percent,100F/grid.RowCount));
            stack.Controls.Add(grid);
            Label note=Theme.Label("Seleziona cliente, identità e destinazione prima delle operazioni aziendali.\nElevazione richiesta soltanto per le operazioni locali che la richiedono.",Theme.Muted);
            stack.Controls.Add(note);
            Footer.Controls.Add(Theme.Button("Cronologia",delegate { Navigation.Push(new TextDialog("Cronologia della sessione",app.History.Read(),Glyphs.History)); }));
        }
        private static Button AddTile(TableLayoutPanel grid,string title,string description,string glyph,EventHandler clicked)
        {
            int column=grid.Controls.Count%2,row=grid.Controls.Count/2;
            DarkButton button=(DarkButton)Theme.Card(title,description,clicked,glyph); button.GlyphColor=Theme.Accent;
            button.Dock=DockStyle.Fill; button.Margin=new Padding(column==0 ? 0 : 6,0,column==0 ? 6 : 0,10);
            grid.Controls.Add(button,column,row); return button;
        }
    }
    internal sealed class TextDialog : DarkPage
    {
        public TextDialog(string title,string text) : this(title,text,Glyphs.Document) { }
        public TextDialog(string title,string text,string glyph) : base(title,"Informazioni della sessione corrente.",glyph,null)
        {
            Content.Padding=new Padding(26,8,26,16); DarkOutput output=Theme.Output(); output.Text=text; Content.Controls.Add(output);
            Footer.Controls.Add(Theme.Button("Copia testo",delegate { if (output.TextLength>0 && !Theme.TryCopy(output.Text)) SetFeedback("Appunti occupati: impossibile copiare."); }));
        }
    }
    internal sealed class MenuDialog : DarkPage
    {
        public MenuDialog(AppServices app,MenuDefinition menu) : base(menu.Title,menu.Description,menu.Glyph,menu.Detail)
        {
            StackPanel stack=new StackPanel(); Content.Controls.Add(stack); AddActions(stack,app,menu);
            foreach (MenuDefinition child in menu.Children)
            {
                if (!System.Linq.Enumerable.Any(child.AllActions())) continue;
                Label section=Theme.Section(child.Title,child.Glyph);
                if (!String.IsNullOrEmpty(child.Detail)) { section.AccessibleDescription=child.Detail; Tips.SetToolTip(section,child.Detail); }
                stack.Controls.Add(section); AddActions(stack,app,child);
            }
        }
        private void AddActions(StackPanel stack,AppServices app,MenuDefinition menu)
        {
            foreach (ActionSpec action in menu.Actions)
            {
                if (!action.Applicable) continue;
                ActionSpec selected=action;
                stack.Controls.Add(ActionCard(action,async delegate { await ActionDispatcher.Invoke(this,app,selected,SetFeedback); }));
            }
        }
        // Card con glifo del tipo di azione e riga breve; il testo completo resta nel tooltip e per i lettori di schermo.
        internal static Button ActionCard(ActionSpec action,EventHandler clicked,ToolTip tips)
        {
            Button card=Theme.Card(action.Title,action.CardText,clicked,Glyphs.ForAction(action)); card.AccessibleDescription=action.Description;
            if (action.CardText!=action.Description) tips.SetToolTip(card,action.Description);
            return card;
        }
        private Button ActionCard(ActionSpec action,EventHandler clicked) { return ActionCard(action,clicked,Tips); }
    }
    internal sealed class PrinterDialog : DarkPage
    {
        private readonly DarkPrinterList list;
        private readonly Label status,selection;
        private readonly Button refresh;
        private readonly List<Button> selectedButtons=new List<Button>();
        private readonly AppServices app;
        public PrinterDialog(AppServices app,bool loadOnShown=true) : base("Stampanti","Seleziona una stampante per proprietà, preferenze e coda.",Glyphs.Print,null)
        {
            this.app=app;
            TableLayoutPanel layout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(26,4,26,12) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            status=Theme.Label("Scegli una stampante nell'elenco.",Theme.Muted);
            list=new DarkPrinterList { Dock=DockStyle.Fill,Margin=new Padding(0,0,0,12),Font=new Font("Segoe UI",10F) };
            selection=Theme.Label("Nessuna stampante selezionata.",Theme.Accent);
            list.SelectedIndexChanged+=delegate {
                PrinterItem item=list.SelectedItem as PrinterItem; selection.Text=item==null ? "Nessuna stampante selezionata." : item.Name;
                foreach (Button button in selectedButtons) button.Enabled=item!=null;
            };
            list.DoubleClick+=async delegate { await OpenPrinter("/p","Proprietà"); };
            list.KeyDown+=async delegate(object sender,KeyEventArgs e) {
                if (e.KeyCode==Keys.Enter) { e.SuppressKeyPress=true; await OpenPrinter("/p","Proprietà"); }
            };
            FlowLayoutPanel actions=new FlowLayoutPanel { Dock=DockStyle.Fill,AutoSize=true,WrapContents=true,Margin=Padding.Empty };
            AddPrinterButton(actions,"Proprietà","/p"); AddPrinterButton(actions,"Preferenze","/e"); AddPrinterButton(actions,"Apri coda","/o");
            Button purge=Theme.Button("Annulla lavori…",async delegate { PrinterItem item=list.SelectedItem as PrinterItem; if (item!=null) await OperationDialog.Open(this,app,app.Catalog.PurgePrinter(item.Name)); });
            purge.Enabled=false; selectedButtons.Add(purge); actions.Controls.Add(purge);
            layout.SizeChanged+=delegate { selection.MaximumSize=new Size(Math.Max(100,layout.Width-60),0); status.MaximumSize=selection.MaximumSize; };
            layout.Controls.Add(status,0,0); layout.Controls.Add(list,0,1); layout.Controls.Add(selection,0,2); layout.Controls.Add(actions,0,3); Content.Controls.Add(layout);
            refresh=Theme.Button("Aggiorna",async delegate { await RefreshPrinters(); }); Footer.Controls.Add(refresh);
            if (app.Catalog.Spool.AllActions().Any()) Footer.Controls.Add(Theme.Button("Spooler…",delegate { Navigation.Push(new MenuDialog(app,app.Catalog.Spool)); }));
            if (app.Availability.Target("ms-settings:printers")) Footer.Controls.Add(Theme.Button("Stampanti e scanner",async delegate { await ActionDispatcher.Invoke(this,app,app.Catalog.Settings("printers.settings","Stampanti e scanner","Impostazioni Windows","ms-settings:printers"),SetFeedback); }));
            if (app.Catalog.ClassicPrinterView.Applicable) Footer.Controls.Add(Theme.Button("Vista classica",async delegate { await ActionDispatcher.Invoke(this,app,app.Catalog.ClassicPrinterView,SetFeedback); }));
            if (loadOnShown) Shown+=async delegate { await RefreshPrinters(); };
        }
        private void AddPrinterButton(FlowLayoutPanel actions,string title,string mode)
        {
            if (!app.Availability.File("rundll32.exe") || !app.Availability.File("printui.dll")) return;
            Button button=Theme.Button(title,async delegate { await OpenPrinter(mode,title); });
            button.Enabled=false; selectedButtons.Add(button); actions.Controls.Add(button);
        }
        private async Task OpenPrinter(string mode,string title)
        {
            PrinterItem item=list.SelectedItem as PrinterItem; if (item==null) return;
            foreach (Button button in selectedButtons) button.Enabled=false;
            try { await ActionDispatcher.Invoke(this,app,app.Catalog.PrinterDialog(mode,title,item.Name),SetFeedback); }
            finally { if (!IsDisposed) foreach (Button button in selectedButtons) button.Enabled=list.SelectedItem!=null; }
        }
        private async Task RefreshPrinters()
        {
            if (!app.Coordinator.TryEnter()) return;
            string old=list.SelectedItem is PrinterItem ? ((PrinterItem)list.SelectedItem).Name : null;
            refresh.Enabled=false; CloseAction.Enabled=false; list.Enabled=false;
            foreach (Button button in selectedButtons) button.Enabled=false;
            status.Text="Lettura stampanti e stato spooler…";
            try
            {
                List<PrinterItem> items=null; string spoolStatus=null,spoolError=null;
                await Task.Run(delegate { try { spoolStatus=app.Spool.Status(); } catch (Exception ex) { spoolError=ex.Message; } items=app.Printers.Read(); });
                list.Items.Clear();
                foreach (PrinterItem item in items) { int index=list.Items.Add(item); if (item.Name==old) list.SelectedIndex=index; }
                status.Text=(items.Count==0 ? "Nessuna stampante trovata." : items.Count+" stampanti")+"  ·  Spooler: "+(spoolError??spoolStatus);
            }
            catch (Exception ex) { list.Items.Clear(); status.Text="Elenco non disponibile: "+ex.Message+". Apri Spooler per verificarne lo stato."; }
            finally { app.Coordinator.Leave(); refresh.Enabled=true; CloseAction.Enabled=true; list.Enabled=true; foreach (Button button in selectedButtons) button.Enabled=list.SelectedItem!=null; }
        }
        internal override Task<bool> CanLeaveAsync(bool closing) => Task.FromResult(!app.Coordinator.IsBusy);
    }
}
