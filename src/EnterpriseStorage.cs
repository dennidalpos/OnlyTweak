using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace OnlyTweakWindows
{
    internal static class SecureFiles
    {
        public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OnlyTweak");
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
    internal static class ProfileStore
    {
        public static EnvironmentProfile[] Load()
        {
            string path=Path.Combine(SecureFiles.Root,"profiles.json");
            if (!File.Exists(path)) return Array.Empty<EnvironmentProfile>();
            SecureFiles.EnsureDirectory(SecureFiles.Root);
            if ((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0 || new FileInfo(path).Length>1048576)
                throw new IOException("Archivio profili non valido.");
            EnvironmentProfile[] profiles=Wire.Read<EnvironmentProfile[]>(File.ReadAllText(path));
            if (profiles.Length>100 || profiles.Select(p=>p.Id).Distinct().Count()!=profiles.Length)
                throw new FormatException("Archivio profili non valido.");
            foreach (var profile in profiles) profile.Validate();
            return profiles;
        }
        public static void Save(IEnumerable<EnvironmentProfile> profiles)
        {
            EnvironmentProfile[] copy=profiles.ToArray();
            if (copy.Length>100) throw new ArgumentException("Massimo 100 profili.");
            foreach (var profile in copy) profile.Validate();
            SecureFiles.AtomicWrite(Path.Combine(SecureFiles.Root,"profiles.json"),Wire.Write(copy));
        }
    }
    internal interface IOperationAudit
    {
        void Record(OperationRequest request,string identity,string phase,StepResult result=null);
    }
    internal sealed class FileOperationAudit : IOperationAudit
    {
        private readonly string root;
        internal FileOperationAudit(string root=null) { this.root=root??Path.Combine(SecureFiles.Root,"audit"); }
        public void Record(OperationRequest request,string identity,string phase,StepResult result=null)
        {
            string folder=Path.Combine(root,request.Profile.Id.ToString("N"));
            SecureFiles.EnsureDirectory(folder);
            // Parameters intentionally limited: no request dump, command text, password or ciphertext.
            var entry=new { schemaVersion=1, time=DateTimeOffset.UtcNow, request.OperationId,
                request.Profile.Id, request.Profile.Customer, request.ActionId,
                target=request.ActionId.StartsWith("rsat.",StringComparison.Ordinal) || request.ActionId=="endpoint.securechannel" ?
                    Environment.MachineName : request.Target, identity, phase,
                objectGuid=request.Parameters.TryGetValue("objectGuid",out string id) ? id : null,
                outcome=result?.State.ToString(), errorCode=result?.ErrorCode };
            string path=Path.Combine(folder,DateTime.UtcNow.ToString("yyyy-MM-dd")+".jsonl");
            if (File.Exists(path) && (File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0) throw new IOException("Audit reindirizzato.");
            using (FileStream stream=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.Read))
            {
                byte[] data=Encoding.UTF8.GetBytes(Wire.Write(entry)+Environment.NewLine);
                stream.Write(data); stream.Flush(true);
            }
        }
    }
    internal static class ReportExport
    {
        public static string Json(EnvironmentProfile profile,IEnumerable<StepResult> results) =>
            Wire.Write(new {schemaVersion=1, generated=DateTimeOffset.UtcNow, customer=profile.Customer,
                domain=profile.Domain, results=results.ToArray()});
        public static string Html(EnvironmentProfile profile,IEnumerable<StepResult> results)
        {
            string safe=WebUtility.HtmlEncode(Json(profile,results));
            return "<!doctype html><html lang=\"it\"><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" "+
                "content=\"default-src 'none'; style-src 'unsafe-inline'\"><title>OnlyTweak · rapporto</title>"+
                "<style>body{font:15px system-ui;background:#121b26;color:#eee;padding:24px}pre{white-space:pre-wrap;overflow-wrap:anywhere}</style>"+
                "<h1>OnlyTweak · evidenze della sessione</h1><p>Contiene nomi e dati dell'ambiente aziendale. Conservare secondo le policy del cliente.</p><pre>"+
                safe+"</pre></html>";
        }
    }
}
