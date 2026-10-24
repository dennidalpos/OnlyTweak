using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace OnlyTweakWindows
{
    internal static partial class Tests
    {
        private static OsProfile TestProfile(int build=26100,string edition="Professional",int architecture=9,int productType=1,int major=10,string release="")
        { return new OsProfile(major,0,build,0,architecture,productType,edition,release); }
        private static AppServices TestApp(ISecurityBackend backend=null,int build=26100,string edition="Professional",FeatureAvailability availability=null)
        {
            OsProfile os=availability==null ? TestProfile(build,edition) : availability.OS;
            return new AppServices(backend??new FakeSecurityBackend(),os,availability??new FeatureAvailability(os,name=>true,target=>true));
        }
        private static void CompatibilityTests()
        {
            Test("OS: Windows 10 1809-22H2 e LTSC senza dipendenze opzionali",delegate {
                foreach (int build in new[] {17763,18362,18363,19041,19042,19043,19044,19045})
                    foreach (string edition in new[] {"Core","Professional","Enterprise","Education","EnterpriseS","IoTEnterpriseS"}) {
                        OsProfile os=TestProfile(build,edition);
                        Assert(os.Supported && !os.IsWindows11 && os.Summary.StartsWith("Windows 10"),"Profilo Windows 10 errato.");
                        AppServices app=TestApp(build:build,edition:edition);
                        Assert(app.Tweaks.AvailableDefinitions.Count()==3,"Menu classico presente su Windows 10.");
                        Assert(!app.Security.AvailableDefinitions(app.Availability).Any(d=>d.Id=="sac" || d.Id=="kernel"),"Protezioni Windows 11 presenti.");
                        Assert(app.Catalog.Accounts.AllActions().Any(a=>a.Id=="accounts.local")==os.LocalUsers,"Edizione console utenti ignorata.");
                    }
            });
            Test("OS: Windows 11, Server, ARM64, x86 e versioni escluse",delegate {
                foreach (int build in new[] {22000,22621,22631,26100,26200,26300}) {
                    OsProfile os=TestProfile(build); Assert(os.Supported && os.IsWindows11 && os.Summary.StartsWith("Windows 11"),"Windows 11 etichettato Windows 10.");
                }
                foreach (OsProfile os in new[] {TestProfile(17762),TestProfile(26100,architecture:12),TestProfile(19045,architecture:0),TestProfile(20348,productType:3),TestProfile(26100,productType:2),TestProfile(19045,major:6)})
                    Assert(!os.Supported,"Sistema escluso accettato.");
                Assert(TestProfile(17763,release:"1809").Summary.Contains("1809"),"Release persa.");
                Assert(TestProfile(17763).Summary.Contains("build 17763.0"),"Fallback senza release errato.");
                Assert(OsProfile.ReleaseName("", "1809")=="1809" && OsProfile.ReleaseName("22H2","2009")=="22H2","Fallback DisplayVersion/ReleaseId errato.");
                Assert(OsProfile.NativeArchitecture(0x8664)==9 && OsProfile.NativeArchitecture(0xaa64)==12 && OsProfile.NativeArchitecture(0x014c)==0 && OsProfile.NativeArchitecture(0)==-1,"Architettura host confusa con emulazione.");
            });
            Test("OS: tweak non pertinente rifiutato prima delle scritture",delegate {
                FakeRegistry store=new FakeRegistry(); RegistryTweakService service=new RegistryTweakService(store,TestProfile(19045));
                Throws(delegate { service.Apply(service.Definitions[0],true,Ignore); },"non applicabile");
                Throws(delegate { service.Restore(service.Definitions[0],Ignore); },"non applicabile");
                Assert(store.WriteCount==0,"Tweak Windows 11 scritto su Windows 10.");
            });
            Test("catalogo: componenti mancanti e sezioni vuote rimossi",delegate {
                FeatureAvailability availability=new FeatureAvailability(TestProfile(19045,"Core"),name=>name!="sfc.exe" && name!="dism.exe" && name!="cleanmgr.exe" && name!="netplwiz.exe",target=>target!="ms-settings:storagesense");
                AppServices app=TestApp(availability:availability);
                Assert(!app.Catalog.Maintenance.AllActions().Any() && app.Catalog.Maintenance.Children.Count==0,"Sezione vuota rimasta.");
                Assert(!app.Catalog.Accounts.AllActions().Any(a=>a.Id=="accounts.local" || a.Id=="accounts.classic" || a.Id=="accounts.close" || a.Id=="accounts.portal"),"Azione rimossa presente.");
                Assert(app.Catalog.Tools.AllActions().Count()==11,"Strumenti richiesti rimossi.");
                Assert(app.Catalog.Tools.Actions.Single(a=>a.Id=="tools.apps").Title=="App e funzionalità","Etichetta Windows 10 errata.");
            });
            Test("catalogo: rileva Explorer nella cartella Windows",delegate {
                ProcessService process=new ProcessService();
                string explorer=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe");
                Assert(process.SystemFile("explorer.exe")==explorer,"Explorer cercato fuori dalla cartella Windows.");
                Assert(process.SystemFile("sfc.exe")==Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"sfc.exe"),"Percorso degli strumenti di sistema alterato.");
                AppServices app=new AppServices(new FakeSecurityBackend(),TestProfile(),null);
                Assert(app.Catalog.ExplorerRestart.Applicable==File.Exists(explorer),"Riavvio shell nascosto malgrado Explorer disponibile.");
                FeatureAvailability absent=new FeatureAvailability(TestProfile(),name=>name!="explorer.exe",target=>true);
                Assert(!TestApp(availability:absent).Catalog.ExplorerRestart.Applicable,"Explorer assente presentato come disponibile.");
            });
            Test("destinazioni: pagina padre su Windows 10 e alternativa senza gestore sicurezza",delegate {
                FeatureAvailability win10=new FeatureAvailability(TestProfile(17763),name=>true,target=>true);
                Assert(win10.ResolveTarget("windowsdefender://smartscreenpua/")=="windowsdefender://appbrowser/","Deep link moderno usato su 1809.");
                FeatureAvailability noDefender=new FeatureAvailability(TestProfile(26100),name=>true,target=>!target.StartsWith("windowsdefender:",StringComparison.Ordinal));
                Assert(noDefender.ResolveTarget("windowsdefender://coreisolation/")=="ms-settings:windowsdefender" && noDefender.Target("windowsdefender://coreisolation/"),"Alternativa Sicurezza Windows assente.");
                FeatureAvailability absent=new FeatureAvailability(TestProfile(26100),name=>true,target=>false);
                Assert(!absent.Target("windowsdefender://coreisolation/"),"Destinazione assente disponibile.");
            });
            Test("icona: otto dimensioni ICO, risorsa gestita e anteprima",delegate {
                using (BinaryReader reader=new BinaryReader(File.OpenRead("assets/OnlyTweak.ico"))) {
                    Assert(reader.ReadUInt16()==0 && reader.ReadUInt16()==1 && reader.ReadUInt16()==8,"Intestazione ICO errata.");
                    foreach (int size in new[] {16,20,24,32,48,64,128,256}) {
                        int width=reader.ReadByte(),height=reader.ReadByte();
                        Assert((width==0 ? 256 : width)==size && width==height,"Dimensione ICO assente.");
                        reader.ReadBytes(2); Assert(reader.ReadUInt16()==1 && reader.ReadUInt16()==32,"Profondità ICO errata.");
                        uint length=reader.ReadUInt32(),offset=reader.ReadUInt32(); Assert(length>0 && offset+length<=reader.BaseStream.Length,"Immagine ICO fuori file.");
                    }
                }
                Assert(AppIcon.Value!=null && AppIcon.Value.Width==32 && AppIcon.Small!=null && AppIcon.Small.Width==16,"Risorsa finestra assente.");
                using (MainForm home=new MainForm(TestApp())) Assert(home.ShowInTaskbar && Object.ReferenceEquals(home.Icon,AppIcon.Value),"Icona titolo/taskbar non assegnata.");
                using (Bitmap preview=new Bitmap("assets/OnlyTweak.png")) Assert(preview.Width==512 && preview.GetPixel(0,0).A==0,"Anteprima o trasparenza errata.");
            });
        }
        private static void RenderProfiles(string folder)
        {
            foreach (int build in new[] {17763,19044,19045,22000,22621,26100}) {
                FakeSecurityBackend backend=new FakeSecurityBackend(); AppServices app=TestApp(backend,build);
                using (MainForm home=new MainForm(app))
                using (AppearanceDialog explorer=new AppearanceDialog(app))
                using (SecurityDialog security=new SecurityDialog(app,false)) {
                    security.DisplayStates(backend.Environment);
                    foreach (DarkForm form in new DarkForm[] {home,explorer,security}) {
                        form.ShowInTaskbar=false; form.Show(); Application.DoEvents(); form.Update();
                        CaptureForm(form,Path.Combine(folder,"os-"+build+"-"+form.GetType().Name+".png")); form.Close();
                    }
                }
            }
        }
    }
}
