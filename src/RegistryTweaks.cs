using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OnlyTweakWindows
{
    internal interface IRegistryStore : IRegistryReader
    {
        void Set(string path,string name,object data,RegistryValueKind kind);
        void DeleteValue(string path,string name);
        void DeleteKeyIfEmpty(string path);
        void Notify();
    }
    internal sealed class WindowsRegistryStore : IRegistryStore
    {
        public RegistryValueState Read(string path,string name)
        {
            using RegistryKey root=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64);
            using RegistryKey key=root.OpenSubKey(path);
            var state=new RegistryValueState {KeyExists=key!=null};
            if(key!=null && key.GetValueNames().Contains(name,StringComparer.OrdinalIgnoreCase)) {
                state.ValueExists=true;state.Kind=key.GetValueKind(name);
                state.Data=key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames);
            }
            return state;
        }
        public void Set(string path,string name,object data,RegistryValueKind kind) {
            using RegistryKey root=RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,RegistryView.Registry64);
            using RegistryKey key=root.CreateSubKey(path);key.SetValue(name,data,kind);
        }
        public void DeleteValue(string path,string name) {using RegistryKey key=Registry.CurrentUser.OpenSubKey(path,true);key?.DeleteValue(name,false);}
        public void DeleteKeyIfEmpty(string path) {
            using(RegistryKey key=Registry.CurrentUser.OpenSubKey(path)) if(key==null || key.ValueCount!=0 || key.SubKeyCount!=0) return;
            Registry.CurrentUser.DeleteSubKey(path,false);
        }
        public void Notify() {
            SendMessageTimeout(new IntPtr(0xffff),0x001a,IntPtr.Zero,RegistryTweakService.ExplorerPath,2,1000,out _);
            SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero);
        }
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr hwnd,uint msg,IntPtr wParam,string lParam,uint flags,uint timeout,out UIntPtr result);
        [DllImport("shell32.dll")] private static extern void SHChangeNotify(uint eventId,uint flags,IntPtr first,IntPtr second);
    }
    internal sealed record TweakDefinition(string Id,string Title,string Path,string Name,RegistryValueKind Kind,object Enabled,object Disabled,string On,string Off);
    internal sealed class StoredRegistryValue
    {
        public bool KeyExists {get;set;}
        public bool Exists {get;set;}
        public RegistryValueKind Kind {get;set;}
        public JsonElement Data {get;set;}
        internal static StoredRegistryValue Capture(RegistryValueState value) => new() {KeyExists=value.KeyExists,Exists=value.ValueExists,Kind=value.Kind,Data=JsonSerializer.SerializeToElement(value.Data)};
        internal object Value() => Kind switch {
            RegistryValueKind.String or RegistryValueKind.ExpandString=>Data.GetString(),
            RegistryValueKind.DWord=>Data.GetInt32(),RegistryValueKind.QWord=>Data.GetInt64(),
            RegistryValueKind.Binary or RegistryValueKind.None=>Data.GetBytesFromBase64(),
            RegistryValueKind.MultiString=>Data.EnumerateArray().Select(x=>x.GetString()).ToArray(),
            _=>throw new FormatException("Tipo registro non recuperabile.")
        };
        internal bool Matches(RegistryValueState actual) => Exists==actual.ValueExists && (!Exists || (Kind==actual.Kind && Data.GetRawText()==JsonSerializer.SerializeToElement(actual.Data).GetRawText()));
    }
    internal sealed class TweakCheckpoint
    {
        public int SchemaVersion {get;set;}=1;
        public string Machine {get;set;}
        public string Sid {get;set;}
        public string Id {get;set;}
        public string Phase {get;set;}
        public StoredRegistryValue Original {get;set;}
        public StoredRegistryValue Last {get;set;}
        public string[] CreatedKeys {get;set;}=Array.Empty<string>();
    }
    internal sealed record CheckpointEnvelope(string Content,string Sha256);
    internal sealed class RegistryTweakService
    {
        internal const string ExplorerPath=@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        internal const string ClassicPath=@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";
        private readonly IRegistryStore store;
        private readonly string folder,machine,sid;
        private readonly Action authorize;
        internal readonly TweakDefinition[] Definitions;
        internal RegistryTweakService(IRegistryStore store,OsProfile os,string folder=null,Action authorize=null,string machine=null,string sid=null)
        {
            this.store=store;this.folder=folder??Path.Combine(SecureFiles.Root,"Tweaks");
            this.authorize=authorize??ShellService.RequireDesktopIdentity;
            this.machine=machine??Environment.MachineName;this.sid=sid??WindowsIdentity.GetCurrent().User.Value;
            var definitions=new List<TweakDefinition>();
            if(os.IsWindows11) definitions.Add(new("classic","Menu contestuale classico",ClassicPath,"",RegistryValueKind.String,"",null,"Menu classico","Menu Windows 11"));
            definitions.Add(new("extensions","Estensioni dei file",ExplorerPath,"HideFileExt",RegistryValueKind.DWord,0,1,"Mostra estensioni","Nascondi estensioni"));
            definitions.Add(new("hidden","File e cartelle nascosti",ExplorerPath,"Hidden",RegistryValueKind.DWord,1,2,"Mostra nascosti","Nascondi nascosti"));
            definitions.Add(new("launch","Apertura Esplora file",ExplorerPath,"LaunchTo",RegistryValueKind.DWord,1,2,"Questo PC","Home / Accesso rapido"));
            Definitions=definitions.ToArray();
        }
        private TweakDefinition Get(string id) => Definitions.SingleOrDefault(d=>d.Id==id)??throw new ArgumentException("Tweak non disponibile.");
        private string FilePath(string id) {Get(id);return Path.Combine(folder,id+".json");}
        private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        internal bool HasCheckpoint(string id) => File.Exists(FilePath(id));
        private TweakCheckpoint Load(string id)
        {
            string path=FilePath(id);
            if(!File.Exists(path)) return null;
            CheckPlain(folder);CheckPlain(path);
            if(new FileInfo(path).Length>131072) throw new FormatException("Checkpoint troppo grande.");
            var envelope=Wire.Read<CheckpointEnvelope>(File.ReadAllText(path));
            if(envelope.Content==null || Hash(envelope.Content)!=envelope.Sha256) throw new FormatException("Checkpoint alterato.");
            var checkpoint=Wire.Read<TweakCheckpoint>(envelope.Content);
            TweakDefinition definition=Get(id);
            if(checkpoint.SchemaVersion!=1 || checkpoint.Machine!=machine || checkpoint.Sid!=sid || checkpoint.Id!=id ||
                checkpoint.Original==null || checkpoint.Last==null || checkpoint.CreatedKeys==null ||
                checkpoint.CreatedKeys.Any(k=>!definition.Path.Equals(k,StringComparison.OrdinalIgnoreCase) && !definition.Path.StartsWith(k+"\\",StringComparison.OrdinalIgnoreCase)) ||
                checkpoint.CreatedKeys.Any(k=>k.Length<"Software\\".Length)) throw new FormatException("Checkpoint estraneo o non valido.");
            if(checkpoint.Original.Exists) _=checkpoint.Original.Value();
            if(checkpoint.Phase is not ("Applied" or "Restored")) throw new InvalidOperationException("Checkpoint incompleto: recupero manuale richiesto.");
            return checkpoint;
        }
        private void Save(TweakCheckpoint checkpoint) {
            if(checkpoint.Original.Exists) _=checkpoint.Original.Value();
            string content=Wire.Write(checkpoint),envelope=Wire.Write(new CheckpointEnvelope(content,Hash(content)));
            if(Encoding.UTF8.GetByteCount(envelope)>131072) throw new IOException("Checkpoint oltre il limite: modifica bloccata.");
            string path=FilePath(checkpoint.Id);SecureFiles.AtomicWrite(path,envelope);
            if(File.ReadAllText(path)!=envelope) throw new IOException("Checkpoint non verificato dopo il salvataggio.");
        }
        private static void CheckPlain(string path) {
            for(var info=new DirectoryInfo(Path.GetDirectoryName(path));info!=null;info=info.Parent)
                if(info.Exists && (info.Attributes&FileAttributes.ReparsePoint)!=0) throw new IOException("Percorso checkpoint reindirizzato.");
            if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0) throw new IOException("Checkpoint reindirizzato.");
        }
        internal string Describe(string id)
        {
            var definition=Get(id);var current=store.Read(definition.Path,definition.Name);
            if(!current.ValueExists) return "Valore assente: comportamento predefinito di Windows.";
            if(current.Kind==definition.Kind && Equals(current.Data,definition.Enabled)) return id=="classic" ? "Tweak presente nel registro; effetto visivo da verificare." : definition.On;
            if(definition.Disabled!=null && current.Kind==definition.Kind && Equals(current.Data,definition.Disabled)) return definition.Off;
            return "Configurazione personalizzata ("+current.Kind+").";
        }
        internal string Preview(string id,bool? enabled)
        {
            var definition=Get(id);var checkpoint=Load(id);
            return "Account desktop corrente · HKCU\\"+definition.Path+"\nValore: "+(definition.Name.Length==0 ? "(Predefinito)" : definition.Name)+
                "\nStato: "+Describe(id)+"\nRichiesta: "+(enabled.HasValue ? enabled.Value ? definition.On : definition.Off : checkpoint==null ? "Nessun checkpoint disponibile" : "Ripristina precedente")+
                (id=="classic" ? "\nTweak non ufficiale, dipendente dalla build. La verifica riguarda soltanto il registro." : "")+
                "\nPotrebbe essere necessario disconnettersi. Nessun riavvio automatico di Explorer.";
        }
        internal void Apply(string id,bool? enabled,Action<string> log)
        {
            authorize();var definition=Get(id);SecureFiles.EnsureDirectory(folder);
            string leasePath=Path.Combine(folder,"checkpoint.lock");if(File.Exists(leasePath)) CheckPlain(leasePath);
            using var lease=new FileStream(leasePath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            var current=store.Read(definition.Path,definition.Name);var checkpoint=Load(id);
            if(checkpoint!=null && !checkpoint.Last.Matches(current)) throw new InvalidOperationException("Modifica esterna: operazione bloccata.");
            if(id=="classic" && current.ValueExists && (current.Kind!=RegistryValueKind.String || !Equals(current.Data,"")))
                throw new InvalidOperationException("Registrazione COM personalizzata: operazione bloccata.");
            if(enabled==null && checkpoint==null) throw new InvalidOperationException("Nessun checkpoint disponibile.");
            if(enabled.HasValue && (checkpoint==null || checkpoint.Phase=="Restored")) {
                var missing=new List<string>();
                for(string path=definition.Path;path.Contains('\\') && !store.Read(path,definition.Name).KeyExists;path=path[..path.LastIndexOf('\\')]) missing.Add(path);
                checkpoint=new TweakCheckpoint {Machine=machine,Sid=sid,Id=id,Original=StoredRegistryValue.Capture(current),CreatedKeys=missing.ToArray()};
            }
            StoredRegistryValue expected=enabled.HasValue ? StoredRegistryValue.Capture(new RegistryValueState {
                KeyExists=true,ValueExists=enabled.Value || definition.Disabled!=null,Kind=definition.Kind,Data=enabled.Value ? definition.Enabled : definition.Disabled}) : checkpoint.Original;
            checkpoint.Phase="Pending";checkpoint.Last=expected;Save(checkpoint); // Recovery must exist before writing HKCU.
            authorize(); // Recheck desktop ownership immediately before the write.
            if(!StoredRegistryValue.Capture(current).Matches(store.Read(definition.Path,definition.Name))) throw new InvalidOperationException("Modifica concorrente del registro.");
            if(expected.Exists) store.Set(definition.Path,definition.Name,expected.Value(),expected.Kind);
            else store.DeleteValue(definition.Path,definition.Name);
            if(!expected.Matches(store.Read(definition.Path,definition.Name))) throw new IOException("Rilettura del registro divergente.");
            if(!expected.Exists) {
                var others=Definitions.Where(d=>d.Id!=id).Select(d=>Load(d.Id)).Where(c=>c!=null).ToArray();
                foreach(string key in checkpoint.CreatedKeys.Concat(others.SelectMany(c=>c.CreatedKeys)).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(k=>k.Length)) {
                    bool shared=others.Any(c=>c.Phase=="Applied" && c.Last.Exists && Get(c.Id).Path.StartsWith(key,StringComparison.OrdinalIgnoreCase));
                    if(!shared) store.DeleteKeyIfEmpty(key);
                }
                foreach(var prior in others) {
                    var remaining=prior.CreatedKeys.Where(k=>store.Read(k,"").KeyExists).ToArray();
                    if(remaining.Length!=prior.CreatedKeys.Length) {prior.CreatedKeys=remaining;Save(prior);}
                }
                checkpoint.CreatedKeys=checkpoint.CreatedKeys.Where(k=>store.Read(k,"").KeyExists).ToArray();
            }
            checkpoint.Phase=enabled.HasValue ? "Applied" : "Restored";Save(checkpoint);store.Notify();
            log(Describe(id));log("Configurazione riletta. Effetto visivo da verificare; se necessario disconnettiti.");
        }
        internal ActionSpec Action(string id,bool? enabled)
        {
            var definition=Get(id);string title=enabled.HasValue ? enabled.Value ? definition.On : definition.Off : "Ripristina precedente";
            var action=new ActionSpec("tweak."+id+"."+(enabled?.ToString()??"restore"),title,Preview(id,enabled),"Utente desktop corrente · HKCU",log=>Apply(id,enabled,log)) {Confirmation=Preview(id,enabled)};
            action.Observe=(log,token)=> {
                var value=store.Read(definition.Path,definition.Name);var checkpoint=Load(id);
                bool? satisfied=checkpoint==null ? null : checkpoint.Last.Matches(value);
                return new LocalObservation(true,satisfied,StoredRegistryValue.Capture(value));
            };
            return action;
        }
    }
    internal sealed class TweakPage : DarkPage
    {
        private readonly List<Action> refreshers=new();
        internal override void ResumePage() {foreach(var refresh in refreshers) refresh();}
        internal TweakPage(AppServices app) : base("Tweak Windows","Personalizzazioni dell'utente desktop corrente, con recupero persistente.",Glyphs.Settings,null)
        {
            StackPanel stack=new();Content.Controls.Add(stack);
            foreach(var definition in app.Tweaks.Definitions) {
                stack.Controls.Add(Theme.Section(definition.Title,Glyphs.Settings));
                Label state=Theme.Label("",Theme.Muted);stack.Controls.Add(state);
                void Refresh() {try {state.Text=app.Tweaks.Describe(definition.Id);} catch(Exception ex) {state.Text="Stato sconosciuto: "+ex.Message;}}
                refreshers.Add(Refresh);Refresh();
                if(definition.Id=="classic") stack.Controls.Add(Theme.Label("Tweak non ufficiale: l'effetto dipende dalla build Windows 11.",Theme.Warning));
                foreach(bool? enabled in new bool?[] {true,false,null}) {
                    bool? choice=enabled;string title=choice.HasValue ? choice.Value ? definition.On : definition.Off : "Ripristina precedente";
                    stack.Controls.Add(Theme.Button(title,async delegate {
                        try {await OperationDialog.Open(this,app,app.Tweaks.Action(definition.Id,choice));}
                        catch(Exception ex) {SetFeedback(ex.Message);}
                    }));
                }

            }
            Footer.Controls.Add(Theme.Button("Aggiorna stato",delegate {ResumePage();}));
        }
    }
}
