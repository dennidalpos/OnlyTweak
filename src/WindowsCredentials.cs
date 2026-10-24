using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

namespace OnlyTweakWindows
{
    internal static class WindowsCredentials
    {
        internal static Process Start(string executable,string arguments,IntPtr owner,out string identity)
        {
            CredentialInfo info=new CredentialInfo {Size=Marshal.SizeOf<CredentialInfo>(),Owner=owner,
                Caption="OnlyTweak · credenziali della sessione",Message="Account del cliente: credenziali di rete, senza salvataggio. Usa una postazione amministrativa appropriata."};
            uint package=0,size=0; bool save=false; IntPtr packed=IntPtr.Zero;
            int code=CredUIPromptForWindowsCredentials(ref info,0,ref package,IntPtr.Zero,0,out packed,out size,ref save,1);
            if (code==1223) throw new OperationCanceledException("Accesso annullato.");
            if (code!=0) throw new Win32Exception(code,"Impossibile acquisire le credenziali.");
            IntPtr user=IntPtr.Zero,domain=IntPtr.Zero,password=IntPtr.Zero;
            uint userSize=0,domainSize=0,passwordSize=0;
            try
            {
                CredUnPackAuthenticationBuffer(0,packed,size,IntPtr.Zero,ref userSize,IntPtr.Zero,ref domainSize,IntPtr.Zero,ref passwordSize);
                if (Marshal.GetLastWin32Error()!=122 || userSize>1024 || domainSize>1024 || passwordSize>4096)
                    throw new InvalidOperationException("Formato credenziali non supportato.");
                user=Marshal.AllocCoTaskMem(checked((int)userSize*2));
                domain=Marshal.AllocCoTaskMem(checked((int)domainSize*2));
                password=Marshal.AllocCoTaskMem(checked((int)passwordSize*2));
                if (!CredUnPackAuthenticationBuffer(0,packed,size,user,ref userSize,domain,ref domainSize,password,ref passwordSize))
                    throw new Win32Exception(Marshal.GetLastWin32Error(),"Credenziali non leggibili.");
                string account=Marshal.PtrToStringUni(user),authority=Marshal.PtrToStringUni(domain);
                identity=String.IsNullOrEmpty(authority) ? account : authority+"\\"+account;
                StartupInfo startup=new StartupInfo {Size=Marshal.SizeOf<StartupInfo>()};
                ProcessInfo process;
                // LOGON_NETCREDENTIALS_ONLY creates an isolated LSA logon; it does not validate the password.
                if (!CreateProcessWithLogonW(account,String.IsNullOrEmpty(authority) ? null : authority,password,2,
                    executable,new StringBuilder(ProcessService.QuoteArgument(executable)+" "+arguments),0x08000000,IntPtr.Zero,
                    System.IO.Path.GetDirectoryName(executable),ref startup,out process))
                    throw new Win32Exception(Marshal.GetLastWin32Error(),"Sessione di rete non avviata.");
                try { return Process.GetProcessById((int)process.Id); }
                finally { CloseHandle(process.Thread); CloseHandle(process.Process); }
            }
            finally
            {
                ZeroFree(password,checked((int)passwordSize*2)); ZeroFree(user,checked((int)userSize*2)); ZeroFree(domain,checked((int)domainSize*2));
                ZeroFree(packed,checked((int)size));
            }
        }
        private static void ZeroFree(IntPtr pointer,int size)
        { if (pointer==IntPtr.Zero) return; for (int i=0;i<size;i++) Marshal.WriteByte(pointer,i,0); Marshal.FreeCoTaskMem(pointer); }
        public static string Protect(SecureString password)
        {
            IntPtr value=Marshal.SecureStringToGlobalAllocUnicode(password);
            byte[] bytes=new byte[password.Length*2];
            try
            {
                Marshal.Copy(value,bytes,0,bytes.Length);
                return Convert.ToBase64String(ProtectedData.Protect(bytes,null,DataProtectionScope.CurrentUser));
            }
            finally { CryptographicOperations.ZeroMemory(bytes); Marshal.ZeroFreeGlobalAllocUnicode(value); }
        }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        private struct CredentialInfo { public int Size; public IntPtr Owner; public string Message,Caption; public IntPtr Banner; }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        private struct StartupInfo
        {
            public int Size; public string Reserved,Desktop,Title;
            public int X,Y,Width,Height,XChars,YChars,Fill,Flags; public short Show,ReservedSize;
            public IntPtr ReservedPointer,Input,Output,Error;
        }
        [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Process,Thread; public uint Id,ThreadId; }
        [DllImport("credui.dll",CharSet=CharSet.Unicode)]
        private static extern int CredUIPromptForWindowsCredentials(ref CredentialInfo info,int error,ref uint package,
            IntPtr input,uint inputSize,out IntPtr output,out uint outputSize,ref bool save,uint flags);
        [DllImport("credui.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CredUnPackAuthenticationBuffer(uint flags,IntPtr buffer,uint size,
            IntPtr user,ref uint userSize,IntPtr domain,ref uint domainSize,IntPtr password,ref uint passwordSize);
        [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CreateProcessWithLogonW(string user,string domain,IntPtr password,uint logon,
            string application,StringBuilder command,uint flags,IntPtr environment,string directory,ref StartupInfo startup,out ProcessInfo process);
        [DllImport("kernel32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
    }
    internal static class LocalPrivilege
    {
        internal static void ExecuteAudited(ActionSpec action,Action<string> log)
        {
            OperationRequest request=new OperationRequest {Profile=TechnicianProfile(),ActionId=action.Id,
                Target=Environment.MachineName,Confirmed=true};
            FileOperationAudit audit=new FileOperationAudit();
            audit.Record(request,EnvironmentInfo.Account,"requested");
            StepResult result=new StepResult {State=StepState.Unverified};
            try { action.Execute(log); result.State=StepState.Success; }
            finally { audit.Record(request,EnvironmentInfo.Account,"finished",result); }
        }
        public static bool IsAdministrator {
            get { using (WindowsIdentity identity=WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); }
        }
        public static EnvironmentProfile TechnicianProfile() => new EnvironmentProfile {
            Id=new Guid("89870428-5153-4967-8712-44ea3fa32904"),Customer="Postazione tecnico",
            Domain="local.invalid",DomainController="technician.local.invalid",Targets=new[] {"technician.local.invalid"}
        };
        public static void Execute(string id,Action<string> log)
        {
            using (CustomerSession session=CustomerSession.Start(TechnicianProfile(),false,true).GetAwaiter().GetResult())
                log(session.ExecuteLocal(id).GetAwaiter().GetResult());
        }
        internal static StepResult RunAuthorized(string id)
        {
            if (!IsAdministrator) throw new InvalidOperationException("Elevazione locale richiesta.");
            string[] allowed={"spool.restart","spool.clean","network.dns","network.winsock","network.tcpip",
                "sfc.verify","sfc.repair","dism.scan","dism.repair"};
            if (Array.IndexOf(allowed,id)<0) throw new ArgumentException("Azione locale non consentita.");
            AppServices app=new AppServices();
            ActionSpec action=null;
            foreach (MenuDefinition menu in new[] {app.Catalog.Network,app.Catalog.Maintenance,app.Catalog.Spool})
                foreach (ActionSpec candidate in menu.AllActions()) if (candidate.Id==id) action=candidate;
            if (action==null || !action.Applicable) throw new InvalidOperationException("Componente non disponibile.");
            StepResult result=new StepResult {OperationId=Guid.NewGuid(),ActionId=id,Target=Environment.MachineName,
                Identity=EnvironmentInfo.Account,Started=DateTimeOffset.UtcNow,State=StepState.Cancelled,Summary="Operazione annullata."};
            if (!WorkerHost.ConfirmOnSta(action.Title,action.Confirmation??action.Description)) return result;
            OperationRequest request=new OperationRequest {OperationId=result.OperationId,Profile=TechnicianProfile(),ActionId=id,Target=result.Target,Confirmed=true};
            FileOperationAudit audit=new FileOperationAudit();
            audit.Record(request,result.Identity,"requested");
            BoundedText output=new BoundedText();
            try {
                if (action.Unavailable!=null) { string reason=action.Unavailable(); if (reason!=null) throw new InvalidOperationException(reason); }
                action.Execute(line=>output.Append(line+Environment.NewLine));
                result.State=StepState.Success; result.Summary="Esecuzione terminata; leggere il riepilogo dello strumento.";
            } catch (Exception ex) { result.State=StepState.Unverified; result.Summary="Operazione incompleta o non verificata."; result.ErrorCode=ex.GetType().Name; }
            result.Evidence=new[] {output.Read()}; result.Truncated=output.Truncated; result.Finished=DateTimeOffset.UtcNow;
            audit.Record(request,result.Identity,"finished",result);
            return result;
        }
    }
}
