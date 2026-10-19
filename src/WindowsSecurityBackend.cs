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
        private readonly IRegistryReader registry=new MachineRegistryStore();
        public IRegistryReader Registry { get { return registry; } }
        public SecurityEnvironment Probe()
        {
            SecurityEnvironment result=new SecurityEnvironment();
            RegistryValueState sku=registry.Read(SecurityService.CiPolicy,"SkuPolicyRequired");
            result.SMode=sku.ValueExists && sku.Kind==RegistryValueKind.DWord && Object.Equals(sku.Data,1);
            string output;
            try
            {
                output=PowerShell("try { $d=Get-CimInstance -Namespace root\\Microsoft\\Windows\\DeviceGuard -ClassName Win32_DeviceGuard -ErrorAction Stop; 'DG=1';"+
                    "'HR='+[int]($d.SecurityServicesRunning -contains 2); 'KR='+[int]($d.SecurityServicesRunning -contains 5) } catch { 'DG=Unknown' };"+
                    "try { $p=Get-MpPreference -ErrorAction Stop; $s=Get-MpComputerStatus -ErrorAction Stop;"+
                    "if ($null -ne $p.PUAProtection) { 'Pua='+[int]$p.PUAProtection };"+
                    "if ($null -ne $s.AMRunningMode) { 'Active='+[int]($s.AMRunningMode -eq 'Normal') }"+
                    "elseif ($null -ne $s.AMServiceEnabled -and $null -ne $s.AntivirusEnabled) { 'Active='+[int]($s.AMServiceEnabled -and $s.AntivirusEnabled) } } catch { 'Pua=Unknown' }");
            }
            catch (Exception) { return result; }
            return ParseProbe(output,result);
        }
        internal static SecurityEnvironment ParseProbe(string output,SecurityEnvironment result)
        {
            bool puaKnown=false,activeKnown=false;
            foreach (string line in output.Split(new[] {'\r','\n'},StringSplitOptions.RemoveEmptyEntries))
            {
                string[] pair=line.Split('='); int number; if (pair.Length!=2 || !Int32.TryParse(pair[1],out number)) continue;
                switch (pair[0])
                {
                    case "DG": result.DeviceGuardKnown=number==1; break;
                    case "HR": result.HvciRunning=number==1; break;
                    case "KR": result.KernelRunning=number==1; break;
                    case "Pua": puaKnown=number>=0 && number<=2; result.Pua=number; break;
                    case "Active": activeKnown=number==0 || number==1; result.DefenderActive=number==1; break;
                }
            }
            result.DefenderKnown=puaKnown && activeKnown;
            return result;
        }
        // Fixed commands only; no downloaded scripts, ExecutionPolicy changes or PowerShell 7.
        private static string PowerShell(string command)
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
