using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OnlyTweakWindows
{
    internal sealed class MainForm : DarkForm
    {
        public MainForm(AppServices app) : base("OnlyTweakWindows","Tweak, stampanti e manutenzione. Scegli da dove iniziare.")
        {
            StartPosition=FormStartPosition.CenterScreen; ClientSize=new Size(960,760);
            StackPanel stack=new StackPanel(); Content.Controls.Add(stack);
            stack.Controls.Add(Theme.Label(EnvironmentInfo.Summary+"\n"+EnvironmentInfo.Account+"  ·  Amministratore",Theme.Muted));
            // Quattro righe da 88: le schede restano alte almeno quanto Theme.Card e la nota finale resta visibile al 100%.
            TableLayoutPanel grid=new TableLayoutPanel { Height=4*88,ColumnCount=2,RowCount=4,BackColor=Theme.Background,Margin=new Padding(0,10,0,14) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            for (int i=0;i<4;i++) grid.RowStyles.Add(new RowStyle(SizeType.Percent,25F));
            AddTile(grid,"Stampanti","Stampanti, code e spooler",Glyphs.Print,0,0,delegate { using (PrinterDialog form=new PrinterDialog(app)) form.ShowDialog(this); });
            AddTile(grid,"Aspetto ed Esplora file","Menu classico, estensioni e selezione",Glyphs.Personalize,1,0,delegate { using (AppearanceDialog form=new AppearanceDialog(app)) form.ShowDialog(this); });
            AddTile(grid,"Rete","Indirizzi, DNS e ripristino",Glyphs.Network,0,1,delegate { using (MenuDialog form=new MenuDialog(app,app.Catalog.Network)) form.ShowDialog(this); });
            AddTile(grid,"Manutenzione","Integrità Windows e spazio su disco",Glyphs.Repair,1,1,delegate { using (MenuDialog form=new MenuDialog(app,app.Catalog.Maintenance)) form.ShowDialog(this); });
            AddTile(grid,"Compatibilità e protezioni","Blocchi app, memoria e driver",Glyphs.Shield,0,2,delegate { using (SecurityDialog form=new SecurityDialog(app)) form.ShowDialog(this); });
            AddTile(grid,"Account","Account e utenti del PC",Glyphs.Contact,1,2,delegate { using (MenuDialog form=new MenuDialog(app,app.Catalog.Accounts)) form.ShowDialog(this); });
            Button tools=AddTile(grid,"Strumenti Windows","Gestione computer, dispositivi e servizi",Glyphs.Apps,0,3,delegate { using (MenuDialog form=new MenuDialog(app,app.Catalog.Tools)) form.ShowDialog(this); });
            grid.SetColumnSpan(tools,2); tools.Margin=new Padding(0,0,0,10); stack.Controls.Add(grid);
            const string persistence="Le modifiche restano attive dopo la chiusura. Cronologia e ripristino dei valori precedenti sono disponibili solo in questa sessione.";
            Label note=Theme.Label("Ripristino e cronologia valgono solo per questa sessione.",Theme.Muted);
            note.Font=new Font("Segoe UI",9F); note.Margin=Padding.Empty; note.AccessibleDescription=persistence; Tips.SetToolTip(note,persistence); stack.Controls.Add(note);
            Footer.Controls.Add(Theme.Button("Cronologia",delegate { using (TextDialog form=new TextDialog("Cronologia della sessione",app.History.Read(),Glyphs.History)) form.ShowDialog(this); }));
        }
        private static Button AddTile(TableLayoutPanel grid,string title,string description,string glyph,int column,int row,EventHandler clicked)
        {
            DarkButton button=(DarkButton)Theme.Card(title,description,clicked,glyph); button.GlyphColor=Theme.Accent;
            button.Dock=DockStyle.Fill; button.Margin=new Padding(column==0 ? 0 : 6,0,column==0 ? 6 : 0,10);
            grid.Controls.Add(button,column,row); return button;
        }
    }
    internal sealed class TextDialog : DarkForm
    {
        public TextDialog(string title,string text) : this(title,text,Glyphs.Document) { }
        public TextDialog(string title,string text,string glyph) : base(title,"Informazioni della sessione corrente.",glyph,null)
        {
            Content.Padding=new Padding(26,8,26,16); DarkOutput output=Theme.Output(); output.Text=text; Content.Controls.Add(output);
            Footer.Controls.Add(Theme.Button("Copia testo",delegate { if (output.TextLength>0) Clipboard.SetText(output.Text); }));
        }
    }
    internal sealed class MenuDialog : DarkForm
    {
        public MenuDialog(AppServices app,MenuDefinition menu) : base(menu.Title,menu.Description,menu.Glyph,menu.Detail)
        {
            StackPanel stack=new StackPanel(); Content.Controls.Add(stack); AddActions(stack,app,menu);
            foreach (MenuDefinition child in menu.Children)
            {
                Label section=Theme.Section(child.Title,child.Glyph);
                if (!String.IsNullOrEmpty(child.Detail)) { section.AccessibleDescription=child.Detail; Tips.SetToolTip(section,child.Detail); }
                stack.Controls.Add(section); AddActions(stack,app,child);
            }
        }
        private void AddActions(StackPanel stack,AppServices app,MenuDefinition menu)
        {
            foreach (ActionSpec action in menu.Actions)
            {
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
    internal sealed class AppearanceDialog : DarkForm
    {
        public AppearanceDialog(AppServices app) : base("Aspetto ed Esplora file","Tweak dell'account "+EnvironmentInfo.Account+".",Glyphs.Personalize,
            "Stato e comandi nello stesso pannello. Le modifiche riguardano l'account che esegue l'app.")
        {
            StackPanel stack=new StackPanel(); Content.Controls.Add(stack);
            foreach (TweakDefinition definition in app.Tweaks.Definitions) { TweakPane pane=new TweakPane(app,definition); pane.Describe(Tips); stack.Controls.Add(pane); }
            stack.Controls.Add(Theme.Section("Applica alla shell",Glyphs.Settings));
            stack.Controls.Add(MenuDialog.ActionCard(app.Catalog.ExplorerRestart,delegate { OperationDialog.Open(this,app,app.Catalog.ExplorerRestart); },Tips));
            FormClosing+=delegate(object sender,FormClosingEventArgs e) { if (app.Coordinator.IsBusy) e.Cancel=true; };
        }
    }
    internal sealed class TweakPane : UserControl
    {
        private readonly AppServices app;
        private readonly TweakDefinition definition;
        private readonly Label state,title,detail;
        private readonly Button restore,on,off;
        private readonly FlowLayoutPanel buttons;
        public TweakPane(AppServices app,TweakDefinition definition)
        {
            this.app=app; this.definition=definition;
            AutoScaleMode=AutoScaleMode.None; AutoSize=false; Height=180;
            BackColor=Theme.Surface; Margin=new Padding(0,0,0,12); Padding=new Padding(16,12,16,12);
            TableLayoutPanel layout=new TableLayoutPanel { Dock=DockStyle.Fill,AutoSize=false,ColumnCount=1,RowCount=4,Margin=Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            title=Theme.Label(definition.Title,Theme.Text); title.Font=new Font("Segoe UI",11F,FontStyle.Bold); title.Margin=new Padding(0,0,0,5);
            detail=Theme.Label(definition.Summary??definition.Description,Theme.Muted); detail.Font=new Font("Segoe UI",9F); detail.Margin=new Padding(0,0,0,5);
            AccessibleName=definition.Title; AccessibleDescription=definition.Description;
            state=Theme.Label("",Theme.Accent); state.Font=new Font("Segoe UI",9F); state.Margin=Padding.Empty;
            buttons=new FlowLayoutPanel { Dock=DockStyle.Fill,AutoSize=true,WrapContents=true,Margin=Padding.Empty,Padding=Padding.Empty };
            on=Theme.Button(definition.EnabledLabel,async delegate { await Change(true,false); });
            off=Theme.Button(definition.DisabledLabel,async delegate { await Change(false,false); });
            restore=Theme.Button("Ripristina precedente",async delegate { await Change(false,true); });
            foreach (Button button in new[] { on,off,restore }) { button.AutoSize=true; button.MinimumSize=new Size(120,36); button.Margin=new Padding(0,8,8,0); button.Height=36; buttons.Controls.Add(button); }
            restore.Width=178;
            layout.Controls.Add(title,0,0); layout.Controls.Add(detail,0,1); layout.Controls.Add(state,0,2); layout.Controls.Add(buttons,0,3); Controls.Add(layout);
            layout.SizeChanged+=delegate {
                int width=Math.Max(100,layout.ClientSize.Width-6); detail.MaximumSize=new Size(width,0); state.MaximumSize=new Size(width,0);
                int needed=Padding.Vertical+title.PreferredHeight+title.Margin.Vertical+detail.GetPreferredSize(new Size(width,0)).Height+detail.Margin.Vertical+
                    state.GetPreferredSize(new Size(width,0)).Height+buttons.GetPreferredSize(new Size(width,0)).Height+6;
                if (Height!=needed) Height=needed;
            };
            RefreshState();
        }
        internal void Describe(ToolTip tips)
        { if (definition.Summary!=null) { tips.SetToolTip(title,definition.Description); tips.SetToolTip(detail,definition.Description); } }
        private async Task Change(bool enabled,bool restoring)
        {
            on.Enabled=false; off.Enabled=false; restore.Enabled=false;
            ActionSpec action=new ActionSpec("tweak."+definition.Id,restoring ? "Ripristino valore precedente" : enabled ? definition.EnabledLabel : definition.DisabledLabel,definition.Description,"Account corrente",
                delegate(Action<string> log) { if (restoring) app.Tweaks.Restore(definition,log); else app.Tweaks.Apply(definition,enabled,log); });
            string result="";
            try { await ActionDispatcher.Launch(app,action,delegate(string message) { result=message; if (!IsDisposed) state.Text=message; }); }
            finally
            {
                if (!IsDisposed)
                {
                    on.Enabled=true; off.Enabled=true; RefreshState();
                    if (result.StartsWith("Operazione non riuscita",StringComparison.Ordinal)) { state.Text=result; state.ForeColor=Theme.Warning; }
                }
            }
        }
        private void RefreshState()
        {
            try { state.Text=app.Tweaks.Describe(definition); state.ForeColor=Theme.Accent; }
            catch (Exception ex) { state.Text=ex.Message; state.ForeColor=Theme.Warning; }
            restore.Enabled=app.Tweaks.CanRestore(definition);
        }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using (Pen pen=new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1); }
    }
    internal sealed class PrinterDialog : DarkForm
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
            Button purge=Theme.Button("Annulla lavori…",delegate { PrinterItem item=list.SelectedItem as PrinterItem; if (item!=null) OperationDialog.Open(this,app,app.Catalog.PurgePrinter(item.Name)); });
            purge.Enabled=false; selectedButtons.Add(purge); actions.Controls.Add(purge);
            layout.SizeChanged+=delegate { selection.MaximumSize=new Size(Math.Max(100,layout.Width-60),0); status.MaximumSize=selection.MaximumSize; };
            layout.Controls.Add(status,0,0); layout.Controls.Add(list,0,1); layout.Controls.Add(selection,0,2); layout.Controls.Add(actions,0,3); Content.Controls.Add(layout);
            refresh=Theme.Button("Aggiorna",async delegate { await RefreshPrinters(); }); Footer.Controls.Add(refresh);
            Footer.Controls.Add(Theme.Button("Spooler…",delegate { using (MenuDialog form=new MenuDialog(app,app.Catalog.Spool)) form.ShowDialog(this); }));
            Footer.Controls.Add(Theme.Button("Stampanti e scanner",async delegate { await ActionDispatcher.Invoke(this,app,app.Catalog.Settings("printers.settings","Stampanti e scanner","Impostazioni Windows","ms-settings:printers"),SetFeedback); }));
            Footer.Controls.Add(Theme.Button("Vista classica",async delegate { await ActionDispatcher.Invoke(this,app,app.Catalog.ClassicPrinterView,SetFeedback); }));
            if (loadOnShown) Shown+=async delegate { await RefreshPrinters(); };
        }
        private void AddPrinterButton(FlowLayoutPanel actions,string title,string mode)
        {
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
        protected override void OnFormClosing(FormClosingEventArgs e) { if (app.Coordinator.IsBusy) e.Cancel=true; base.OnFormClosing(e); }
    }
}
