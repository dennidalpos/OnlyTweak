using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace OnlyTweakWindows
{
    internal static class ShellService
    {
        public static string Unavailable()
        {
            try
            {
                using (Process shell = GetShellProcess())
                {
                    IntPtr token = OpenShellToken(shell);
                    try { VerifyOwner(token); } finally { CloseHandle(token); }
                }
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }
        private static Process GetShellProcess()
        {
            IntPtr window = GetShellWindow();
            if (window==IntPtr.Zero) throw new InvalidOperationException("Shell del desktop non disponibile.");
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            Process shell = Process.GetProcessById(checked((int)pid));
            using (Process current = Process.GetCurrentProcess())
            {
                if (!String.Equals(shell.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase) || shell.SessionId!=current.SessionId)
                { shell.Dispose(); throw new InvalidOperationException("La shell non appartiene alla sessione corrente."); }
            }
            return shell;
        }
        private static IntPtr OpenShellToken(Process shell)
        {
            IntPtr token;
            if (!OpenProcessToken(shell.Handle, 0x0002 | 0x0008, out token)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return token;
        }
        private static void VerifyOwner(IntPtr token)
        {
            using (WindowsIdentity shellIdentity = new WindowsIdentity(token))
            using (WindowsIdentity currentIdentity = WindowsIdentity.GetCurrent())
                if (shellIdentity.User==null || !shellIdentity.User.Equals(currentIdentity.User))
                    throw new InvalidOperationException("La shell appartiene a un altro account. Riavvia Esplora risorse da quell'account o disconnettiti.");
        }
        public static void Restart(Action<string> log)
        {
            using (Process shell = GetShellProcess())
            {
                IntPtr original = OpenShellToken(shell), duplicate = IntPtr.Zero;
                try
                {
                    VerifyOwner(original);
                    // Capture the shell's original token BEFORE stopping it: never start an elevated desktop.
                    if (!DuplicateTokenEx(original, 0x02000000, IntPtr.Zero, 2, 1, out duplicate)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    log("Chiusura della shell dell'account corrente...");
                    shell.Kill();
                    if (!shell.WaitForExit(10000)) throw new InvalidOperationException("La shell non si è chiusa entro il tempo previsto.");
                    for (int i=0; i<30; i++)
                    {
                        if (GetShellWindow()!=IntPtr.Zero) { log("Esplora risorse riavviato automaticamente da Windows."); return; }
                        Thread.Sleep(100);
                    }
                    string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                    STARTUPINFO startup = new STARTUPINFO { Size=Marshal.SizeOf(typeof(STARTUPINFO)), Desktop=@"winsta0\default" };
                    PROCESS_INFORMATION process;
                    if (!CreateProcessWithTokenW(duplicate, 1, explorer, new StringBuilder(ProcessService.QuoteArgument(explorer)), 0, IntPtr.Zero,
                        Environment.GetFolderPath(Environment.SpecialFolder.Windows), ref startup, out process))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Impossibile riaprire la shell. Usa Gestione attività > Esegui nuova attività > explorer.exe senza privilegi amministrativi, oppure disconnettiti.");
                    CloseHandle(process.Thread); CloseHandle(process.Process);
                    log("Avvio di Esplora risorse richiesto con il token originale del desktop.");
                }
                finally { if (duplicate!=IntPtr.Zero) CloseHandle(duplicate); CloseHandle(original); }
            }
        }
        public static void LaunchInteractiveTarget(string target)
        {
            using (Process shell=GetShellProcess())
            {
                IntPtr original=OpenShellToken(shell),duplicate=IntPtr.Zero,environment=IntPtr.Zero;
                try
                {
                    if (!DuplicateTokenEx(original,0x02000000,IntPtr.Zero,2,1,out duplicate)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    if (!CreateEnvironmentBlock(out environment,duplicate,false)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    string explorer=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe");
                    STARTUPINFO startup=new STARTUPINFO { Size=Marshal.SizeOf(typeof(STARTUPINFO)),Desktop=@"winsta0\default" };
                    PROCESS_INFORMATION process;
                    if (!CreateProcessWithTokenW(duplicate,1,explorer,new StringBuilder(ProcessService.QuoteArgument(explorer)+" "+ProcessService.QuoteArgument(target)),
                        0x00000400,environment,Environment.GetFolderPath(Environment.SpecialFolder.Windows),ref startup,out process))
                        throw new Win32Exception(Marshal.GetLastWin32Error(),"Impossibile aprire la pagina per l'utente del desktop.");
                    CloseHandle(process.Thread); CloseHandle(process.Process);
                }
                finally
                {
                    if (environment!=IntPtr.Zero) DestroyEnvironmentBlock(environment);
                    if (duplicate!=IntPtr.Zero) CloseHandle(duplicate);
                    CloseHandle(original);
                }
            }
        }
        [DllImport("userenv.dll",SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateEnvironmentBlock(out IntPtr environment,IntPtr token,[MarshalAs(UnmanagedType.Bool)] bool inherit);
        [DllImport("userenv.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyEnvironmentBlock(IntPtr environment);
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int Size;
            public string Reserved, Desktop, Title;
            public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
            public short ShowWindow, ReservedSize;
            public IntPtr Reserved2, StdInput, StdOutput, StdError;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("advapi32.dll", SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DuplicateTokenEx(IntPtr existing, uint access, IntPtr attributes, int level, int type, out IntPtr token);
        [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateProcessWithTokenW(IntPtr token, uint flags, string application, StringBuilder command, uint creationFlags, IntPtr environment, string directory, ref STARTUPINFO startup, out PROCESS_INFORMATION process);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
    }
}
