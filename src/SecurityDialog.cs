using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OnlyTweakWindows
{
    internal sealed class SecurityDialog : DarkForm
    {
        private readonly AppServices app;
        private readonly List<SecurityPane> panes=new List<SecurityPane>();
        private readonly Button refresh;
        private bool loading;
        public SecurityDialog(AppServices app,bool loadOnShown=true) : base("Compatibilità e protezioni","Computer locale, tutti gli utenti.",Glyphs.Shield,
            "Ogni modifica richiede conferma; il ripristino iniziale resta disponibile solo nella sessione. Nessun riavvio automatico.")
        {
            this.app=app;
            StackPanel stack=new StackPanel(); Content.Controls.Add(stack);
            foreach (SecurityDefinition definition in app.Security.Definitions)
            {
                if (definition.Id=="hvci") stack.Controls.Add(Theme.Section("Isolamento core",Glyphs.Shield));
                SecurityPane pane=new SecurityPane(app,definition,Change,SetFeedback); pane.Describe(Tips); panes.Add(pane); stack.Controls.Add(pane);
            }
            refresh=Theme.Button("Aggiorna stato",async delegate { await RefreshStates(); }); Footer.Controls.Add(refresh);
            if (loadOnShown) Shown+=async delegate { await RefreshStates(); };
            FormClosing+=delegate(object sender,FormClosingEventArgs e) { if (loading || app.Coordinator.IsBusy) e.Cancel=true; };
        }
        internal void DisplayStates(SecurityEnvironment environment)
        {
            foreach (SecurityPane pane in panes)
            {
                try { pane.ShowState(app.Security.Read(pane.Definition,environment)); }
                catch (Exception ex) { pane.ShowFailure(ex.Message); }
            }
        }
        private async Task RefreshStates()
        {
            if (!app.Coordinator.TryEnter()) { SetFeedback("Attendi il completamento dell'operazione in corso."); return; }
            loading=true; refresh.Enabled=false;
            foreach (SecurityPane pane in panes) pane.SetBusy();
            SetFeedback("Lettura protezioni e criteri…");
            try
            {
                List<SecurityStatus> states=await Task.Run(delegate {
                    SecurityEnvironment environment=app.Security.Probe(); List<SecurityStatus> results=new List<SecurityStatus>();
                    foreach (SecurityPane pane in panes)
                    {
                        try { results.Add(app.Security.Read(pane.Definition,environment)); }
                        catch (Exception ex) { results.Add(new SecurityStatus { Configured="lettura non riuscita: "+ex.Message,Effective="sconosciuto",
                            EnableUnavailable="Stato non verificato.",DisableUnavailable="Stato non verificato.",RestoreUnavailable="Stato non verificato." }); }
                    }
                    return results;
                });
                if (IsDisposed) return;
                for (int i=0;i<panes.Count;i++) panes[i].ShowState(states[i]);
                SetFeedback("Lettura completata. Le modifiche negli strumenti esterni si verificano con Aggiorna stato.");
            }
            catch (Exception ex) { if (!IsDisposed) { foreach (SecurityPane pane in panes) pane.ShowFailure(ex.Message); SetFeedback("Lettura non riuscita: "+ex.Message); } }
            finally { app.Coordinator.Leave(); loading=false; if (!IsDisposed) refresh.Enabled=true; }
        }
        private async void Change(SecurityDefinition definition,bool enable,bool restore)
        {
            if (loading || app.Coordinator.IsBusy) return;
            string title=(restore ? "Ripristina precedente · " : enable ? "Attiva · " : "Disattiva · ")+definition.Title;
            if (definition.Id=="store" && !restore) title=enable ? "Consenti solo app dello Store" : "Consenti app da qualsiasi origine";
            ActionSpec action=new ActionSpec("security."+definition.Id,title,definition.Description,"Computer locale · tutti gli utenti",
                delegate(Action<string> log) { app.Security.Change(definition,enable,restore,log); });
            action.Confirmation=(restore ? "Ripristinare la configurazione originale di questa sessione?" : title+"?")+
                "\n"+definition.Description+"\nLa modifica riguarda tutti gli utenti del computer."+
                (definition.Restart ? " Richiede un riavvio manuale; l'effetto prima del riavvio può essere diverso dalla configurazione." : "")+
                "\n"+(restore ? "Verranno ripristinati i valori iniziali, inclusi tipi ed eventuali assenze." : "La disattivazione riduce la protezione. Puoi ripristinare lo stato iniziale finché questa sessione resta aperta.");
            OperationDialog.Open(this,app,action);
            await RefreshStates();
        }
    }
    internal sealed class SecurityPane : UserControl
    {
        internal readonly SecurityDefinition Definition;
        private readonly Label state,reason,title,detail;
        private readonly Button on,off,restore;
        private readonly FlowLayoutPanel buttons;
        private readonly TableLayoutPanel layout;
        public SecurityPane(AppServices app,SecurityDefinition definition,Action<SecurityDefinition,bool,bool> change,Action<string> feedback)
        {
            Definition=definition;
            AutoScaleMode=AutoScaleMode.None; BackColor=Theme.Surface; Margin=new Padding(0,0,0,12); Padding=new Padding(16,12,16,12); Height=250;
            layout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Margin=Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            for (int i=0;i<5;i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            title=Theme.Label(definition.Title,Theme.Text); title.Font=new Font("Segoe UI",11F,FontStyle.Bold);
            detail=Theme.Label(definition.Summary??definition.Description,Theme.Muted); detail.Font=new Font("Segoe UI",9F);
            AccessibleName=definition.Title; AccessibleDescription=definition.Description;
            state=Theme.Label("Stato non ancora letto.",Theme.Accent); state.Font=new Font("Segoe UI",9F);
            reason=Theme.Label("",Theme.Warning); reason.Font=new Font("Segoe UI",9F);
            buttons=new FlowLayoutPanel { AutoSize=true,Dock=DockStyle.Fill,WrapContents=true,Margin=Padding.Empty };
            if (definition.Direct)
            {
                on=Theme.Button(definition.Id=="store" ? "Solo Microsoft Store" : "Attiva",delegate { change(definition,true,false); });
                off=Theme.Button(definition.Id=="store" ? "Qualsiasi origine" : "Disattiva",delegate { change(definition,false,false); });
                restore=Theme.Button("Ripristina precedente",delegate { change(definition,false,true); });
                buttons.Controls.Add(on); buttons.Controls.Add(off); buttons.Controls.Add(restore);
            }
            buttons.Controls.Add(Theme.Button("Apri pagina Windows",async delegate {
                await ActionDispatcher.Invoke(FindForm(),app,app.Catalog.Target("security.page."+definition.Id,definition.Title,definition.Description,definition.Page),feedback);
            }));
            layout.Controls.Add(title,0,0); layout.Controls.Add(detail,0,1); layout.Controls.Add(state,0,2); layout.Controls.Add(reason,0,3); layout.Controls.Add(buttons,0,4); Controls.Add(layout);
            layout.SizeChanged+=delegate { Arrange(); }; SetBusy();
        }
        private void Arrange()
        {
            int width=Math.Max(100,layout.ClientSize.Width-6),height=Padding.Vertical+8;
            // Un motivo vuoto non deve riservare una riga sopra i pulsanti.
            reason.Visible=reason.Text.Length>0;
            foreach (Label label in new[] { title,detail,state,reason })
            {
                label.MaximumSize=new Size(width,0);
                if (label.Visible) height+=label.GetPreferredSize(new Size(width,0)).Height+label.Margin.Vertical;
            }
            height+=buttons.GetPreferredSize(new Size(width,0)).Height;
            if (Height!=height) Height=height;
        }
        internal void Describe(ToolTip tips)
        { if (Definition.Summary!=null) { tips.SetToolTip(title,Definition.Description); tips.SetToolTip(detail,Definition.Description); } }
        internal void SetBusy() { if (on!=null) { on.Enabled=false; off.Enabled=false; restore.Enabled=false; } }
        internal void ShowFailure(string message) { SetBusy(); state.Text="Stato sconosciuto."; reason.Text=message; Arrange(); }
        internal void ShowState(SecurityStatus status)
        {
            state.Text=status.ToString();
            if (on!=null)
            {
                on.Enabled=status.EnableUnavailable==null; off.Enabled=status.DisableUnavailable==null; restore.Enabled=status.RestoreUnavailable==null;
                reason.Text=status.DisableUnavailable==status.EnableUnavailable ? status.DisableUnavailable??"" :
                    (status.EnableUnavailable==null ? "" : "Attivazione: "+status.EnableUnavailable)+
                    (status.DisableUnavailable==null ? "" : "\nDisattivazione: "+status.DisableUnavailable);
            }
            else reason.Text="Modifica nella pagina Windows dedicata.";
            Arrange();
        }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using (Pen pen=new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1); }
    }
}
