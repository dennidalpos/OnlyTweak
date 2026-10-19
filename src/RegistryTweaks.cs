using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OnlyTweakWindows
{
    internal sealed class RegistryValueState
    {
        public bool KeyExists, ValueExists;
        public object Data;
        public RegistryValueKind Kind;
    }
    internal interface IRegistryReader
    {
        RegistryValueState Read(string path, string name);
    }
    internal interface IRegistryStore : IRegistryReader
    {
        void Set(string path, string name, object data, RegistryValueKind kind);
        void DeleteValue(string path, string name);
        void DeleteKeyIfEmpty(string path);
        void Notify();
    }
    internal sealed class WindowsRegistryStore : IRegistryStore
    {
        public RegistryValueState Read(string path, string name)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(path))
            {
                RegistryValueState state = new RegistryValueState { KeyExists = key != null };
                if (key == null) return state;
                foreach (string valueName in key.GetValueNames())
                {
                    if (!String.Equals(valueName, name, StringComparison.OrdinalIgnoreCase)) continue;
                    state.ValueExists = true;
                    state.Kind = key.GetValueKind(name);
                    state.Data = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    break;
                }
                return state;
            }
        }
        public void Set(string path, string name, object data, RegistryValueKind kind)
        { using (RegistryKey key = Registry.CurrentUser.CreateSubKey(path)) key.SetValue(name, data, kind); }
        public void DeleteValue(string path, string name)
        { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(path, true)) if (key != null) key.DeleteValue(name, false); }
        public void DeleteKeyIfEmpty(string path)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(path))
            { if (key == null || key.ValueCount != 0 || key.SubKeyCount != 0) return; }
            Registry.CurrentUser.DeleteSubKey(path, false);
        }
        public void Notify()
        {
            UIntPtr result;
            SendMessageTimeout(new IntPtr(0xffff), 0x001A, IntPtr.Zero, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", 2, 1000, out result);
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);
        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
    }
    internal sealed class TweakDefinition
    {
        public string Id, Title, Description, Summary, Path, ValueName, EnabledLabel, DisabledLabel;
        public object EnabledData, DisabledData;
        public bool DeleteWhenDisabled;
        public RegistryValueKind Kind;
    }
    internal sealed class TweakSnapshot
    {
        public RegistryValueState Value;
        public readonly List<string> MissingKeys = new List<string>();
    }
    internal sealed class RegistryTweakService
    {
        internal const string ExplorerPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        internal const string ClassicPath = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";
        private readonly IRegistryStore store;
        private readonly OsProfile os;
        private readonly Dictionary<string, TweakSnapshot> snapshots = new Dictionary<string, TweakSnapshot>();
        public readonly List<TweakDefinition> Definitions;
        public RegistryTweakService(IRegistryStore store) : this(store,EnvironmentInfo.Profile) { }
        public RegistryTweakService(IRegistryStore store,OsProfile os)
        {
            this.store = store; this.os=os;
            Definitions = new List<TweakDefinition> {
                new TweakDefinition { Id="classic", Title="Menu contestuale classico", Path=ClassicPath, ValueName="", Kind=RegistryValueKind.String,
                    EnabledData="", DeleteWhenDisabled=true, EnabledLabel="Attiva menu classico", DisabledLabel="Menu Windows 11",
                    Summary="Tweak non ufficiale: l'effetto dipende dalla build.",
                    Description="Tweak non ufficiale. Imposta il menu classico tramite registro; l'effetto dipende dalla build di Windows. Dopo la modifica riavvia Esplora risorse con il pulsante dedicato oppure disconnettiti. Lo stato indica il registro, non una verifica visiva del menu." },
                Explorer("extensions", "Estensioni dei file", "HideFileExt", 0, 1, "Mostra estensioni", "Nascondi estensioni", "Mostra anche le estensioni dei tipi di file conosciuti, per esempio .pdf e .exe.","Mostra estensioni come .pdf e .exe."),
                Explorer("hidden", "File e cartelle nascosti", "Hidden", 1, 2, "Mostra nascosti", "Nascondi nascosti", "Visualizza i normali elementi nascosti. I file protetti di sistema restano protetti.","Mostra gli elementi nascosti, non quelli di sistema."),
                Explorer("checkboxes", "Caselle di selezione", "AutoCheckSelect", 1, 0, "Attiva caselle", "Disattiva caselle", "Aggiunge caselle di selezione agli elementi in Esplora file.",null)
            };
        }
        public IEnumerable<TweakDefinition> AvailableDefinitions
        { get { foreach (TweakDefinition d in Definitions) if (os.SupportsTweak(d.Id)) yield return d; } }
        private void RequireApplicable(TweakDefinition definition)
        { if (!os.SupportsTweak(definition.Id)) throw new NotSupportedException("Tweak non applicabile a questo sistema operativo."); }
        private static TweakDefinition Explorer(string id, string title, string name, int enabled, int disabled, string on, string off, string description, string summary)
        { return new TweakDefinition { Id=id, Title=title, Path=ExplorerPath, ValueName=name, Kind=RegistryValueKind.DWord, EnabledData=enabled, DisabledData=disabled, EnabledLabel=on, DisabledLabel=off, Description=description, Summary=summary }; }
        public string Describe(TweakDefinition definition)
        {
            RegistryValueState value = store.Read(definition.Path, definition.ValueName);
            if (!value.ValueExists) return "Valore non impostato: comportamento predefinito di Windows.";
            if (value.Kind == definition.Kind && Object.Equals(value.Data, definition.EnabledData))
                return definition.Id == "classic" ? "Tweak classico presente nel registro; effetto visivo da verificare." : definition.EnabledLabel + ": attivo.";
            if (!definition.DeleteWhenDisabled && value.Kind == definition.Kind && Object.Equals(value.Data, definition.DisabledData))
                return definition.DisabledLabel + ": attivo.";
            return "Valore personalizzato (" + value.Kind + "): " + Convert.ToString(value.Data);
        }
        public bool CanRestore(TweakDefinition definition) { return snapshots.ContainsKey(definition.Id); }
        private void Capture(TweakDefinition definition)
        {
            if (snapshots.ContainsKey(definition.Id)) return;
            TweakSnapshot snapshot = new TweakSnapshot { Value = store.Read(definition.Path, definition.ValueName) };
            string path = definition.Path;
            while (path.Length > 0 && !store.Read(path, definition.ValueName).KeyExists)
            {
                snapshot.MissingKeys.Add(path);
                int slash = path.LastIndexOf('\\');
                path = slash < 0 ? "" : path.Substring(0, slash);
            }
            snapshots.Add(definition.Id, snapshot);
        }
        public void Apply(TweakDefinition definition, bool enabled, Action<string> log)
        {
            RequireApplicable(definition);
            // Removing a custom COM registration could break another application.
            if (!enabled && definition.DeleteWhenDisabled)
            {
                RegistryValueState current = store.Read(definition.Path, definition.ValueName);
                if (current.ValueExists && (current.Kind != definition.Kind || !Object.Equals(current.Data, definition.EnabledData)))
                    throw new InvalidOperationException("Il valore è personalizzato. Non viene cancellato: usa il ripristino della sessione, se disponibile.");
            }
            Capture(definition);
            if (!enabled && definition.DeleteWhenDisabled)
            {
                store.DeleteValue(definition.Path, definition.ValueName);
                // Preserve original and foreign keys; prune only keys created by this session.
                foreach (string missing in snapshots[definition.Id].MissingKeys) store.DeleteKeyIfEmpty(missing);
            }
            else store.Set(definition.Path, definition.ValueName, enabled ? definition.EnabledData : definition.DisabledData, definition.Kind);
            Verify(definition, enabled);
            store.Notify();
            log(Describe(definition));
            log("La modifica riguarda l'account che esegue l'app. Se necessario, riavvia Esplora risorse dal menu dedicato.");
        }
        private void Verify(TweakDefinition definition, bool enabled)
        {
            RegistryValueState current = store.Read(definition.Path, definition.ValueName);
            bool shouldExist = enabled || !definition.DeleteWhenDisabled;
            object expected = enabled ? definition.EnabledData : definition.DisabledData;
            if (current.ValueExists != shouldExist || (shouldExist && (current.Kind != definition.Kind || !Object.Equals(current.Data, expected))))
                throw new InvalidOperationException("Il valore letto dopo la modifica non corrisponde a quello richiesto.");
        }
        public void Restore(TweakDefinition definition, Action<string> log)
        {
            RequireApplicable(definition);
            TweakSnapshot snapshot;
            if (!snapshots.TryGetValue(definition.Id, out snapshot)) throw new InvalidOperationException("Nessun valore precedente salvato in questa sessione.");
            if (snapshot.Value.ValueExists)
                store.Set(definition.Path, definition.ValueName, snapshot.Value.Data, snapshot.Value.Kind);
            else
            {
                store.DeleteValue(definition.Path, definition.ValueName);
                foreach (string missing in snapshot.MissingKeys) store.DeleteKeyIfEmpty(missing);
            }
            RegistryValueState actual = store.Read(definition.Path, definition.ValueName);
            if (actual.ValueExists != snapshot.Value.ValueExists || (actual.ValueExists &&
                (actual.Kind != snapshot.Value.Kind || !ValuesEqual(actual.Data, snapshot.Value.Data))))
                throw new InvalidOperationException("Ripristino non confermato dalla rilettura del registro.");
            store.Notify();
            log("Ripristinato il valore originale di inizio sessione, compresi tipo ed eventuale assenza.");
        }
        private static bool ValuesEqual(object left, object right)
        {
            Array a = left as Array, b = right as Array;
            if (a == null || b == null) return Object.Equals(left, right);
            if (a.Length != b.Length) return false;
            for (int i=0; i<a.Length; i++) if (!Object.Equals(a.GetValue(i), b.GetValue(i))) return false;
            return true;
        }
    }
}
