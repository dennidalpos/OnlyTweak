using System;
using System.Collections.Generic;
using System.IO;
using System.ServiceProcess;
using Microsoft.Win32;

namespace OnlyTweakWindows
{
    internal interface ISpoolBackend
    {
        string StandardDirectory { get; }
        IEnumerable<string> ConfiguredDirectories();
        bool DirectoryIsPlain(string path);
        IEnumerable<string> Files(string directory);
        bool FileIsPlain(string path);
        void DeleteFile(string path);
        void Stop();
        void Start();
        string Status();
    }
    internal sealed class SpoolService
    {
        private readonly ISpoolBackend backend;
        public SpoolService(ISpoolBackend backend) { this.backend = backend; }
        public string Status() { return backend.Status(); }
        public string CleanupUnavailable()
        { try { Validate(); return null; } catch (Exception ex) { return ex.Message; } }
        private string Validate()
        {
            string standard = Path.GetFullPath(backend.StandardDirectory).TrimEnd(Path.DirectorySeparatorChar);
            foreach (string configured in backend.ConfiguredDirectories())
            {
                if (String.IsNullOrWhiteSpace(configured) || !Path.IsPathRooted(configured) || configured.StartsWith(@"\\", StringComparison.Ordinal) ||
                    !String.Equals(Path.GetFullPath(configured).TrimEnd(Path.DirectorySeparatorChar), standard, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Pulizia globale non disponibile: percorso spool personalizzato o non valido. Usa l'annullamento della singola coda.");
            }
            if (!backend.DirectoryIsPlain(standard))
                throw new InvalidOperationException("Pulizia globale non disponibile: cartella spool assente, reindirizzata o non accessibile.");
            return standard;
        }
        public void Restart(Action<string> log)
        { Perform(false, log); }
        public void Clean(Action<string> log)
        { Perform(true, log); }
        private void Perform(bool clean, Action<string> log)
        {
            if (clean) Validate(); // A failed preflight must never touch the service.
            List<string> errors = new List<string>();
            int deleted = 0;
            try
            {
                log("Arresto spooler (attesa massima 30 secondi)...");
                backend.Stop();
                log("Spooler fermo.");
                if (clean)
                {
                    string directory = Validate(); // Recheck after stop before any file operation.
                    foreach (string file in backend.Files(directory))
                    {
                        string extension = Path.GetExtension(file);
                        if (!String.Equals(extension, ".spl", StringComparison.OrdinalIgnoreCase) && !String.Equals(extension, ".shd", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!String.Equals(Path.GetDirectoryName(Path.GetFullPath(file)), directory, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("File fuori dalla cartella spool: pulizia interrotta.");
                        try
                        {
                            // Revalidate parents for each deletion; never recurse or follow links.
                            Validate();
                            if (!backend.FileIsPlain(file)) throw new IOException("File reindirizzato o non regolare.");
                            backend.DeleteFile(file);
                            deleted++;
                            log("Eliminato: " + Path.GetFileName(file));
                        }
                        catch (Exception ex) { errors.Add(Path.GetFileName(file) + ": " + ex.Message); }
                    }
                }
            }
            catch (Exception ex) { errors.Add("Arresto/pulizia: " + ex.Message); }
            finally
            {
                try
                {
                    log("Avvio spooler (attesa massima 30 secondi)...");
                    backend.Start();
                    log("Spooler avviato.");
                }
                catch (Exception ex) { errors.Add("Riavvio spooler: " + ex.Message); }
            }
            if (clean) log("File di coda eliminati: " + deleted + ". Gli altri file e le sottocartelle sono stati conservati.");
            foreach (string error in errors) log("ERRORE: " + error);
            if (errors.Count > 0) throw new InvalidOperationException("Operazione incompleta. " + String.Join(" | ", errors.ToArray()));
        }
    }
    internal sealed class WindowsSpoolBackend : ISpoolBackend
    {
        public string StandardDirectory { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\spool\PRINTERS"); } }
        public IEnumerable<string> ConfiguredDirectories()
        {
            List<string> paths = new List<string>();
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Printers"))
            {
                if (key == null) throw new InvalidOperationException("Configurazione spooler non trovata.");
                object global = key.GetValue("DefaultSpoolDirectory", null);
                paths.Add(global == null ? StandardDirectory : Convert.ToString(global));
                foreach (string printer in key.GetSubKeyNames())
                {
                    using (RegistryKey child = key.OpenSubKey(printer))
                    {
                        if (child == null) throw new IOException("Configurazione stampante non accessibile.");
                        object custom = child.GetValue("SpoolDirectory", null);
                        // Windows writes an empty per-printer value to mean "inherit global".
                        // Whitespace and all other nonempty values still go through strict validation.
                        if (custom != null && Convert.ToString(custom).Length > 0) paths.Add(Convert.ToString(custom));
                    }
                }
            }
            return paths;
        }
        public bool DirectoryIsPlain(string path)
        {
            DirectoryInfo directory = new DirectoryInfo(path);
            while (directory != null)
            {
                if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                directory = directory.Parent;
            }
            return true;
        }
        public IEnumerable<string> Files(string directory) { return Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly); }
        public bool FileIsPlain(string path)
        { FileAttributes attributes = File.GetAttributes(path); return (attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) == 0; }
        public void DeleteFile(string path) { File.Delete(path); }
        public string Status()
        {
            using (ServiceController service = new ServiceController("Spooler"))
            {
                switch (service.Status)
                {
                    case ServiceControllerStatus.Running: return "In esecuzione";
                    case ServiceControllerStatus.Stopped: return "Arrestato";
                    case ServiceControllerStatus.StartPending: return "Avvio in corso";
                    case ServiceControllerStatus.StopPending: return "Arresto in corso";
                    default: return service.Status.ToString();
                }
            }
        }
        public void Stop()
        {
            using (ServiceController service = new ServiceController("Spooler"))
            {
                if (service.Status == ServiceControllerStatus.StartPending) service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                service.Refresh();
                if (service.Status == ServiceControllerStatus.Stopped) return;
                if (service.Status != ServiceControllerStatus.StopPending)
                {
                    // Do not silently stop dependent services.
                    foreach (ServiceController dependent in service.DependentServices)
                        using (dependent) if (dependent.Status != ServiceControllerStatus.Stopped)
                            throw new InvalidOperationException("Servizio dipendente attivo: " + dependent.ServiceName + ". Arrestalo manualmente prima di procedere.");
                    service.Stop();
                }
                service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }
        }
        public void Start()
        {
            using (ServiceController service = new ServiceController("Spooler"))
            {
                if (service.Status == ServiceControllerStatus.StopPending) service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                service.Refresh();
                if (service.Status == ServiceControllerStatus.Running) return;
                if (service.Status == ServiceControllerStatus.Stopped) service.Start();
                service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            }
        }
    }
}
