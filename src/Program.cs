using System;
using System.Reflection;
using System.Security.Principal;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("OnlyTweakWindows")]
[assembly: AssemblyDescription("Tweak e strumenti Windows 11, senza installazione")]
[assembly: AssemblyProduct("OnlyTweakWindows")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

namespace OnlyTweakWindows
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            { MessageBox.Show(e.Exception.Message, "OnlyTweakWindows", MessageBoxButtons.OK, MessageBoxIcon.Error); };
            try
            {
                if (!Environment.Is64BitProcess || EnvironmentInfo.Build < 22000)
                    throw new NotSupportedException("Occorre Windows 11 x64 (Intel o AMD). ARM64 non supportato nella prima versione.");
                string architecture = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "";
                string nativeArchitecture = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") ?? architecture;
                if (nativeArchitecture.IndexOf("ARM", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new NotSupportedException("Questa versione supporta solo Windows 11 x64 Intel/AMD.");
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                        throw new InvalidOperationException("Avvia l'app come amministratore.");
                }
                Application.Run(new MainForm(new AppServices()));
            }
            catch (Exception ex)
            { MessageBox.Show(ex.Message, "OnlyTweakWindows", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }

    internal static class EnvironmentInfo
    {
        public static int Build
        {
            get
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    int build;
                    return key != null && Int32.TryParse(Convert.ToString(key.GetValue("CurrentBuildNumber")), out build) ? build : 0;
                }
            }
        }
        public static string Summary
        {
            get
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    return "Windows 11 " + (key == null ? "" : Convert.ToString(key.GetValue("DisplayVersion"))) +
                        " · build " + Build + "." + (key == null ? "?" : Convert.ToString(key.GetValue("UBR"))) + " · x64";
                }
            }
        }
        public static string Account
        { get { using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) return identity.Name; } }
    }
}
