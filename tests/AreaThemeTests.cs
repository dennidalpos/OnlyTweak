using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace OnlyTweakWindows
{
    internal static partial class Tests
    {
        private static double Contrast(Color a,Color b)
        {
            double L(Color c) {
                double Channel(byte v) {double n=v/255D;return n<=0.04045 ? n/12.92 : Math.Pow((n+0.055)/1.055,2.4);}
                return 0.2126*Channel(c.R)+0.7152*Channel(c.G)+0.0722*Channel(c.B);
            }
            double x=L(a),y=L(b);return (Math.Max(x,y)+0.05)/(Math.Min(x,y)+0.05);
        }
        private static void AreaThemeTests()
        {
            Test("UI colori: contrasto testo, icone e focus in tutti gli stati",()=> {
                double minimum=100;
                foreach(var area in Enum.GetValues<AppArea>()) {
                    using var button=(DarkButton)Theme.NavigationButton("Ingresso",null,area);
                    var palette=button.Palette;
                    foreach(var state in new[]{(false,false),(true,false),(true,true)}) {
                        Color background=button.StateBackground(state.Item1,state.Item2);
                        minimum=Math.Min(minimum,Contrast(palette.Secondary,background));
                        Assert(Contrast(Theme.Text,background)>=4.5 && Contrast(palette.Secondary,background)>=4.5,"Testo poco leggibile: "+area);
                        Assert(Contrast(palette.Accent,background)>=3 && Contrast(palette.Accent,Theme.Background)>=3,"Icona/focus poco visibile: "+area);
                    }
                    Assert(palette.Surface!=palette.Hover && palette.Hover!=palette.Pressed,"Stati indistinguibili.");
                    button.Enabled=false;
                    Assert(button.StateBackground(true,true)==Theme.Surface && Contrast(Theme.Muted,Theme.Surface)>=4.5,"Stato disabilitato illeggibile.");
                }
                using var primary=(DarkButton)Theme.Button("Esegui",null);primary.BackColor=Theme.Accent;primary.ForeColor=Theme.Background;
                Assert(Contrast(primary.ForeColor,primary.StateBackground(true,true))>=4.5,"Azione primaria premuta illeggibile.");
                Console.WriteLine("Contrasto minimo descrizioni per area: "+minimum.ToString("F2")+":1");
            });
            Test("UI colori: navigazione eredita area, preserva stato e segnali di conferma",()=> {
                var app=TestApp();using var host=new MainForm(app);ShowPage(host);
                var home=host.Navigation.Current;
                var tiles=Descendants(home).OfType<DarkButton>().Where(b=>b.Glyph!=null && b.OpensArea).ToArray();
                Assert(tiles.Length==6 && tiles.Select(t=>t.Area).Distinct().Count()==6,"Ingressi non distinti.");
                Assert(tiles.Select(t=>t.Palette.Surface).Distinct().Count()==6,"Fondi non distinti.");
                host.Navigation.Push(new TweakPage(app));
                var personalization=host.Navigation.Current;
                var convenience=Descendants(personalization).OfType<Button>().Single(b=>b.Text=="Comodità e mantieni sveglio");convenience.PerformClick();
                Assert(host.Navigation.Current.Area==AppArea.Personalization,"Comodità senza colore.");
                Descendants(host.Navigation.Current).OfType<Button>().Single(b=>b.Text=="Mantieni sveglio").PerformClick();
                Assert(host.Navigation.Current.Area==AppArea.Personalization,"Awake senza colore.");
                host.Navigation.Push(new TextDialog("Dettaglio","Testo conservato"));
                Assert(host.Navigation.Current.Area==AppArea.Personalization,"Dettaglio senza colore.");
                object[] shortcut={Message.Create(host.Handle,0,IntPtr.Zero,IntPtr.Zero),Keys.Alt|Keys.Left};
                var processKey=typeof(MainForm).GetMethod("ProcessCmdKey",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                Assert((bool)processKey.Invoke(host,shortcut),"Alt+Sinistra non gestito.");
                Application.DoEvents();
                Assert(host.Navigation.Current is AwakePage && host.Navigation.Current.Area==AppArea.Personalization,"Alt+Sinistra perde il colore.");
                using(var confirm=new ConfirmDialog("Fixture","Nessuna operazione")) {
                    host.Navigation.Push(confirm);
                    Assert(confirm.Area==AppArea.Personalization && Descendants(confirm).OfType<GlyphLabel>().Any(l=>l.Glyph==Glyphs.Warning && l.GlyphColor==Theme.Warning),"Conferma priva di avviso.");
                    Assert(Descendants(confirm).OfType<DarkButton>().Single(b=>b.Caution).ForeColor==Theme.Warning,"Conferma ricolorata.");
                    Await(host.Navigation.BackAsync());
                }
                while(host.Navigation.Depth>1) Await(host.Navigation.BackAsync());
                Assert(ReferenceEquals(home,host.Navigation.Current) && home.Area==AppArea.Neutral,"Colore home contaminato.");
                host.Navigation.Push(new GamingPage(app));
                var gaming=host.Navigation.Current;
                Descendants(gaming).OfType<ComboBox>().Single(c=>c.AccessibleName=="Livello gaming").SelectedIndex=2;
                Assert(Descendants(gaming).OfType<DarkButton>().Where(b=>b.OpensArea).All(b=>b.Area==AppArea.Gaming),"Controlli gaming dinamici senza colore.");
                host.Navigation.Push(new TextDialog("Cronologia","Fixture",Glyphs.History,AppArea.Neutral));
                Assert(host.Navigation.Current.Area==AppArea.Neutral,"Cronologia ricolorata.");
                Await(host.Navigation.BackAsync());Assert(ReferenceEquals(gaming,host.Navigation.Current),"Pagina ricreata al ritorno.");
                foreach(var menu in new[]{app.Catalog.Network,app.Catalog.Maintenance,app.Catalog.Spool,app.Catalog.Tools}) {
                    host.Navigation.Push(new MenuDialog(app,menu));
                    Assert(host.Navigation.Current.Area==menu.Area,"Area del menu dipendente dalla provenienza.");
                    host.Navigation.Push(new OperationDialog(app,menu.AllActions().First()));
                    Assert(host.Navigation.Current.Area==menu.Area,"Operazione senza colore del menu.");
                    Await(host.Navigation.BackAsync());Await(host.Navigation.BackAsync());
                }
                using(var protections=new SecurityDialog(app,false)) {
                    host.Navigation.Push(protections);
                    Assert(protections.Area==AppArea.Advanced,"Protezioni senza area avanzata.");
                    Await(host.Navigation.BackAsync());
                }
                ClosePage(host);
            });
            Test("UI colori: home minima, scorrimento e focus senza troncamenti",()=> {
                var app=TestApp();using var host=new MainForm(app);ShowPage(host);
                host.ClientSize=new Size(660,540);Application.DoEvents();
                var tiles=Descendants(host).OfType<DarkButton>().Where(b=>b.Glyph!=null && b.OpensArea).ToArray();
                Assert(tiles.Select(t=>t.Left).Distinct().Count()==1,"Home minima ancora su due colonne.");
                foreach(var tile in tiles) {
                    Assert(tile.Height>=tile.ContentHeight(tile.Width),"Descrizione troncata: "+tile.Text);
                    using var title=new Font(tile.Font,FontStyle.Bold);
                    using var icon=Theme.IconFont(tile.Font.SizeInPoints*1.6F);
                    int available=tile.Width-tile.Padding.Horizontal-42-(icon==null ? 0 : icon.Height+8);
                    Assert(TextRenderer.MeasureText(tile.Text,title).Width<=available,"Titolo troncato: "+tile.Text);
                }
                var stack=Descendants(host).OfType<StackPanel>().Single();tiles[^1].Focus();Application.DoEvents();
                Assert(stack.ScrollOffset>0 && tiles[^1].Focused,"Ultima area non raggiungibile da tastiera.");
                ClosePage(host);
            });
        }
    }
}
