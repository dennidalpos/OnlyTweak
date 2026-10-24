using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OnlyTweakWindows
{
    internal sealed class OperationDialog : DarkForm
    {
        private readonly AppServices app;
        private readonly ActionSpec action;
        private readonly DarkOutput output;
        private readonly Label status;
        private readonly ActivityBar progress;
        private readonly Button run;
        private readonly Timer outputTimer;
        private readonly BoundedText pending=new BoundedText();
        private bool running, checking;
        private readonly bool confirmationGranted;
        public static void Open(IWin32Window parent, AppServices app, ActionSpec action)
        {
            if (app.Coordinator.IsBusy) return;
            if (action.Confirmation!=null&&!ConfirmDialog.Ask(parent,action.Title,action.Confirmation)) return;
            using (OperationDialog form=new OperationDialog(app,action,true,true)) form.ShowDialog(parent);
        }
        public OperationDialog(AppServices app,ActionSpec action,bool startAutomatically=false,bool confirmationGranted=false) : base(action.Title,action.Description,Glyphs.ForAction(action),null)
        {
            if (action.Confirmation!=null) Heading.GlyphColor=Theme.Warning;
            this.app=app; this.action=action;
            this.confirmationGranted=confirmationGranted;
            Content.Padding=new Padding(26,4,26,4);
            TableLayoutPanel layout=new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=1, RowCount=5 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,12));
            Label scope=Theme.Label("Ambito: " + action.Scope,Theme.Muted);
            Label warning=Theme.Label(startAutomatically ? "Risultato e avanzamento dell'operazione." : action.Confirmation ?? "Premi Esegui per avviare questa azione.",Theme.Muted);
            layout.SizeChanged+=delegate { scope.MaximumSize=new Size(Math.Max(100,layout.ClientSize.Width-12),0); warning.MaximumSize=scope.MaximumSize; };
            output=Theme.Output(); output.Text="Pronto. Nessuna operazione eseguita."; output.Margin=new Padding(0,3,0,3);
            status=Theme.Label("Verifica disponibilità...",Theme.Muted); status.Margin=new Padding(0,10,0,6);
            progress=new ActivityBar { Dock=DockStyle.Fill,Visible=false,Margin=Padding.Empty };
            layout.Controls.Add(scope,0,0); layout.Controls.Add(warning,0,1); layout.Controls.Add(output,0,2); layout.Controls.Add(status,0,3); layout.Controls.Add(progress,0,4);
            Content.Controls.Add(layout);
            run=Theme.Button("&Esegui",async delegate { await Execute(); });
            run.BackColor=Theme.Accent; run.ForeColor=Theme.Background; run.Enabled=false;
            run.Visible=!startAutomatically;
            Footer.Controls.Add(run); AcceptButton=startAutomatically ? CloseAction : run;
            Footer.Controls.Add(Theme.Button("Copia output",delegate { if (output.TextLength>0 && !Theme.TryCopy(output.Text)) { status.Text="Appunti occupati: impossibile copiare."; status.ForeColor=Theme.Warning; } }));
            outputTimer=new Timer { Interval=100 };
            outputTimer.Tick+=delegate { FlushOutput(); progress.Invalidate(); };
            Shown+=async delegate { await CheckAvailability(); if (startAutomatically&&run.Enabled) await Execute(); };
        }
        private async Task CheckAvailability()
        {
            checking=true;
            try
            {
                string reason=!action.Applicable ? "Funzione non applicabile a questo sistema." : action.Unavailable==null ? null : await Task.Run(action.Unavailable);
                if (IsDisposed) return;
                run.Enabled=reason==null;
                status.Text=reason ?? "Pronto per l'esecuzione.";
                status.ForeColor=reason==null ? Theme.Muted : Theme.Warning;
                if (reason!=null) output.Text="Azione non disponibile:\r\n" + reason;
            }
            catch (Exception ex) { if (!IsDisposed) { status.Text="Verifica fallita."; output.Text=ex.Message; } }
            finally { checking=false; }
        }
        private async Task Execute()
        {
            if (running || checking) return;
            if (!confirmationGranted&&action.Confirmation!=null&&!ConfirmDialog.Ask(this,action.Title,action.Confirmation)) return;
            if (!app.Coordinator.TryEnter()) { status.Text="Un'altra operazione è in corso."; return; }
            running=true; run.Enabled=false; CloseAction.Enabled=false; progress.Visible=true;
            output.Clear(); status.Text="Operazione in corso. Attendi il completamento."; status.ForeColor=Theme.Accent;
            app.History.Add("AVVIO · " + action.Title); outputTimer.Start();
            try
            {
                await Task.Run(delegate {
                    if (!action.Applicable) throw new NotSupportedException("Funzione non applicabile a questo sistema.");
                    if (action.Unavailable!=null)
                    { string reason=action.Unavailable(); if (reason!=null) throw new InvalidOperationException(reason); }
                    if (action.RequiresElevation)
                        LocalPrivilege.Execute(action.Id,Enqueue);
                    else if (action.Id=="printer.purge") LocalPrivilege.ExecuteAudited(action,Enqueue);
                    else action.Execute(Enqueue);
                });
                if (action.Restart!=null) Enqueue(action.Restart);
                Enqueue("Operazione terminata. Leggi il riepilogo sopra per l'esito dello strumento.");
                status.Text="Esecuzione terminata · leggi il risultato.";
                app.History.Add("TERMINATA · " + action.Title);
            }
            catch (Exception ex)
            {
                Enqueue("ERRORE: " + ex.Message); status.Text="Operazione incompleta o non riuscita."; status.ForeColor=Theme.Warning;
                app.History.Add("ERRORE · " + action.Title + " · " + ex.Message);
            }
            finally
            {
                outputTimer.Stop(); FlushOutput(); running=false; app.Coordinator.Leave();
                progress.Visible=false; CloseAction.Enabled=true; run.Enabled=true;
            }
        }
        private void Enqueue(string line)
        {
            pending.Append((line??"")+Environment.NewLine);
        }
        private void FlushOutput()
        {
            string next=pending.Drain();
            if (next.Length==0) return;
            BoundedText combined=new BoundedText(); combined.Append(output.Text); combined.Append(next);
            output.Text=combined.Read();
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        { if (running || checking) e.Cancel=true; base.OnFormClosing(e); }
        protected override void Dispose(bool disposing)
        { if (disposing && outputTimer!=null) outputTimer.Dispose(); base.Dispose(disposing); }
    }
}
