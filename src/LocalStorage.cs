using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
namespace OnlyTweakWindows {
    internal static class SecureFiles
    {
        public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OnlyTweak");
        internal static void AssertPlain(string path) {
            for(FileSystemInfo info=File.Exists(path) ? new FileInfo(path) : new DirectoryInfo(path);info!=null;
                info=info is FileInfo file ? file.Directory : ((DirectoryInfo)info).Parent)
                if(info.Exists && (info.Attributes&FileAttributes.ReparsePoint)!=0) throw new IOException("Percorso reindirizzato.");
        }
        public static void EnsureDirectory(string path)
        {
            path=Path.GetFullPath(path);
            for (DirectoryInfo info=new DirectoryInfo(path); info!=null; info=info.Parent)
                if (info.Exists && (info.Attributes & FileAttributes.ReparsePoint)!=0)
                    throw new IOException("Percorso reindirizzato non consentito.");
            Directory.CreateDirectory(path);
            DirectorySecurity acl=new DirectorySecurity();
            acl.SetAccessRuleProtection(true,false);
            foreach (SecurityIdentifier sid in new[] { WindowsIdentity.GetCurrent().User,
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null) })
                acl.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
            new DirectoryInfo(path).SetAccessControl(acl);
        }
        public static void AtomicWrite(string path,string content)
        {
            EnsureDirectory(Path.GetDirectoryName(path));
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                using (FileStream file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {
                    byte[] bytes=Encoding.UTF8.GetBytes(content); file.Write(bytes); file.Flush(true);
                }
                if (File.Exists(path)) {
                    if ((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0) throw new IOException("File reindirizzato.");
                    File.Move(temp,path,true);
                } else File.Move(temp,path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
    internal interface IOperationAudit {void Record(OperationRequest request,string identity,string phase,StepResult result=null);}
    internal sealed class FileOperationAudit : IOperationAudit {
        private readonly string root;
        internal FileOperationAudit(string root=null) {this.root=root??Path.Combine(SecureFiles.Root,"audit","local");}
        public void Record(OperationRequest request,string identity,string phase,StepResult result=null) {
            SecureFiles.EnsureDirectory(root);
            string path=Path.Combine(root,DateTime.UtcNow.ToString("yyyy-MM-dd")+".jsonl");
            if(File.Exists(path) && (File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0) throw new IOException("Audit reindirizzato.");
            var entry=new {schemaVersion=1,time=DateTimeOffset.UtcNow,request.OperationId,request.ActionId,
                target=Environment.MachineName,identity,phase,outcome=result?.State.ToString(),errorCode=result?.ErrorCode};
            using var stream=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.Read);
            byte[] bytes=Encoding.UTF8.GetBytes(Wire.Write(entry)+Environment.NewLine);stream.Write(bytes);stream.Flush(true);
        }
    }
}
