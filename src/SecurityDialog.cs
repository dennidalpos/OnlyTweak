using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OnlyTweakWindows
{
    internal sealed class SecurityDialog : DarkPage
    {
        private readonly AppServices app;
        private readonly List<SecurityPane> panes=new List<SecurityPane>();
        private readonly Button refresh;
        private bool loading;
        public SecurityDialog(AppServices app,bool loadOnShown=true) : base("CompatibilitÃ  e protezioni","Computer locale, tutti gli utenti.",Glyphs.Shield,
            "Letture dello stato e accesso alle pagine Windows. Le modifiche si effettuano negli strumenti di sistema.")
        {
            this.app=app;
            StackPanel stack=new StackPanel(); Content.Controls.Add(stack);
            foreach (SecurityDefinition definition in app.Security.AvailableDefinitions(app.Availability))
            {
                if (definition.Id=="hvci") stack.Controls.Add(Theme.Section("Isolamento core",Glyphs.Shield));
                SecurityPane pane=new SecurityPane(app,definition,SetFeedback); pane.Describe(Tips); panes.Add(pane); stack.Controls.Add(pane);
            }
            refresh=Theme.Button("Aggiorna stato",async delegate { await RefreshStates(); }); Footer.Controls.Add(refresh);
            if (loadOnShown) Shown+=async delegate { await RefreshStates(); };

        }
        internal override Task<bool> CanLeaveAsync(bool closing) => Task.FromResult(!loading && !app.Coordinator.IsBusy);
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
            SetFeedback("Lettura protezioni e criteriâ€¦");
            try
            {
                List<SecurityStatus> states=await Task.Run(delegate {
                    SecurityEnvironment environment=app.Security.Probe(); List<SecurityStatus> results=new List<SecurityStatus>();
                    foreach (SecurityPane pane in panes)
                    {
                        try { results.Add(app.Security.Read(pane.Definition,environment)); }
                        catch (Exception ex) { results.Add(new SecurityStatus { ReadFailed=true,Configured="lettura non riuscita: "+ex.Message,Effective="sconosciuto" }); }
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
    }
    internal sealed class SecurityPane : UserControl
    {
        internal readonly SecurityDefinition Definition;
        private readonly bool applicableDriverToggle;
        private readonly Label state,reason,title,detail;
        private readonly FlowLayoutPanel buttons;
        private readonly Panel layout;
        private bool arranging;
        public SecurityPane(AppServices app,SecurityDefinition definition,Action<string> feedback)
        {
            Definition=definition; applicableDriverToggle=app.OS.IsWindows11 && app.OS.Build>=22621;
            AutoScaleMode=AutoScaleMode.None; BackColor=Theme.Surface; Margin=new Padding(0,0,0,12); Padding=new Padding(16,12,16,12); Height=250;
            layout=new Panel { Dock=DockStyle.Fill,Margin=Padding.Empty };
            title=Theme.Label(definition.Title,Theme.Text); title.Font=new Font("Segoe UI",11F,FontStyle.Bold);
            detail=Theme.Label(definition.Summary??definition.Description,Theme.Muted); detail.Font=new Font("Segoe UI",9F);
            AccessibleName=definition.Title; AccessibleDescription=definition.Description;
            state=Theme.Label("Stato non ancora letto.",Theme.Muted); state.Font=new Font("Segoe UI",9F);
            reason=Theme.Label("",Theme.Warning); reason.Font=new Font("Segoe UI",9F);
            buttons=new FlowLayoutPanel { AutoSize=false,WrapContents=true,Margin=Padding.Empty };
            ActionSpec page=app.Catalog.Target("security.page."+definition.Id,definition.Title,definition.Description,definition.Page);
            if (page.Applicable) buttons.Controls.Add(Theme.Button("Apri pagina Windows",async delegate {
                await ActionDispatcher.Invoke(FindForm(),app,page,feedback);
            }));
            layout.Controls.AddRange(new Control[] {title,detail,state,reason,buttons}); Controls.Add(layout);
            layout.SizeChanged+=delegate { Arrange(); }; SetBusy();
        }
        private void Arrange()
        {
            if (arranging) return;
            arranging=true;
            try
            {
                int width=Math.Max(100,ClientSize.Width-Padding.Horizontal),top=0;
                reason.Visible=reason.Text.Length>0;
                foreach (Label label in new[] { title,detail,state,reason })
                {
                    if (label==reason && reason.Text.Length==0) continue;
                    label.MaximumSize=new Size(width,0);
                    Size preferred=label.GetPreferredSize(new Size(width,0));
                    label.SetBounds(0,top,width,preferred.Height); top+=preferred.Height+label.Margin.Vertical;
                }
                buttons.Width=width;
                int buttonHeight=buttons.Controls.Count==0 ? 0 : buttons.GetPreferredSize(new Size(width,0)).Height;
                buttons.SetBounds(0,top,width,buttonHeight);
                Height=Padding.Vertical+top+buttonHeight+4;
            }
            finally { arranging=false; }
        }
        internal void Describe(ToolTip tips)
        { if (Definition.Summary!=null) { tips.SetToolTip(title,Definition.Description); tips.SetToolTip(detail,Definition.Description); } }
        internal void SetBusy()
        {
            state.ForeColor=Theme.Muted; reason.ForeColor=Theme.Muted;
        }
        internal void ShowFailure(string message) { SetBusy(); state.Text="Stato sconosciuto."; state.ForeColor=Theme.Warning; reason.Text=message; reason.ForeColor=Theme.Warning; Arrange(); }
        internal void ShowState(SecurityStatus status)
        {
            state.Text=status.ToString();
            state.ForeColor=status.ReadFailed || status.Protection==ProtectionState.Inactive ? Theme.Warning :
                status.Protection==ProtectionState.Active ? Theme.Accent : Theme.Muted;
            reason.Text=Definition.Id=="drivers" && !(applicableDriverToggle) ? "Stato blocklist; gestione tramite Isolamento core." : "Modifica nella pagina Windows dedicata.";
            reason.ForeColor=status.ReadFailed ? Theme.Warning : Theme.Muted;
            Arrange();
        }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using (Pen pen=new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1); }
    }
}
