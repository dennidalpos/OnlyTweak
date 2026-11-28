using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OnlyTweakWindows
{
    // Valori modificabili per una funzione abilitabile. Ogni valore è validato: il worker elevato riceve solo
    // un suffisso a grammatica chiusa sull'identificativo dell'azione, mai comandi o percorsi liberi.
    internal sealed record RemotingSettings
    {
        internal const int DefaultPort=5985,DefaultIdleMinutes=0;
        internal const string DefaultName="OnlyTweak.Local";
        public int Port {get;init;}=DefaultPort;
        // Vuoto = sottorete privata della scheda scelta. Altrimenti indirizzi IPv4 privati o CIDR con prefisso 8-32.
        public string[] Remote {get;init;}=Array.Empty<string>();
        // "admins" = amministratori locali; "rmu" = amministratori e gruppo Remote Management Users.
        public string Who {get;init;}="admins";
        public string EndpointName {get;init;}=DefaultName;
        // Minuti di inattività prima della chiusura della sessione remota; 0 = predefinito di Windows.
        public int IdleMinutes {get;init;}=DefaultIdleMinutes;
        private static readonly Regex Name=new(@"^[A-Za-z][A-Za-z0-9._-]{2,31}$",RegexOptions.CultureInvariant);
        internal bool IsDefault => Query().Length==0;
        internal static string NormalizeRemote(string text)
        {
            string[] parts=(text??"").Trim().Split('/');
            if(parts.Length>2 || !Regex.IsMatch(parts[0],@"^\d{1,3}(\.\d{1,3}){3}$") || !IPAddress.TryParse(parts[0],out IPAddress ip) || ip.AddressFamily!=AddressFamily.InterNetwork)
                throw new ArgumentException("Indirizzo remoto non valido: usa IPv4 o CIDR, per esempio 192.168.1.20 o 192.168.1.0/24.");
            byte[] b=ip.GetAddressBytes();
            bool isPrivate=b[0]==10 || b[0]==172 && b[1]>=16 && b[1]<=31 || b[0]==192 && b[1]==168;
            if(!isPrivate) throw new ArgumentException("Sono ammessi solo indirizzi IPv4 privati (10.x, 172.16-31.x, 192.168.x): "+parts[0]+".");
            int length=32;
            if(parts.Length==2 && (!Int32.TryParse(parts[1],System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out length) || length<8 || length>32))
                throw new ArgumentException("Prefisso non valido in "+text.Trim()+": usa un valore da 8 a 32.");
            return ip+"/"+length;
        }
        internal static string[] ParseRemote(string text)
        {
            var items=(text??"").Split(new[]{',',';',' ','\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(NormalizeRemote).Distinct().OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            if(items.Length>8) throw new ArgumentException("Al massimo 8 indirizzi o reti remote.");
            return items;
        }
        internal RemotingSettings Validated()
        {
            if(Port<1024 || Port>65535) throw new ArgumentException("Porta non valida: usa un valore da 1024 a 65535.");
            if(Who is not ("admins" or "rmu")) throw new ArgumentException("Utenti ammessi non validi.");
            if(EndpointName==null || !Name.IsMatch(EndpointName)) throw new ArgumentException("Nome endpoint non valido: 3-32 caratteri, inizia con una lettera, ammessi lettere, cifre, punto, trattino e underscore.");
            if(IdleMinutes!=0 && (IdleMinutes<5 || IdleMinutes>1440)) throw new ArgumentException("Timeout di inattività non valido: da 5 a 1440 minuti, oppure 0 per il predefinito di Windows.");
            string[] remote=(Remote??Array.Empty<string>()).Select(NormalizeRemote).Distinct().OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            if(remote.Length>8) throw new ArgumentException("Al massimo 8 indirizzi o reti remote.");
            return this with {Remote=remote};
        }
        // Suffisso dell'identificativo: solo i valori diversi dal predefinito, in ordine fisso.
        internal string Query()
        {
            var parts=new List<string>();
            if(Port!=DefaultPort) parts.Add("port="+Port);
            if(Remote!=null && Remote.Length>0) parts.Add("remote="+String.Join(",",Remote));
            if(Who!="admins") parts.Add("who="+Who);
            if(EndpointName!=DefaultName) parts.Add("name="+EndpointName);
            if(IdleMinutes!=0) parts.Add("idle="+IdleMinutes*60);
            return String.Join("&",parts);
        }
        internal static RemotingSettings FromQuery(string query)
        {
            if(String.IsNullOrEmpty(query)) return new RemotingSettings();
            if(query.Length>512 || !Regex.IsMatch(query,@"^[A-Za-z0-9._,/=&-]+$")) throw new ArgumentException("Azione WinRM non consentita.");
            var seen=new HashSet<string>();RemotingSettings value=new();
            foreach(string pair in query.Split('&')) {
                string[] kv=pair.Split('=');
                if(kv.Length!=2 || !seen.Add(kv[0])) throw new ArgumentException("Azione WinRM non consentita.");
                value=kv[0] switch {
                    "port"=>value with {Port=Int32.TryParse(kv[1],out int p) ? p : 0},
                    "who"=>value with {Who=kv[1]},
                    "name"=>value with {EndpointName=kv[1]},
                    "idle"=>value with {IdleMinutes=Int32.TryParse(kv[1],out int s) && s%60==0 ? s/60 : -1},
                    "remote"=>value with {Remote=ParseRemote(kv[1]).DefaultIfEmpty().Where(x=>x!=null).ToArray() is {Length:>0} items ? items : throw new ArgumentException("Elenco indirizzi remoti vuoto.")},
                    _=>throw new ArgumentException("Azione WinRM non consentita.")};
            }
            return value.Validated();
        }
        internal string Describe()
        {
            var lines=new List<string>{"Porta TCP: "+Port,"Indirizzi remoti: "+(Remote.Length==0 ? "sottorete privata della scheda" : String.Join(", ",Remote)),
                "Utenti ammessi: "+(Who=="rmu" ? "amministratori locali e Remote Management Users" : "amministratori locali"),"Endpoint: "+EndpointName,
                "Timeout di inattività: "+(IdleMinutes==0 ? "predefinito di Windows" : IdleMinutes+" minuti")};
            return String.Join("\n",lines);
        }
    }
    internal sealed record AwakeSettings
    {
        public int Minutes {get;init;}=60;
        public bool KeepScreen {get;init;}
    }
    // Impostazioni dell'utente corrente in %LocalAppData%\OnlyTweak\settings.json. Un file illeggibile o alterato
    // non blocca l'app: si ripartono i valori predefiniti e l'errore resta segnalato alla pagina.
    internal sealed class UserSettingsStore
    {
        private sealed record Document(int SchemaVersion,RemotingSettings Remoting,AwakeSettings Awake);
        private readonly string path;
        internal string LoadError {get;private set;}
        internal UserSettingsStore(string root=null) {path=Path.Combine(root??SecureFiles.Root,"settings.json");}
        private Document Read()
        {
            LoadError=null;
            try {
                if(!File.Exists(path)) return new Document(1,new RemotingSettings(),new AwakeSettings());
                SecureFiles.AssertPlain(path);
                if(new FileInfo(path).Length>16384) throw new FormatException("File impostazioni troppo grande.");
                var document=Wire.Read<Document>(File.ReadAllText(path,Encoding.UTF8));
                if(document.SchemaVersion!=1) throw new FormatException("Versione impostazioni non supportata.");
                return new Document(1,(document.Remoting??new RemotingSettings()).Validated(),Clamp(document.Awake??new AwakeSettings()));
            } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or FormatException or JsonException or ArgumentException) {
                LoadError="Impostazioni salvate non leggibili ("+ex.Message+"): usati i valori predefiniti.";
                return new Document(1,new RemotingSettings(),new AwakeSettings());
            }
        }
        private static AwakeSettings Clamp(AwakeSettings value) => value with {Minutes=Math.Clamp(value.Minutes,1,1440)};
        private void Write(Document document) {SecureFiles.AtomicWrite(path,Wire.Write(document));}
        internal RemotingSettings Remoting() => Read().Remoting;
        internal AwakeSettings Awake() => Read().Awake;
        internal void Save(RemotingSettings settings) {var current=Read();Write(current with {Remoting=settings.Validated()});}
        internal void Save(AwakeSettings settings) {var current=Read();Write(current with {Awake=Clamp(settings)});}
    }
}
