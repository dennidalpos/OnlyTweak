using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OnlyTweakWindows
{
    // Native version/product type, not ProductName (which may still say Windows 10 on 11).
    internal sealed class OsProfile
    {
        public readonly int Major,Minor,Build,Revision,Architecture,ProductType;
        public readonly string Edition,Release;
        public OsProfile(int major,int minor,int build,int revision,int architecture,int productType,string edition,string release)
        { Major=major; Minor=minor; Build=build; Revision=revision; Architecture=architecture; ProductType=productType; Edition=edition??""; Release=release??""; }
        public bool IsWindows11 { get { return Major==10 && Minor==0 && ProductType==1 && Build>=22000; } }
        public bool Supported { get { return Major==10 && Minor==0 && ProductType==1 && Build>=17763 && Architecture==9; } }
        public bool LocalUsers { get { return Edition.StartsWith("Professional",StringComparison.OrdinalIgnoreCase) || Edition.StartsWith("Enterprise",StringComparison.OrdinalIgnoreCase) || Edition.StartsWith("Education",StringComparison.OrdinalIgnoreCase) || Edition.StartsWith("IoTEnterprise",StringComparison.OrdinalIgnoreCase); } }
        public string Summary { get { return (ProductType==1 ? (IsWindows11 ? "Windows 11" : "Windows 10") : "Windows Server")+
            (Release.Length==0 ? "" : " "+Release)+(Edition.Length==0 ? "" : " · "+Edition)+" · build "+Build+"."+Revision+" · "+(Architecture==9 ? "x64" : Architecture==12 ? "ARM64" : "x86"); } }
        public static string ReleaseName(string display,string releaseId) { return String.IsNullOrWhiteSpace(display) ? releaseId??"" : display; }
        internal static int NativeArchitecture(ushort machine)
        { return machine==0x8664 ? 9 : machine==0xaa64 ? 12 : machine==0x014c ? 0 : -1; }
        public static OsProfile Detect()
        {
            NativeVersion version=new NativeVersion(); version.Size=Marshal.SizeOf(typeof(NativeVersion));
            if (RtlGetVersion(ref version)!=0) throw new InvalidOperationException("Impossibile rilevare la versione nativa di Windows.");
            // GetNativeSystemInfo can report x64 on ARM64 with x64 emulation.
            // IsWow64Process2 (available before our 1809 minimum) reports the host machine.
            int architecture=-1;
            if (version.Major==10 && version.Build>=17763)
            {
                ushort processMachine,nativeMachine;
                if (!IsWow64Process2(new IntPtr(-1),out processMachine,out nativeMachine))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Impossibile verificare l’architettura nativa.");
                architecture=NativeArchitecture(nativeMachine);
            }
            using (RegistryKey root=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64))
            using (RegistryKey key=root.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                int revision=0; string release="",edition="";
                if (key!=null) { Int32.TryParse(Convert.ToString(key.GetValue("UBR")),out revision); edition=Convert.ToString(key.GetValue("EditionID"));
                    release=ReleaseName(Convert.ToString(key.GetValue("DisplayVersion")),Convert.ToString(key.GetValue("ReleaseId"))); }
                return new OsProfile(version.Major,version.Minor,version.Build,revision,architecture,version.ProductType,edition,release);
            }
        }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        private struct NativeVersion
        {
            public int Size,Major,Minor,Build,Platform;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string ServicePack;
            public ushort ServicePackMajor,ServicePackMinor,SuiteMask;
            public byte ProductType,Reserved;
        }
        [DllImport("ntdll.dll",CharSet=CharSet.Unicode)] private static extern int RtlGetVersion(ref NativeVersion version);
        [DllImport("kernel32.dll",SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWow64Process2(IntPtr process,out ushort processMachine,out ushort nativeMachine);
    }
    internal sealed class FeatureAvailability
    {
        public readonly OsProfile OS;
        private readonly Func<string,bool> file,target;
        public FeatureAvailability(OsProfile os,Func<string,bool> file,Func<string,bool> target) { OS=os; this.file=file; this.target=target; }
        public bool File(string name) { return file(name); }
        public bool Security(string id)
        { return OS.Supported && ((id!="sac" && id!="kernel") || (OS.IsWindows11 && OS.Build>=22621)); }
        // Older security apps do not promise the newer deep links. Open the parent page instead.
        public string ResolveTarget(string requested)
        {
            ProcessService.ValidateTarget(requested);
            string resolved=requested;
            if (requested=="windowsdefender://smartapp/" && !Security("sac")) resolved="windowsdefender://appbrowser/";
            if (requested=="windowsdefender://smartscreenpua/" && !OS.IsWindows11) resolved="windowsdefender://appbrowser/";
            if (resolved.StartsWith("windowsdefender:",StringComparison.Ordinal) && !target(resolved)) resolved="ms-settings:windowsdefender";
            return resolved;
        }
        public bool Target(string requested) { return target(ResolveTarget(requested)); }
    }
}
