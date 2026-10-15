using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OnlyTweakWindows
{
    internal sealed class MachineRegistryStore : IRegistryStore
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
        public void Set(string path,string name,object value,RegistryValueKind kind)
        { using (RegistryKey root=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64)) using (RegistryKey key=root.CreateSubKey(path)) key.SetValue(name,value,kind); }
        public void DeleteValue(string path,string name)
        { using (RegistryKey root=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64)) using (RegistryKey key=root.OpenSubKey(path,true)) if (key!=null) key.DeleteValue(name,false); }
        public void DeleteKeyIfEmpty(string path)
        {
            using (RegistryKey root=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64))
            {
                using (RegistryKey key=root.OpenSubKey(path)) { if (key==null || key.ValueCount!=0 || key.SubKeyCount!=0) return; }
                root.DeleteSubKey(path,false);
            }
        }
        public void Notify() { }
    }
    internal sealed class WindowsSecurityBackend : ISecurityBackend
    {
        private readonly IRegistryStore registry=new MachineRegistryStore();
        public IRegistryStore Registry { get { return registry; } }
        internal static bool PolicyEdition
        {
            get
            {
                RegistryValueState state=new MachineRegistryStore().Read(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion","EditionID");
                string edition=Convert.ToString(state.Data);
                return edition.StartsWith("Professional",StringComparison.Ordinal) || edition.StartsWith("Enterprise",StringComparison.Ordinal) || edition.StartsWith("Education",StringComparison.Ordinal) || edition.StartsWith("IoTEnterprise",StringComparison.Ordinal);
            }
        }
        public bool HasTemplate(string name,string value)
        {
            if (name!="SmartScreen.admx" && name!="DeviceGuard.admx") return false;
            string file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"PolicyDefinitions",name);
            return File.Exists(file) && File.ReadAllText(file).Contains("valueName=\""+value+"\"");
        }
        private bool KeyHasChildren(string path)
        {
            using (RegistryKey root=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64))
            using (RegistryKey key=root.OpenSubKey(path)) return key!=null && key.SubKeyCount>0;
        }
        public SecurityEnvironment Probe()
        {
            SecurityEnvironment result=new SecurityEnvironment { PolicyEdition=PolicyEdition };
            result.Managed=KeyHasChildren(@"SOFTWARE\Microsoft\Provisioning\OMADM\Accounts") || KeyHasChildren(@"SYSTEM\CurrentControlSet\Control\CloudDomainJoin\JoinInfo");
            RegistryValueState sku=registry.Read(SecurityService.CiPolicy,"SkuPolicyRequired");
            result.SMode=sku.ValueExists && sku.Kind==RegistryValueKind.DWord && Object.Equals(sku.Data,1);
            string output;
            try
            {
                output=PowerShell("try { 'Domain='+[int][bool](Get-CimInstance Win32_ComputerSystem -ErrorAction Stop).PartOfDomain } catch { 'Domain=Unknown' };"+
                    "try { $d=Get-CimInstance -Namespace root\\Microsoft\\Windows\\DeviceGuard -ClassName Win32_DeviceGuard -ErrorAction Stop; 'DG=1'; 'VBS='+[int]$d.VirtualizationBasedSecurityStatus;"+
                    "'HC='+[int]($d.SecurityServicesConfigured -contains 2); 'HR='+[int]($d.SecurityServicesRunning -contains 2);"+
                    "'KC='+[int]($d.SecurityServicesConfigured -contains 5); 'KR='+[int]($d.SecurityServicesRunning -contains 5) } catch { 'DG=Unknown' };"+
                    "try { $p=Get-MpPreference -ErrorAction Stop; $s=Get-MpComputerStatus -ErrorAction Stop; 'Pua='+[int]$p.PUAProtection; 'Active='+[int]($s.AMRunningMode -eq 'Normal');"+
                    "if ($null -ne $s.IsTamperProtected) { 'Tamper='+[int][bool]$s.IsTamperProtected } } catch { 'Pua=Unknown' }");
            }
            catch (Exception) { return result; }
            foreach (string line in output.Split(new[] {'\r','\n'},StringSplitOptions.RemoveEmptyEntries))
            {
                string[] pair=line.Split('='); int number; if (pair.Length!=2 || !Int32.TryParse(pair[1],out number)) continue;
                switch (pair[0])
                {
                    case "Domain": result.ManagementKnown=true; result.Managed|=number!=0; break;
                    case "DG": result.DeviceGuardKnown=number==1; break;
                    case "VBS": result.VbsRunning=number==2; break;
                    case "HC": result.HvciConfigured=number==1; break;
                    case "HR": result.HvciRunning=number==1; break;
                    case "KC": result.KernelConfigured=number==1; break;
                    case "KR": result.KernelRunning=number==1; break;
                    case "Pua": result.DefenderKnown=true; result.Pua=number; break;
                    case "Active": result.DefenderActive=number==1; break;
                    case "Tamper": result.TamperKnown=true; result.TamperProtected=number==1; break;
                }
            }
            return result;
        }
        public void SetPua(int mode)
        {
            if (mode<0 || mode>2) throw new ArgumentException("Modalità PUA non consentita.");
            PowerShell("$ErrorActionPreference='Stop'; Set-MpPreference -PUAProtection "+mode+" -ErrorAction Stop");
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
                if (!process.WaitForExit(30000)) { process.Kill(); process.WaitForExit(); throw new TimeoutException("Lettura/configurazione protezioni oltre il limite di 30 secondi."); }
                string text=output.Result,details=error.Result;
                if (process.ExitCode!=0) throw new InvalidOperationException("Windows PowerShell: "+details.Trim());
                return text;
            }
        }
    }
}
