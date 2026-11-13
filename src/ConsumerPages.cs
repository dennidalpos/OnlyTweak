using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace OnlyTweakWindows
{
    internal sealed record GuidedTweak(string Id,string Title,int Level,string Target,string Description,string Source,int MinimumBuild=17763)
    {
        internal string State=>"Da completare nelle impostazioni Windows";
    }
    internal static class ConsumerCatalog
    {
        internal const string Performance="https://support.microsoft.com/en-us/windows/experience/performance-optimization/tips-to-improve-pc-performance-in-windows";
        internal const string Graphics="https://support.microsoft.com/en-us/windows/hardware/display-graphics/optimizations-for-windowed-games-in-windows-11";
        internal static readonly GuidedTweak[] Gaming={
            new("game.mode","Modalità gioco",1,"ms-settings:gaming-gamemode","Controlla Modalità gioco. Confronta il comportamento nei giochi che usi.",Performance),
            new("game.capture","Registrazione in background",1,"ms-settings:gaming-gamedvr","Disattivala in Windows soltanto se non usi la cattura delle partite.",Performance),
            new("game.refresh","Frequenza del monitor",1,"ms-settings:display-advanced","Scegli una frequenza supportata dal monitor e dal collegamento.",Performance),
            new("game.drivers","Driver e aggiornamenti",1,"ms-settings:windowsupdate","Verifica Windows Update e il sito del produttore. Nessun driver viene scaricato dall'app.",Performance),
            new("game.startup","App in avvio",2,"ms-settings:startupapps","Rivedi le app singolarmente; conserva antivirus, driver e componenti che ti servono.",Performance),
            new("game.gpu","Preferenza GPU per gioco",3,"ms-settings:display-advancedgraphics","Scegli il gioco nelle impostazioni Windows e confronta la GPU assegnata.",Graphics,17134),
            new("game.windowed","Ottimizzazioni in finestra",3,"ms-settings:display-advancedgraphics","Windows 11: opzione per giochi DirectX 10/11 in finestra o senza bordi. Non vale per tutti i giochi.",Graphics,22000),
            new("game.hags","Pianificazione GPU (HAGS)",3,"ms-settings:display-advancedgraphics","Opzione dipendente da GPU e driver: se presente, prova un cambiamento alla volta. Può richiedere riavvio.",Graphics,19041),
            new("game.vrr","Frequenza variabile (VRR)",3,"ms-settings:display-advancedgraphics","Verifica disponibilità e compatibilità di monitor, GPU e driver. Non forzare impostazioni assenti.",Graphics,18362)
        };
        internal static readonly GuidedTweak[] Convenience={
            new("comfort.clipboard","Cronologia appunti",0,"ms-settings:clipboard","Attivala se utile. La sincronizzazione cloud resta una scelta separata in Windows.","https://support.microsoft.com/en-us/windows/apps/using-the-clipboard"),
            new("comfort.suggestions","Suggerimenti di Windows",0,"ms-settings:notifications","Rivedi suggerimenti e messaggi aggiuntivi; la loro posizione varia fra build Windows.",Performance),
            new("comfort.notifications","Notifiche delle app",0,"ms-settings:notifications","Scegli quali app possono interromperti, senza disattivare indiscriminatamente gli avvisi.",Performance)
        };
        internal static void AddGuided(StackPanel stack,DarkPage page,AppServices app,GuidedTweak item) {
            stack.Controls.Add(Theme.Section(item.Title,Glyphs.Settings));
            stack.Controls.Add(Theme.Label(item.Description,Theme.Text));
            stack.Controls.Add(Theme.Label("Guidata · utente corrente · disponibilità da verificare in Windows",Theme.Muted));
            var state=Theme.Label(item.State,Theme.Muted);stack.Controls.Add(state);
            var button=Theme.Button("Apri impostazioni",async delegate {
                var action=app.Catalog.Settings(item.Id,item.Title,item.Description,item.Target);
                await ActionDispatcher.Invoke(page,app,action,text=>state.Text=text+"\n"+item.State);
            });
            button.Enabled=app.OS.Build>=item.MinimumBuild && app.Availability.Target(item.Target);
            if(!button.Enabled) state.Text="Non disponibile su questa versione o installazione Windows.";
            stack.Controls.Add(button);
            stack.Controls.Add(Theme.Button("Fonte e dettagli",delegate {
                page.Navigation.Push(new TextDialog(item.Title,item.Description+"\n\nFonte Microsoft:\n"+item.Source+"\n\n"+item.State));
            }));
        }
    }
    internal sealed class ConveniencePage : DarkPage
    {
        internal ConveniencePage(AppServices app) : base("Comodità","Impostazioni personali e sessioni temporanee.",Glyphs.Settings,null) {
            var stack=new StackPanel();Content.Controls.Add(stack);
            stack.Controls.Add(Theme.Button("Mantieni sveglio",delegate {Navigation.Push(new AwakePage(app));}));
            foreach(var item in ConsumerCatalog.Convenience) ConsumerCatalog.AddGuided(stack,this,app,item);
        }
    }
    internal sealed class GamingPage : DarkPage
    {
        private readonly AppServices app;
        private readonly StackPanel sections=new();
        private readonly List<Action> refreshers=new();
        internal override void ResumePage() {foreach(var refresh in refreshers) refresh();}
        internal GamingPage(AppServices app) : base("Gaming","Tre livelli progressivi: scegli ogni opzione, confronta un cambiamento alla volta.",Glyphs.Settings,null) {
            this.app=app;Content.Controls.Add(sections);
            sections.Controls.Add(Theme.Label("Nessun aumento FPS garantito. Defender, firewall, aggiornamenti e virtualizzazione restano invariati.",Theme.Muted));
            var levels=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat,BackColor=Theme.Surface,ForeColor=Theme.Text,AccessibleName="Livello gaming"};
            levels.Items.AddRange(new[]{"Base","Bilanciato · include Base","Avanzato · include Base e Bilanciato"});
            sections.Controls.Add(levels);
            levels.SelectedIndexChanged+=delegate {ShowLevel(levels.SelectedIndex+1);};levels.SelectedIndex=0;
        }
        private void ShowLevel(int level) {
            refreshers.Clear();
            foreach(Control child in sections.Controls.Cast<Control>().Skip(2).ToArray()) {sections.Controls.Remove(child);child.Dispose();}
            foreach(var item in ConsumerCatalog.Gaming.Where(i=>i.Level<=level)) ConsumerCatalog.AddGuided(sections,this,app,item);
            if(level>=2) {
                sections.Controls.Add(Theme.Section("Animazioni Windows",Glyphs.Settings));
                sections.Controls.Add(Theme.Label("Automatica · utente corrente · rilettura API Windows · nessun riavvio\nRiduce animazioni di menu, elenchi e area client. Fonte: SystemParametersInfo (Microsoft).",Theme.Muted));
                AddPreference("animations",()=>"reduce");
                sections.Controls.Add(Theme.Section("Piano energetico",Glyphs.Settings));
                sections.Controls.Add(Theme.Label("Automatica · piano già disponibile · consumo, calore e rumore possono aumentare\nFonte: API Windows Power Management (Microsoft).",Theme.Muted));
                var plans=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat,BackColor=Theme.Surface,ForeColor=Theme.Text,AccessibleName="Piano energetico"};
                try {var available=app.Preferences.Plans();plans.Items.AddRange(available);if(available.Length>0) plans.SelectedIndex=Math.Max(0,Array.FindIndex(available,p=>p.Id==app.Preferences.CurrentPower));}
                catch(Exception ex) {sections.Controls.Add(Theme.Label("Elenco piani non disponibile: "+ex.Message,Theme.Warning));}
                sections.Controls.Add(plans);AddPreference("power",()=> (plans.SelectedItem as PowerPlan)?.Id);
            }
            if(level==3) sections.Controls.Add(Theme.Label("Confronto: stesso gioco, scena e impostazioni. Annota fluidità, temperature e problemi prima/dopo; ripristina se peggiora. L'app non misura FPS.",Theme.Muted));
        }
        private void AddPreference(string id,Func<string> choice) {
            var status=Theme.Label("",Theme.Muted);sections.Controls.Add(status);
            void Refresh() {try {status.Text="Stato: "+app.Preferences.Describe(id);} catch(Exception ex) {status.Text="Stato sconosciuto: "+ex.Message;}}
            refreshers.Add(Refresh);
            Refresh();
            sections.Controls.Add(Theme.Button("Fonte e dettagli",delegate {
                Navigation.Push(new TextDialog(id=="power" ? "Piano energetico" : "Animazioni Windows",
                    "API Microsoft utilizzata per lettura e modifica:\n"+(id=="power" ?
                    "https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powersetactivescheme" :
                    "https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow")+
                    "\n\nCheckpoint persistente dello stato precedente. Nessuna modifica ai parametri interni dei piani energetici."));
            }));
            foreach(bool restore in new[]{false,true}) {
                bool undo=restore;sections.Controls.Add(Theme.Button(undo ? "Ripristina precedente" : "Applica",async delegate {
                    try {
                        string value=undo ? null : choice();if(!undo && value==null) throw new InvalidOperationException("Seleziona un piano disponibile.");
                        await OperationDialog.Open(this,app,app.Preferences.Action(id,value));Refresh();
                    } catch(Exception ex) {SetFeedback(ex.Message);}
                }));
            }
        }
    }
    internal sealed class AwakePage : DarkPage
    {
        private readonly System.Windows.Forms.Timer timer=new() {Interval=1000};
        internal AwakePage(AppServices app) : base("Mantieni sveglio","Sessione temporanea: termina alla scadenza o alla chiusura dell'app.",Glyphs.Settings,null) {
            var stack=new StackPanel();Content.Controls.Add(stack);
            stack.Controls.Add(Theme.Label("Utente corrente · senza elevazione · nessun piano energetico modificato\nFonte: Microsoft SetThreadExecutionState / PowerToys Awake.",Theme.Muted));
            var duration=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat,BackColor=Theme.Surface,ForeColor=Theme.Text,AccessibleName="Durata mantieni sveglio"};
            duration.Items.AddRange(new[]{"15 minuti","30 minuti","1 ora","2 ore","4 ore","8 ore"});duration.SelectedIndex=2;
            var display=new CheckBox {Text="Mantieni acceso anche lo schermo",AutoSize=true,ForeColor=Theme.Text};
            var state=Theme.Label("",Theme.Muted);stack.Controls.Add(state);stack.Controls.Add(duration);stack.Controls.Add(display);
            void Refresh() {state.Text=app.Awake.Error!=null ? "Errore: "+app.Awake.Error : app.Awake.Active ? "Attivo fino alle "+app.Awake.Expires?.ToLocalTime().ToString("HH:mm") : "Inattivo";}
            Refresh();timer.Tick+=delegate {Refresh();};timer.Start();
            foreach(bool start in new[]{true,false}) {
                bool enable=start;stack.Controls.Add(Theme.Button(enable ? "Avvia" : "Termina",async delegate {
                    var time=TimeSpan.FromMinutes(new[]{15,30,60,120,240,480}[duration.SelectedIndex]);bool screen=display.Checked;
                    var action=new ActionSpec(enable ? "awake.start" : "awake.stop",enable ? "Avvia mantieni sveglio" : "Termina mantieni sveglio",
                        "Sessione temporanea senza modifiche permanenti.","Utente desktop corrente",log=> {if(enable) app.Awake.Start(time,screen);else app.Awake.Stop();log("Sessione aggiornata.");}) {
                        Confirmation=enable ? "Durata: "+time.TotalMinutes+" minuti. Schermo acceso: "+(screen ? "sì" : "no")+". Sostituisce la sessione attiva e può aumentare i consumi." : "Termina la sessione mantieni sveglio dell'app.",
                        Observe=(_,_)=>new LocalObservation(true,app.Awake.Active==enable,new {app.Awake.Active,app.Awake.Expires,app.Awake.Error})
                    };
                    await OperationDialog.Open(this,app,action);Refresh();
                }));
            }
        }
        protected override void Dispose(bool disposing) {if(disposing) timer.Dispose();base.Dispose(disposing);}
    }
    internal sealed class AdvancedPage : DarkPage
    {
        internal AdvancedPage(AppServices app) : base("Strumenti avanzati","Operazioni locali, accesso remoto in ingresso e informazioni sulle protezioni.",Glyphs.Settings,null) {
            var stack=new StackPanel();Content.Controls.Add(stack);
            stack.Controls.Add(Theme.Button("PowerShell Remoting / WinRM",delegate {Navigation.Push(new RemotingPage(app));}));
            if(app.Security.AvailableDefinitions(app.Availability).Any()) stack.Controls.Add(Theme.Button("Compatibilità e protezioni",delegate {Navigation.Push(new SecurityDialog(app));}));
            MenuDialog.AddLocalActions(stack,this,app,app.Catalog.Tools);
        }
    }
}
