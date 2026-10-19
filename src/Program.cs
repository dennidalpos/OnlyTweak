using System;
using System.Reflection;
using System.Security.Principal;
using System.Windows.Forms;


[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.7.2",FrameworkDisplayName=".NET Framework 4.7.2")]
[assembly: AssemblyTitle("OnlyTweakWindows")]
[assembly: AssemblyDescription("Tweak e strumenti Windows 10 e 11, senza installazione")]
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
                if (!Environment.Is64BitProcess || !EnvironmentInfo.Profile.Supported)
                    throw new NotSupportedException("Occorre Windows 10 dalla versione 1809 o Windows 11, x64 Intel/AMD. Windows Server e ARM64 non supportati.");
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
        public static readonly OsProfile Profile=OsProfile.Detect();
        public static int Build { get { return Profile.Build; } }
        public static string Summary { get { return Profile.Summary; } }
        public static string Account
        { get { using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) return identity.Name; } }
    }
}
