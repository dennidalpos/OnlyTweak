using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OnlyTweakWindows
{
    internal sealed class MachineRegistryStore : IRegistryReader
    {
        public RegistryValueState Read(string path,string name)
        {
            using (RegistryKey root=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64))
            using (RegistryKey key=root.OpenSubKey(path))
            {
                RegistryValueState state=new RegistryValueState { KeyExists=key!=null };
                if (key!=null && Array.Exists(key.GetValueNames(),n=>String.Equals(n,name,StringComparison.OrdinalIgnoreCase)))
                { state.ValueExists=true; state.Kind=key.GetValueKind(name); state.Data=key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames); }
                return state;
            }
        }
    }
    internal sealed class WindowsSecurityBackend : ISecurityBackend
    {
        internal const string ProbeCommand="try { $d=Get-CimInstance -Namespace root\\Microsoft\\Windows\\DeviceGuard -ClassName Win32_DeviceGuard -ErrorAction Stop;"+
            "if ($null -eq $d.SecurityServicesRunning) { 'DG=Unknown' } else { 'DG=1';"+
            "'HR='+[int]($d.SecurityServicesRunning -contains 2); 'KR='+[int]($d.SecurityServicesRunning -contains 5) } } catch { 'DG=Unknown' };"+
            "try { $p=Get-MpPreference -ErrorAction Stop; $s=Get-MpComputerStatus -ErrorAction Stop;"+
            "if ($null -ne $p.PUAProtection) { 'Pua='+[int]$p.PUAProtection };"+
            "if ($s.AMRunningMode -eq 'Normal') { 'Active=1' } elseif ($s.AMRunningMode -in @('Passive','EDR Block Mode','SxS Passive Mode')) { 'Active=0' } elseif ($null -ne $s.AMRunningMode) { 'Active=Unknown' }"+
            "elseif ($null -ne $s.AMServiceEnabled -and $null -ne $s.AntivirusEnabled) { 'Active='+[int]($s.AMServiceEnabled -and $s.AntivirusEnabled) } } catch { 'Pua=Unknown' }";
        private readonly IRegistryReader registry;
        public WindowsSecurityBackend() : this(new MachineRegistryStore()) { }
        internal WindowsSecurityBackend(IRegistryReader reader) { registry=reader; }
        public IRegistryReader Registry { get { return registry; } }
        public SecurityEnvironment Probe()
        {
            SecurityEnvironment result=new SecurityEnvironment();
            try {
                RegistryValueState sku=registry.Read(SecurityService.CiPolicy,"SkuPolicyRequired");
                result.SMode=sku.ValueExists && sku.Kind==RegistryValueKind.DWord && Object.Equals(sku.Data,1);
                result.SModeKnown=true;
            } catch (System.Security.SecurityException) { } catch (UnauthorizedAccessException) { } catch (IOException) { }
            string output;
            try
            {
                output=PowerShell(ProbeCommand);
            }
            catch (Exception) { return result; }
            return ParseProbe(output,result);
        }
        internal static SecurityEnvironment ParseProbe(string output,SecurityEnvironment result)
        {
            bool dgKnown=false,hvciKnown=false,kernelKnown=false,puaKnown=false,activeKnown=false;
            result.DeviceGuardKnown=result.HvciRunning=result.KernelRunning=result.DefenderKnown=result.DefenderActive=false;
            result.Pua=-1;
            foreach (string line in output.Split(new[] {'\r','\n'},StringSplitOptions.RemoveEmptyEntries))
            {
                string[] pair=line.Split('='); int number; if (pair.Length!=2) continue;
                bool numeric=Int32.TryParse(pair[1],out number),binary=numeric && (number==0 || number==1);
                switch (pair[0])
                {
                    case "DG": dgKnown=binary && number==1; break;
                    case "HR": hvciKnown=binary; result.HvciRunning=binary && number==1; break;
                    case "KR": kernelKnown=binary; result.KernelRunning=binary && number==1; break;
                    case "Pua": puaKnown=numeric && number>=0 && number<=2; result.Pua=puaKnown ? number : -1; break;
                    case "Active": activeKnown=binary; result.DefenderActive=binary && number==1; break;
                }
            }
            // Both runtime flags come from the same CIM property; incomplete reads prove neither state.
            result.DeviceGuardKnown=dgKnown && hvciKnown && kernelKnown;
            result.HvciRunning=result.DeviceGuardKnown && result.HvciRunning;
            result.KernelRunning=result.DeviceGuardKnown && result.KernelRunning;
            result.DefenderKnown=puaKnown && activeKnown;
            return result;
        }
        // Fixed commands only; no downloaded scripts, ExecutionPolicy changes or PowerShell 7.
        internal static string PowerShell(string command)
        {
            string file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
            string script="[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding; "+command;
            ProcessStartInfo info=new ProcessStartInfo(file,"-NoLogo -NoProfile -NonInteractive -Command "+ProcessService.QuoteArgument(script))
            { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,
                WorkingDirectory=Environment.GetFolderPath(Environment.SpecialFolder.System) };
            using (Process process=Process.Start(info))
            {
                Task<string> output=process.StandardOutput.ReadToEndAsync(),error=process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(30000)) { process.Kill(); process.WaitForExit(); throw new TimeoutException("Lettura protezioni oltre il limite di 30 secondi."); }
                string text=output.Result,details=error.Result;
                if (process.ExitCode!=0) throw new InvalidOperationException("Windows PowerShell: "+details.Trim());
                return text;
            }
        }
    }
}
