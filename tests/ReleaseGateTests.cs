using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace OnlyTweakWindows
{
    internal static partial class Tests
    {
        private static string PsLiteral(string text) => "'"+text.Replace("'","''")+"'";
        private static string RunInspection(string file,params string[] arguments)
        {
            ProcessStartInfo start=new ProcessStartInfo(file) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,
                RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
            foreach(string arg in arguments) start.ArgumentList.Add(arg);
            using Process process=Process.Start(start);
            var output=process.StandardOutput.ReadToEndAsync(); var error=process.StandardError.ReadToEndAsync();
            if(!process.WaitForExit(30000)) { process.Kill(true); process.WaitForExit(); throw new Exception("Timeout fixture rilascio."); }
            string result=output.GetAwaiter().GetResult(),details=error.GetAwaiter().GetResult();
            Assert(process.ExitCode==0,"Fixture rilascio: "+details+" "+result);
            return result;
        }
        private static string ReleasePowerShell(string script) => RunInspection("pwsh","-NoProfile","-NonInteractive","-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop'; "+script)));
        private static void ReleaseGateTests()
        {
            Test("consegna: bundle effettivo, sorgenti e runtime risolti associati alla ricevuta",()=> {
                string root=Directory.GetCurrentDirectory();
                string output=ReleasePowerShell("Import-Module "+PsLiteral(Path.Combine(root,"scripts/ReleaseEvidence.psm1"))+"; $b=Get-OnlyTweakPublishedBuild "+PsLiteral(root)+"; $b.Evidence.Metadata | ConvertTo-Json -Depth 12 -Compress");
                using JsonDocument document=JsonDocument.Parse(output.Trim());
                var packs=document.RootElement.GetProperty("RuntimePacks").EnumerateArray().Select(p=>p.GetProperty("Name").GetString()).ToArray();
                Assert(packs.Contains("Microsoft.NETCore.App.Runtime.win-x64") && packs.Contains("Microsoft.WindowsDesktop.App.Runtime.win-x64") &&
                    !packs.Any(p=>p.Contains("AspNetCore",StringComparison.Ordinal)),"Inventario usa download non inclusi nel bundle.");
                Assert(!output.Contains(root,StringComparison.OrdinalIgnoreCase),"Percorso della postazione nei metadati consegnati.");
            });
            Test("consegna: EXE/sorgente/assets divergenti e script incorporato non firmato bloccati",()=> {
                string root=Directory.GetCurrentDirectory(),fixture=Path.Combine(Path.GetTempPath(),"OnlyTweak-release-test-"+Guid.NewGuid().ToString("N"));
                var owned=new List<string>();
                void Copy(string relative) {
                    string target=Path.Combine(fixture,relative); Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(Path.Combine(root,relative),target); owned.Add(target);
                }
                try {
                    using JsonDocument evidence=JsonDocument.Parse(RunInspection(Path.GetFullPath("dist/OnlyTweakWindows.exe"),"--release-inspect"));
                    foreach(JsonElement source in evidence.RootElement.GetProperty("Metadata").GetProperty("SourceFiles").EnumerateArray()) Copy(source.GetProperty("Name").GetString());
                    Copy("obj/project.assets.json"); Copy("dist/OnlyTweakWindows.exe"); Copy("artifacts/build-evidence/publish.json");
                    string delivery=Path.Combine(fixture,"artifacts/delivery/2.0.0");
                    owned.Add(Path.Combine(delivery,"manifest.json")); owned.Add(Path.Combine(delivery,"OnlyTweakWindows.exe"));
                    string prepare="& "+PsLiteral(Path.Combine(fixture,"scripts/prepare-release.ps1"))+" -RequireSigned; ";
                    string mock="function global:Get-AuthenticodeSignature { param($LiteralPath,$Content,$SourcePathOrExtension) [pscustomobject]@{Status='Valid'} }; ";
                    // Signature statuses are simulated. This proves the gate logic, not Authenticode qualification.
                    ReleasePowerShell(mock+prepare);
                    using (JsonDocument manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(delivery,"manifest.json"))))
                        Assert(manifest.RootElement.GetProperty("embeddedScripts").GetArrayLength()==4,"Firme risorse non inventariate.");
                    void Reject(string script,string expected) {
                        string output=ReleasePowerShell(mock+"try { "+script+"; throw 'GateAcceptedInvalidFixture' } catch { if ($_.Exception.Message -notlike "+PsLiteral("*"+expected+"*")+") { throw }; 'REJECTED' }");
                        Assert(output.Contains("REJECTED",StringComparison.Ordinal),"Gate non fallito.");
                    }
                    string sourcePath=Path.Combine(fixture,"scripts/enterprise/Operations.ps1"); byte[] original=File.ReadAllBytes(sourcePath);
                    File.AppendAllText(sourcePath,"\r\n# source signed/changed after publish\r\n");
                    Reject(prepare,"Sorgente diverso"); File.WriteAllBytes(sourcePath,original);
                    string assetsPath=Path.Combine(fixture,"obj/project.assets.json"); original=File.ReadAllBytes(assetsPath);
                    File.AppendAllText(assetsPath," "); Reject(prepare,"Assets diversi"); File.WriteAllBytes(assetsPath,original);
                    string exePath=Path.Combine(fixture,"dist/OnlyTweakWindows.exe");
                    using(FileStream bytes=new FileStream(exePath,FileMode.Append)) bytes.WriteByte(0);
                    Reject(prepare,"EXE diverso"); File.Copy(Path.Combine(root,"dist/OnlyTweakWindows.exe"),exePath,true);
                    string receipt=Path.Combine(fixture,"artifacts/build-evidence/publish.json"); original=File.ReadAllBytes(receipt);
                    File.Delete(receipt); Reject(prepare,"Ricevuta build assente"); File.WriteAllBytes(receipt,original);
                    string module="Import-Module "+PsLiteral(Path.Combine(fixture,"scripts/ReleaseEvidence.psm1"))+" -Force; ";
                    Reject(module+"$v=(Invoke-OnlyTweakInspection "+PsLiteral(exePath)+").Value; $bytes=[Text.Encoding]::UTF8.GetBytes('different embedded resource'); $v.Scripts[0].Content=[Convert]::ToBase64String($bytes); $v.Scripts[0].Sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)); Assert-OnlyTweakBuild "+PsLiteral(fixture)+" $v","Risorsa incorporata diversa");
                    Reject(module+"$v=(Invoke-OnlyTweakInspection "+PsLiteral(exePath)+").Value; $v.Scripts=@($v.Scripts)+$v.Scripts[0]; Assert-OnlyTweakBuild "+PsLiteral(fixture)+" $v","Risorse script incomplete");
                    string unsigned="function global:Get-AuthenticodeSignature { param($LiteralPath,$Content,$SourcePathOrExtension) [pscustomobject]@{Status=$(if($null -ne $Content){'NotSigned'}else{'Valid'})} }; ";
                    string rejected=ReleasePowerShell(unsigned+"try { "+prepare+"throw 'UnsignedAccepted' } catch { if ($_.Exception.Message -notlike '*risorsa incorporata*') { throw }; 'REJECTED_UNSIGNED_RESOURCE' }");
                    Assert(rejected.Contains("REJECTED_UNSIGNED_RESOURCE",StringComparison.Ordinal),"Firma EXE sostituisce firma script.");
                } finally {
                    string resolved=Path.GetFullPath(fixture),temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                    if(!resolved.StartsWith(temp,StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("OnlyTweak-release-test-",StringComparison.Ordinal))
                        throw new IOException("Pulizia fixture fuori TEMP.");
                    // Only files created by this fixture, then empty directories. No recursive deletion.
                    foreach(string file in owned) if(File.Exists(file)) File.Delete(file);
                    foreach(string folder in owned.Select(Path.GetDirectoryName).SelectMany(ParentsUntilFixture).Distinct().OrderByDescending(p=>p.Length))
                        if(Directory.Exists(folder)) Directory.Delete(folder,false);
                    if(Directory.Exists(fixture)) Directory.Delete(fixture,false);
                    IEnumerable<string> ParentsUntilFixture(string folder) {
                        while(folder!=fixture && folder.StartsWith(fixture+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) {
                            yield return folder; folder=Path.GetDirectoryName(folder);
                        }
                    }
                }
            });
        }
    }
}
