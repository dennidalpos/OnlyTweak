using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OnlyTweakWindows
{
    internal static class ActionDispatcher
    {
        public static async Task Invoke(IWin32Window owner, AppServices app, ActionSpec action, Action<string> status)
        {
            if (!action.OpensWindow) { OperationDialog.Open(owner,app,action); return; }
            await Launch(app,action,status);
        }
        internal static async Task Launch(AppServices app,ActionSpec action,Action<string> status)
        {
            if (action.Confirmation!=null) throw new InvalidOperationException("Un'azione con conferma deve usare il dialogo operativo.");
            if (!app.Coordinator.TryEnter()) { status("Attendi il completamento dell'operazione in corso."); return; }
            status((action.OpensWindow ? "Apertura: " : "Esecuzione: ") + action.Title + "…"); app.History.Add("AVVIO · " + action.Title);
            try
            {
                await Task.Run(delegate {
                    if (!action.Applicable) throw new NotSupportedException("Funzione non applicabile a questo sistema.");
                    if (action.Unavailable!=null) { string reason=action.Unavailable(); if (reason!=null) throw new InvalidOperationException(reason); }
                    action.Execute(delegate(string line) { app.History.Add(line); });
                });
                status((action.OpensWindow ? "Aperto: " : "Completato: ") + action.Title + ".");
            }
            catch (Exception ex) { app.History.Add("ERRORE · " + action.Title + " · " + ex.Message); status("Operazione non riuscita: " + ex.Message); }
            finally { app.Coordinator.Leave(); }
        }
    }
}
