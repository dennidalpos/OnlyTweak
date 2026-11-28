using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace OnlyTweakWindows
{
    internal sealed record PowerPlan(string Id,string Name) {public override string ToString()=>Name;}
    internal interface IPreferenceBackend {
        string Read(string key);
        void Write(string key,string value);
        PowerPlan[] Plans();
    }
    internal sealed class WindowsPreferenceBackend : IPreferenceBackend
    {
        private static readonly Dictionary<string,(uint Get,uint Set)> Animation=new() {
            ["client"]=(0x1042,0x1043),["menu"]=(0x1002,0x1003),["combo"]=(0x1004,0x1005)
        };
        public string Read(string key) {
            if(key=="power") {
                uint code=PowerGetActiveScheme(IntPtr.Zero,out IntPtr pointer);if(code!=0) throw new Win32Exception((int)code);
                try {return Marshal.PtrToStructure<Guid>(pointer).ToString();} finally {LocalFree(pointer);}
            }
            if(!Animation.TryGetValue(key,out var api)) throw new ArgumentException("Preferenza non consentita.");
            int value=0;if(!GetParameter(api.Get,0,ref value,0)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return value==0 ? "0" : "1";
        }
        public void Write(string key,string value) {
            if(key=="power") {
                if(!Guid.TryParse(value,out Guid id) || !Plans().Any(p=>p.Id==id.ToString())) throw new ArgumentException("Piano energetico non disponibile.");
                uint code=PowerSetActiveScheme(IntPtr.Zero,ref id);if(code!=0) throw new Win32Exception((int)code);return;
            }
            if(!Animation.TryGetValue(key,out var api) || value is not ("0" or "1")) throw new ArgumentException("Preferenza non consentita.");
            if(!SetParameter(api.Set,0,value=="1" ? new IntPtr(1) : IntPtr.Zero,3)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        public PowerPlan[] Plans() {
            var plans=new List<PowerPlan>();
            for(uint index=0;index<256;index++) {
                uint size=16;byte[] bytes=new byte[16];uint code=PowerEnumerate(IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,16,index,bytes,ref size);
                if(code==259) return plans.ToArray();if(code!=0 || size!=16) throw new Win32Exception((int)code);
                Guid id=new Guid(bytes);uint length=0;
                code=PowerReadFriendlyName(IntPtr.Zero,ref id,IntPtr.Zero,IntPtr.Zero,null,ref length);
                if((code!=0 && code!=234) || length==0 || length>4096) throw new InvalidOperationException("Nome piano energetico non leggibile.");
                byte[] name=new byte[length];code=PowerReadFriendlyName(IntPtr.Zero,ref id,IntPtr.Zero,IntPtr.Zero,name,ref length);
                if(code!=0) throw new Win32Exception((int)code);
                plans.Add(new PowerPlan(id.ToString(),Encoding.Unicode.GetString(name).TrimEnd('\0')));
            }
            throw new InvalidOperationException("Troppi piani energetici.");
        }
        [DllImport("user32.dll",EntryPoint="SystemParametersInfoW",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetParameter(uint action,uint param,ref int value,uint flags);
        [DllImport("user32.dll",EntryPoint="SystemParametersInfoW",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetParameter(uint action,uint param,IntPtr value,uint flags);
        [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root,out IntPtr scheme);
        [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root,ref Guid scheme);
        [DllImport("powrprof.dll")] private static extern uint PowerEnumerate(IntPtr root,IntPtr scheme,IntPtr group,uint access,uint index,byte[] buffer,ref uint size);
        [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr root,ref Guid scheme,IntPtr group,IntPtr setting,byte[] buffer,ref uint size);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
    }
    internal sealed class PreferenceCheckpoint {
        public string Machine {get;set;}
        public string Sid {get;set;}
        public string Id {get;set;}
        public string Phase {get;set;}
        public Dictionary<string,string> Original {get;set;}
        public Dictionary<string,string> Last {get;set;}
    }
    internal sealed class UserPreferences
    {
        private readonly IPreferenceBackend backend;
        private readonly string folder,machine,sid;
        private readonly Action authorize;
        internal UserPreferences(IPreferenceBackend backend,string folder=null,Action authorize=null,string machine=null,string sid=null) {
            this.backend=backend;this.folder=folder??Path.Combine(SecureFiles.Root,"Preferences");
            this.authorize=authorize??ShellService.RequireDesktopIdentity;this.machine=machine??Environment.MachineName;
            this.sid=sid??WindowsIdentity.GetCurrent().User.Value;
        }
        internal PowerPlan[] Plans()=>backend.Plans();
        internal string CurrentPower=>backend.Read("power");
        private static string[] Keys(string id)=>id switch {"animations"=>new[]{"client","menu","combo"},"power"=>new[]{"power"},_=>throw new ArgumentException("Preferenza non consentita.")};
        private string FilePath(string id) {Keys(id);return Path.Combine(folder,id+".json");}
        private static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        private PreferenceCheckpoint Load(string id) {
            string path=FilePath(id);if(!File.Exists(path)) return null;
            SecureFiles.AssertPlain(path);
            if(new FileInfo(path).Length>65536) throw new FormatException("Checkpoint troppo grande.");
            var envelope=Wire.Read<CheckpointEnvelope>(File.ReadAllText(path));
            if(envelope.Content==null || envelope.Sha256!=Hash(envelope.Content)) throw new FormatException("Checkpoint alterato.");
            var value=Wire.Read<PreferenceCheckpoint>(envelope.Content);string[] keys=Keys(id);
            if(value.Machine!=machine || value.Sid!=sid || value.Id!=id || value.Original==null || value.Last==null ||
                !value.Original.Keys.Order().SequenceEqual(keys.Order()) || !value.Last.Keys.Order().SequenceEqual(keys.Order())) throw new FormatException("Checkpoint estraneo.");
            if(value.Phase is not ("Applied" or "Restored")) throw new InvalidOperationException("Checkpoint incompleto: recupero manuale richiesto.");
            return value;
        }
        private void Save(PreferenceCheckpoint value) {
            string json=Wire.Write(value);SecureFiles.AtomicWrite(FilePath(value.Id),Wire.Write(new CheckpointEnvelope(json,Hash(json))));
        }
        internal string Describe(string id) {
            Keys(id);
            if(id=="power") {string current=backend.Read("power");return Plans().FirstOrDefault(p=>p.Id==current)?.Name??"Piano corrente non presente nell'elenco disponibile";}
            var names=new Dictionary<string,string> {["client"]="Area client",["menu"]="Menu",["combo"]="Elenchi"};
            return String.Join(" · ",Keys(id).Select(k=>names[k]+": "+(backend.Read(k)=="1" ? "animazioni attive" : "animazioni ridotte")));
        }
        internal void Apply(string id,string choice,Action<string> log) {
            authorize();SecureFiles.EnsureDirectory(folder);
            string lease=Path.Combine(folder,"checkpoint.lock");if(File.Exists(lease)) SecureFiles.AssertPlain(lease);
            using var guard=new FileStream(lease,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            var current=Keys(id).ToDictionary(k=>k,backend.Read);var prior=Load(id);
            if(prior!=null && prior.Last.Any(v=>current[v.Key]!=v.Value)) throw new InvalidOperationException("Modifica esterna: ripristino bloccato.");
            if(choice==null && prior==null) throw new InvalidOperationException("Nessun checkpoint disponibile.");
            Dictionary<string,string> desired;
            if(choice==null) desired=new(prior.Original);
            else if(id=="animations" && choice=="reduce") desired=Keys(id).ToDictionary(k=>k,_=>"0");
            else if(id=="power" && Plans().Any(p=>p.Id==choice)) desired=new() {["power"]=choice};
            else throw new ArgumentException("Scelta non consentita.");
            var checkpoint=prior??new PreferenceCheckpoint {Machine=machine,Sid=sid,Id=id,Original=current};
            checkpoint.Last=desired;checkpoint.Phase="Applying";Save(checkpoint);
            foreach(var value in desired) {backend.Write(value.Key,value.Value);if(backend.Read(value.Key)!=value.Value) throw new IOException("Rilettura non coerente.");}
            checkpoint.Phase=choice==null ? "Restored" : "Applied";Save(checkpoint);log(Describe(id));
        }
        internal ActionSpec Action(string id,string choice) {
            Keys(id);string title=choice==null ? "Ripristina precedente" : id=="power" ? "Applica piano selezionato" : "Applica riduzione animazioni";
            var action=new ActionSpec("preference."+id,title,"Preferenza locale reversibile.","Utente desktop corrente",log=>Apply(id,choice,log)) {
                Confirmation=title+"\nStato: "+Describe(id)+"\nScelta: "+(choice??"checkpoint precedente")+"\nIl piano energetico può aumentare consumo, calore e rumore. Nessun riavvio automatico."
            };
            action.Observe=(_,_)=> {
                var current=Keys(id).ToDictionary(k=>k,backend.Read);var checkpoint=Load(id);
                return new LocalObservation(true,checkpoint==null ? null : checkpoint.Last.All(v=>current[v.Key]==v.Value),current);
            };return action;
        }
    }
}
