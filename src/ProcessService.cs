using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using Microsoft.Win32;

namespace OnlyTweakWindows
{
    internal sealed class ProcessService
    {
        private readonly Action<string> interactive;
        internal const string ClassicPrinters="shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}";
        internal static readonly string[] AllowedTargets={ ClassicPrinters,"windowsdefender://appbrowser/","windowsdefender://coreisolation/","windowsdefender://smartapp/","windowsdefender://smartscreenpua/",
            "https://account.microsoft.com/","https://support.microsoft.com/it-it/accounts-billing/manage/how-to-close-your-microsoft-account",
            "ms-settings:printers","ms-settings:network-proxy","ms-settings:storagesense","ms-settings:appsfeatures","ms-settings:startupapps",
            "ms-settings:windowsupdate","ms-settings:yourinfo","ms-settings:emailandaccounts","ms-settings:workplace","ms-settings:otherusers" };
        public ProcessService() : this(ShellService.LaunchInteractiveTarget) { }
        internal ProcessService(Action<string> launch) { interactive=launch; }
        public string SystemFile(string name)
        { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name); }
        public string FileUnavailable(string name)
        { return File.Exists(SystemFile(name)) ? null : "Componente Windows non disponibile: " + name; }
        public void Run(string name, string arguments, Action<string> log, bool unicode)
        {
            string file = SystemFile(name);
            if (!File.Exists(file)) throw new FileNotFoundException("Componente Windows non trovato.", file);
            log(name + " " + arguments);
            ProcessStartInfo start = new ProcessStartInfo(file, arguments) {
                UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true,
                WorkingDirectory=Environment.GetFolderPath(Environment.SpecialFolder.System),
                StandardOutputEncoding=unicode ? Encoding.Unicode : Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
                StandardErrorEncoding=unicode ? Encoding.Unicode : Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage)
            };
            using (Process process = new Process { StartInfo=start })
            {
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (!String.IsNullOrEmpty(e.Data)) log(e.Data.Replace("\0", "")); };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (!String.IsNullOrEmpty(e.Data)) log(e.Data.Replace("\0", "")); };
                if (!process.Start()) throw new InvalidOperationException("Impossibile avviare lo strumento.");
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit(); // Also drains both async output streams.
                log("Codice di uscita: " + process.ExitCode + ".");
                // Servicing may return ERROR_SUCCESS_REBOOT_REQUIRED (3010).
                if (process.ExitCode == 3010) log("Riavvio del PC richiesto dallo strumento. Riavvia manualmente quando pronto.");
                else if (process.ExitCode != 0) throw new InvalidOperationException("Lo strumento ha restituito un errore (" + process.ExitCode + "). Consulta l'output.");
                if (String.Equals(name, "sfc.exe", StringComparison.OrdinalIgnoreCase))
                    log("Per l'esito dell'integrità e delle eventuali riparazioni, leggi il riepilogo SFC sopra: il solo codice di uscita non descrive tutti i casi.");
            }
        }
        public void LaunchSystem(string name, string arguments, Action<string> log)
        {
            string file = SystemFile(name);
            if (!File.Exists(file)) throw new FileNotFoundException("Componente Windows non disponibile.", file);
            using (Process process = Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute=true, WorkingDirectory=Path.GetDirectoryName(file) })) { }
            log("Apertura richiesta: " + name + ". La richiesta di apertura non conferma modifiche nello strumento esterno.");
        }
        public void LaunchSettings(string uri, Action<string> log)
        {
            LaunchTarget(uri,log);
        }
        internal static void ValidateTarget(string target)
        {
            if (Array.IndexOf(AllowedTargets,target)<0) throw new ArgumentException("Destinazione non consentita.");
        }
        public string TargetUnavailable(string target)
        {
            ValidateTarget(target);
            if (target==ClassicPrinters)
            {
                using (RegistryKey root=RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot,RegistryView.Registry64))
                using (RegistryKey key=root.OpenSubKey(@"CLSID\{A8A91A66-3A7D-4424-8D24-04E180695C7A}"))
                    return key==null ? "Vista classica non disponibile. Usa Stampanti e scanner nelle Impostazioni Windows." : null;
            }
            string protocol=target.Substring(0,target.IndexOf(':'));
            using (RegistryKey root=RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot,RegistryView.Registry64))
            using (RegistryKey key=root.OpenSubKey(protocol))
                return key==null ? "Gestore della destinazione non disponibile: "+protocol+"." : null;
        }
        public void LaunchTarget(string target,Action<string> log)
        {
            ValidateTarget(target);
            string reason=TargetUnavailable(target); if (reason!=null) throw new InvalidOperationException(reason);
            interactive(target);
            log("Apertura richiesta nel contesto dell'utente del desktop. Verifica la pagina aperta: la richiesta non certifica la vista né modifiche esterne.");
        }
        public void PrintUi(string mode, string printer, Action<string> log)
        {
            if (mode != "/p" && mode != "/e" && mode != "/o") throw new ArgumentException("Operazione stampante non consentita.");
            if (String.IsNullOrWhiteSpace(printer) || printer.IndexOf('\0') >= 0 || printer.IndexOf('"') >= 0)
                throw new ArgumentException("Nome stampante vuoto o non compatibile con PrintUI. Apri Stampanti e scanner.");
            LaunchSystem("rundll32.exe", "printui.dll,PrintUIEntry " + mode + " /n " + QuoteArgument(printer), log);
            log("Stampante: " + printer);
        }
        // Windows CommandLineToArgvW quoting; never invoke cmd.exe or PowerShell.
        public static string QuoteArgument(string value)
        {
            StringBuilder text = new StringBuilder("\"");
            int slashes=0;
            foreach (char c in value)
            {
                if (c=='\\') { slashes++; continue; }
                if (c=='"') text.Append('\\', slashes*2+1).Append(c);
                else text.Append('\\', slashes).Append(c);
                slashes=0;
            }
            return text.Append('\\', slashes*2).Append('"').ToString();
        }
        public static void ReadNetwork(Action<string> log)
        {
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
            if (interfaces.Length==0) log("Nessuna scheda di rete trovata.");
            foreach (NetworkInterface adapter in interfaces)
            {
                log(adapter.Name + " · " + adapter.OperationalStatus);
                log("  " + adapter.Description);
                try
                {
                    IPInterfaceProperties info = adapter.GetIPProperties();
                    foreach (UnicastIPAddressInformation address in info.UnicastAddresses) log("  IP: " + address.Address);
                    foreach (GatewayIPAddressInformation gateway in info.GatewayAddresses) log("  Gateway: " + gateway.Address);
                    foreach (System.Net.IPAddress dns in info.DnsAddresses) log("  DNS: " + dns);
                }
                catch (NetworkInformationException ex) { log("  Lettura parziale: " + ex.Message); }
                log("");
            }
        }
    }
}
