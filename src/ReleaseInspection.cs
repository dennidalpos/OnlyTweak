using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OnlyTweakWindows
{
    internal static class ReleaseInspection
    {
        private static string[] Scripts => Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(name=>name.StartsWith("OnlyTweak.",StringComparison.Ordinal) &&
                (name.EndsWith(".ps1",StringComparison.OrdinalIgnoreCase) || name.EndsWith(".psm1",StringComparison.OrdinalIgnoreCase)))
            .Select(name=>name.Substring("OnlyTweak.".Length)).OrderBy(name=>name,StringComparer.Ordinal).ToArray();
        private static byte[] Resource(string name)
        {
            using Stream source=Assembly.GetExecutingAssembly().GetManifestResourceStream("OnlyTweak."+name) ??
                throw new FileNotFoundException("Metadati di build assenti: ricompilare.");
            using MemoryStream bytes=new MemoryStream(); source.CopyTo(bytes); return bytes.ToArray();
        }
        // Developer inspection only: no UI, credentials, profile storage, scripts or system operations.
        public static bool TryRun(string[] args)
        {
            if (args.Length!=1 || args[0]!="--release-inspect") return false;
            try {
                using JsonDocument metadata=JsonDocument.Parse(Resource("BuildEvidence.json"));
                string json=Wire.Write(new {SchemaVersion=1,Metadata=metadata.RootElement,Scripts=Scripts.Select(name=> {
                    byte[] bytes=Resource(name);
                    return new {Name=name,Sha256=Convert.ToHexString(SHA256.HashData(bytes)),Content=Convert.ToBase64String(bytes)};
                }).ToArray()});
                using Stream output=Console.OpenStandardOutput();
                byte[] payload=Encoding.UTF8.GetBytes(json); output.Write(payload); output.Flush();
            } catch (Exception ex) {
                Environment.ExitCode=1;
                using Stream error=Console.OpenStandardError();
                error.Write(Encoding.UTF8.GetBytes("ReleaseInspectionFailed:"+ex.GetType().Name));
            }
            return true;
        }
    }
}
