using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OnlyTweakWindows
{
    // Codici di Segoe Fluent Icons (compatibili con Segoe MDL2 Assets):
    // https://learn.microsoft.com/en-us/windows/apps/design/style/segoe-fluent-icons-font
    internal static class Glyphs
    {
        public const string Print="",Personalize="",Network="",Repair="",Shield="",Contact="",
            Apps="",History="",Info="",Warning="",OpenWindow="",Diagnostic="",Settings="",
            Delete="",Document="";
        public static string ForAction(ActionSpec action)
        { return action.Confirmation!=null ? Warning : action.OpensWindow ? OpenWindow : Diagnostic; }
    }
    internal static class Theme
    {
        // Font di sistema di Windows 11, con MDL2 come alternativa; null se assenti: le icone vengono omesse.
        internal static string IconFamily=ResolveIconFamily();
        private static string ResolveIconFamily()
        {
            foreach (string name in new[] { "Segoe Fluent Icons","Segoe MDL2 Assets" })
                using (Font font=new Font(name,10F)) if (String.Equals(font.Name,name,StringComparison.OrdinalIgnoreCase)) return name;
            return null;
        }
        public static Font IconFont(float size) { return IconFamily==null ? null : new Font(IconFamily,size); }
        public static readonly Color Background=Color.FromArgb(16,22,31), Surface=Color.FromArgb(25,34,47),
            Raised=Color.FromArgb(33,46,61), Text=Color.FromArgb(235,241,247), Muted=Color.FromArgb(158,175,195),
            Accent=Color.FromArgb(104,226,195), Border=Color.FromArgb(117,138,163), Warning=Color.FromArgb(255,198,119),
            Track=Color.FromArgb(18,27,38), Thumb=Color.FromArgb(111,132,157), Selection=Color.FromArgb(28,65,67);
        public static Button Button(string text,EventHandler clicked)
        {
            Button button=new DarkButton { Text=text,BackColor=Surface,ForeColor=Text,Font=new Font("Segoe UI",10F),
                Size=new Size(150,40),MinimumSize=new Size(120,40),AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,
                Margin=new Padding(5),Cursor=Cursors.Hand,UseVisualStyleBackColor=false,FlatStyle=FlatStyle.Flat };
            if (clicked!=null) button.Click+=clicked;
            return button;
        }
        public static Button Card(string title,string description,EventHandler clicked,string glyph=null)
        {
            DarkButton button=(DarkButton)Button(title,clicked);
            button.Description=description; button.AccessibleDescription=description; button.Glyph=glyph;
            button.AutoSize=false; button.Height=74; button.TextAlign=ContentAlignment.MiddleLeft;
            button.Padding=new Padding(16,8,16,8); button.Margin=new Padding(0,0,0,10); button.UseMnemonic=false;
            return button;
        }
        public static Label Label(string text,Color color)
        { return new Label { Text=text,AutoSize=true,ForeColor=color,BackColor=Color.Transparent,Margin=new Padding(0,0,0,10),UseMnemonic=false }; }
        public static Label Heading(string text,string glyph)
        {
            GlyphLabel label=new GlyphLabel { Text=text,AutoSize=true,ForeColor=Text,BackColor=Color.Transparent,Margin=new Padding(0,0,0,8),UseMnemonic=false,Glyph=glyph,GlyphColor=Accent };
            label.Font=new Font("Segoe UI",22F,FontStyle.Bold); return label;
        }
        public static Label Section(string text,string glyph)
        {
            GlyphLabel label=new GlyphLabel { Text=text.ToUpperInvariant(),AutoSize=true,ForeColor=Muted,BackColor=Color.Transparent,UseMnemonic=false,Glyph=glyph,GlyphColor=Muted };
            label.Font=new Font("Segoe UI",9F,FontStyle.Bold); label.Margin=new Padding(0,14,0,10); return label;
        }
        public static DarkOutput Output() { return new DarkOutput { Dock=DockStyle.Fill }; }
        public static bool TryCopy(string text)
        {
            if (String.IsNullOrEmpty(text)) return false;
            for (int i=0; i<3; i++)
            {
                try { Clipboard.SetText(text); return true; }
                catch (ExternalException) { System.Threading.Thread.Sleep(50); }
                catch (Exception) { break; }
            }
            return false;
        }
        public static GraphicsPath Round(Rectangle rectangle,int radius)
        {
            GraphicsPath path=new GraphicsPath(); int size=Math.Max(2,Math.Min(radius*2,Math.Min(rectangle.Width,rectangle.Height)));
            path.AddArc(rectangle.Left,rectangle.Top,size,size,180,90); path.AddArc(rectangle.Right-size,rectangle.Top,size,size,270,90);
            path.AddArc(rectangle.Right-size,rectangle.Bottom-size,size,size,0,90); path.AddArc(rectangle.Left,rectangle.Bottom-size,size,size,90,90); path.CloseFigure(); return path;
        }
    }
    // Etichetta con glifo opzionale prima del testo. GDI aggiunge un margine laterale proporzionale al
    // carattere: a 22pt il titolo rientrerebbe rispetto alle righe a 8-10pt, quindi il testo usa il
    // margine del carattere del contenitore.
    internal sealed class GlyphLabel : Label
    {
        private const TextFormatFlags Flags=TextFormatFlags.NoPadding|TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix;
        private string glyph;
        private Font iconFont;
        public Color GlyphColor=Theme.Accent;
        public string Glyph { get { return glyph; } set { glyph=value; ResetIcon(); } }
        private Font Icon
        {
            get
            {
                if (iconFont==null && !String.IsNullOrEmpty(glyph)) iconFont=Theme.IconFont(Font.SizeInPoints*0.8F);
                return iconFont;
            }
        }
        private void ResetIcon() { if (iconFont!=null) { iconFont.Dispose(); iconFont=null; } PerformLayout(); Invalidate(); }
        protected override void OnFontChanged(EventArgs e) { ResetIcon(); base.OnFontChanged(e); }
        protected override void Dispose(bool disposing) { if (disposing && iconFont!=null) { iconFont.Dispose(); iconFont=null; } base.Dispose(disposing); }
        // Margine GDI delle Label (GlyphOverhangPadding): circa un sesto dell'altezza del carattere.
        private int Inset()
        { return (int)Math.Ceiling((Parent==null ? Font : Parent.Font).Height/6F); }
        private int IconWidth()
        {
            Font icon=Icon; if (icon==null) return 0;
            // I glifi Fluent occupano circa l'altezza della riga del font icone (misurata con lo stesso DPI del disegno).
            return icon.Height+Math.Max(4,Font.Height/4);
        }
        public override Size GetPreferredSize(Size proposedSize)
        {
            int offset=Inset()+IconWidth(),width=MaximumSize.Width>0 ? Math.Max(1,MaximumSize.Width-Padding.Horizontal-offset) : Int32.MaxValue;
            Size text=TextRenderer.MeasureText(Text,Font,new Size(width,Int32.MaxValue),Flags);
            return new Size(text.Width+Padding.Horizontal+offset,Math.Max(text.Height,Font.Height)+Padding.Vertical);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            int inset=Inset(),icon=IconWidth();
            if (icon>0)
                TextRenderer.DrawText(e.Graphics,glyph,Icon,new Rectangle(Padding.Left+inset,Padding.Top,icon,Font.Height),GlyphColor,
                    TextFormatFlags.NoPadding|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.Left);
            int x=Padding.Left+inset+icon;
            TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(x,Padding.Top,Math.Max(1,Width-x-Padding.Right),Height-Padding.Vertical),ForeColor,Flags);
        }
    }
    // Icona Info focalizzabile: il dettaglio compare al passaggio del mouse, al clic o con Invio/Spazio/F1.
    // Il tooltip richiesto esplicitamente ha una durata limitata e si chiude all'uscita del focus o quando
    // la finestra si sposta, si disattiva o si chiude: non resta sospeso sopra altre finestre.
    internal sealed class InfoIcon : Control
    {
        internal const int DetailDuration=10000;
        private readonly ToolTip tips;
        private bool hovered;
        internal void ShowDetail() { if (IsHandleCreated && Visible) tips.Show(Text,this,0,Height+2,DetailDuration); }
        internal void HideDetail() { if (IsHandleCreated) tips.Hide(this); }
        protected override void OnClick(EventArgs e) { Focus(); ShowDetail(); base.OnClick(e); }
        protected override bool IsInputKey(Keys keyData) { return keyData==Keys.Enter || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode==Keys.Enter || e.KeyCode==Keys.Space || e.KeyCode==Keys.F1) { ShowDetail(); e.Handled=true; }
            base.OnKeyDown(e);
        }
        public InfoIcon(ToolTip tips,string detail)
        {
            this.tips=tips;
            SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.Selectable|ControlStyles.SupportsTransparentBackColor,true);
            BackColor=Color.Transparent; TabStop=true; Cursor=Cursors.Help; Margin=new Padding(4,0,0,0); Size=new Size(28,28);
            AccessibleName="Dettagli"; AccessibleDescription=detail; AccessibleRole=AccessibleRole.HelpBalloon; Text=detail;
            tips.SetToolTip(this,detail);
        }
        protected override void OnMouseEnter(EventArgs e) { hovered=true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered=false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnter(EventArgs e) { Invalidate(); base.OnEnter(e); }
        protected override void OnLeave(EventArgs e) { Invalidate(); HideDetail(); base.OnLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Color color=Focused||hovered ? Theme.Accent : Theme.Muted;
            using (Font icon=Theme.IconFont(12F))
            {
                if (icon!=null) TextRenderer.DrawText(e.Graphics,Glyphs.Info,icon,ClientRectangle,color,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPadding);
                else TextRenderer.DrawText(e.Graphics,"i",Font,ClientRectangle,color,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
            }
            if (Focused&&ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(1,1,Width-3,Height-3),Theme.Accent,Theme.Background);
        }
    }
    internal sealed class DarkButton : Button
    {
        public string Description,Glyph;
        public bool Caution;
        public Color GlyphColor=Theme.Muted;
        private bool hovered,pressed;
        public DarkButton() { SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw,true); }
        public override Size GetPreferredSize(Size proposedSize)
        {
            if (!String.IsNullOrEmpty(Description)) return base.GetPreferredSize(proposedSize);
            Size text=TextRenderer.MeasureText(Text,Font,Size.Empty,TextFormatFlags.SingleLine);
            return new Size(Math.Max(MinimumSize.Width,text.Width+Padding.Horizontal+32),Math.Max(MinimumSize.Height,text.Height+Padding.Vertical+18));
        }
        protected override void OnMouseEnter(EventArgs e) { hovered=true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered=false; pressed=false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed=true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed=false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent==null ? Theme.Background : Parent.BackColor); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            Color emphasis=Caution ? Theme.Warning : Theme.Accent;
            Color background=BackColor;
            if (Enabled&&(hovered||pressed)) background=BackColor==Theme.Accent ? Color.FromArgb(132,237,210) : Theme.Raised;
            using (GraphicsPath shape=Theme.Round(new Rectangle(1,1,Width-3,Height-3),8))
            using (SolidBrush fill=new SolidBrush(background))
            using (Pen border=new Pen(Enabled&&(Caution||Focused||hovered) ? emphasis : Theme.Border))
            { e.Graphics.FillPath(fill,shape); e.Graphics.DrawPath(border,shape); }
            Rectangle text=new Rectangle(Padding.Left+10,Padding.Top+3,Math.Max(1,Width-Padding.Horizontal-20),Math.Max(1,Height-Padding.Vertical-6));
            Color color=Enabled ? ForeColor : Theme.Muted;
            TextFormatFlags flags=TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter;
            flags|=TextAlign==ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
            if (!UseMnemonic) flags|=TextFormatFlags.NoPrefix; else if (!ShowKeyboardCues) flags|=TextFormatFlags.HidePrefix;
            if (String.IsNullOrEmpty(Description)) TextRenderer.DrawText(e.Graphics,Text,Font,text,color,flags);
            else
            {
                if (!String.IsNullOrEmpty(Glyph))
                    using (Font icon=Theme.IconFont(Font.SizeInPoints*1.6F))
                        if (icon!=null)
                        {
                            // Colonna icona a sinistra; titolo e descrizione si spostano a destra.
                            int column=icon.Height+8;
                            TextRenderer.DrawText(e.Graphics,Glyph,icon,new Rectangle(text.X,0,column,Height),Enabled ? GlyphColor : Theme.Border,
                                TextFormatFlags.NoPadding|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.Left);
                            text=new Rectangle(text.X+column,text.Y,Math.Max(1,text.Width-column),text.Height);
                        }
                using (Font titleFont=new Font(Font,FontStyle.Bold))
                using (Font descriptionFont=new Font(Font.FontFamily,Math.Max(8F,Font.Size-1F)))
                {
                    int titleHeight=titleFont.Height+4;
                    TextRenderer.DrawText(e.Graphics,Text,titleFont,new Rectangle(text.X,text.Y,text.Width,titleHeight),color,flags);
                    TextRenderer.DrawText(e.Graphics,Description,descriptionFont,new Rectangle(text.X,text.Y+titleHeight,text.Width,Math.Max(1,text.Height-titleHeight)),Theme.Muted,
                        TextFormatFlags.Left|TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
                }
            }
            if (Focused&&ShowFocusCues&&Enabled) ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(6,6,Width-13,Height-13),BackColor==Theme.Accent ? Theme.Background : emphasis,background);
        }
    }
    internal class DarkForm : Form
    {
        protected readonly TableLayoutPanel Root;
        protected readonly FlowLayoutPanel Footer;
        protected readonly Button CloseAction;
        protected readonly Panel Content;
        protected readonly Label Subtitle,Feedback;
        // Un solo tooltip per finestra: dettagli e avvertenze estese restano disponibili senza occupare spazio.
        internal readonly ToolTip Tips;
        protected readonly GlyphLabel Heading;
        protected override void Dispose(bool disposing) { if (disposing) Tips.Dispose(); base.Dispose(disposing); }
        public DarkForm(string title,string subtitle) : this(title,subtitle,null,null) { }
        public DarkForm(string title,string subtitle,string glyph,string detail)
        {
            Tips=new ToolTip { AutoPopDelay=InfoIcon.DetailDuration,InitialDelay=400,ReshowDelay=100,OwnerDraw=true };
            Tips.Popup+=delegate(object sender,PopupEventArgs e) {
                Size size=TextRenderer.MeasureText(Tips.GetToolTip(e.AssociatedControl)??"",Font,new Size(420,0),TextFormatFlags.WordBreak);
                e.ToolTipSize=new Size(size.Width+20,size.Height+14);
            };
            Tips.Draw+=delegate(object sender,DrawToolTipEventArgs e) {
                using (SolidBrush fill=new SolidBrush(Theme.Surface)) e.Graphics.FillRectangle(fill,e.Bounds);
                using (Pen pen=new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen,0,0,e.Bounds.Width-1,e.Bounds.Height-1);
                TextRenderer.DrawText(e.Graphics,e.ToolTipText,Font,new Rectangle(10,7,e.Bounds.Width-20,e.Bounds.Height-14),Theme.Text,TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
            };
            WheelRouter.Install();
            Text=title=="OnlyTweakWindows" ? title : title+" · OnlyTweakWindows";
            BackColor=Theme.Background; ForeColor=Theme.Text; Font=new Font("Segoe UI",10F);
            AutoScaleDimensions=new SizeF(96F,96F); AutoScaleMode=AutoScaleMode.Dpi;
            ClientSize=new Size(920,740); MinimumSize=new Size(660,540); StartPosition=FormStartPosition.CenterParent; Icon=AppIcon.Value;
            Root=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Margin=Padding.Empty,Padding=Padding.Empty };
            Root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            Root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); Root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            TableLayoutPanel header=new TableLayoutPanel { Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,Padding=new Padding(26,22,26,8),Margin=Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            // L'eyebrow identifica l'app solo nella home; nelle finestre secondarie basta il titolo della finestra.
            if (title=="OnlyTweakWindows")
            {
                Label eyebrow=Theme.Label("ONLYTWEAK  /  WINDOWS",Theme.Accent); eyebrow.Font=new Font("Segoe UI",8F,FontStyle.Bold);
                header.Controls.Add(eyebrow,0,0); header.SetColumnSpan(eyebrow,2);
            }
            Label heading=Theme.Heading(title,glyph); Heading=(GlyphLabel)heading;
            Subtitle=Theme.Label(subtitle,Theme.Muted);
            header.Controls.Add(heading,0,1); header.SetColumnSpan(heading,2); header.Controls.Add(Subtitle,0,2);
            if (!String.IsNullOrEmpty(detail)) { info=new InfoIcon(Tips,detail); header.Controls.Add(info,1,2); }
            else header.SetColumnSpan(Subtitle,2);
            header.SizeChanged+=delegate {
                int width=Math.Max(100,header.ClientSize.Width-header.Padding.Horizontal); heading.MaximumSize=new Size(width,0);
                Subtitle.MaximumSize=new Size(Math.Max(100,width-(info==null ? 0 : info.Width+info.Margin.Horizontal)),0);
            };
            Content=new Panel { Dock=DockStyle.Fill,Margin=Padding.Empty };
            Footer=new FlowLayoutPanel { Dock=DockStyle.Fill,AutoSize=true,FlowDirection=FlowDirection.RightToLeft,WrapContents=true,Padding=new Padding(20,10,20,12),Margin=Padding.Empty };
            CloseAction=Theme.Button("&Chiudi",delegate { Close(); }); Footer.Controls.Add(CloseAction); CancelButton=CloseAction;
            Feedback=Theme.Label("",Theme.Muted); Feedback.Dock=DockStyle.Top; Feedback.Padding=new Padding(26,9,26,0); Feedback.Visible=false;
            Footer.Paint+=delegate(object sender,PaintEventArgs e) { using (Pen pen=new Pen(Theme.Border)) e.Graphics.DrawLine(pen,24,0,Footer.Width-24,0); };
            Root.Controls.Add(header,0,0); Root.Controls.Add(Content,0,1); Root.Controls.Add(Feedback,0,2); Root.Controls.Add(Footer,0,3); Controls.Add(Root);
            // L'icona Info segue contenuti e pulsanti nell'ordine di tabulazione: all'apertura il focus non finisce sull'intestazione.
            header.TabIndex=Root.Controls.Count;
            Move+=delegate { HideTips(); }; ResizeBegin+=delegate { HideTips(); };
        }
        private readonly InfoIcon info;
        // Chiude sia i tooltip al passaggio del mouse sia il dettaglio richiesto dall'icona Info.
        internal void HideTips()
        {
            if (info!=null) info.HideDetail();
            if (Tips.Active) { Tips.Active=false; Tips.Active=true; }
        }
        protected override void OnFormClosing(FormClosingEventArgs e) { HideTips(); base.OnFormClosing(e); }
        protected void SetFeedback(string text)
        { if (IsDisposed) return; Feedback.Text=text; Feedback.Visible=!String.IsNullOrEmpty(text); Feedback.MaximumSize=new Size(Math.Max(100,ClientSize.Width-16),0); }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { SendMessage(Handle,0x0080,(IntPtr)0,AppIcon.Small.Handle); } catch (EntryPointNotFoundException) { }
            ApplyFrame();
        }
        protected override void OnActivated(EventArgs e) { base.OnActivated(e); ApplyFrame(); }
        protected override void OnDeactivate(EventArgs e) { HideTips(); base.OnDeactivate(e); ApplyFrame(); }
        private void ApplyFrame()
        {
            if (!IsHandleCreated) return;
            try { SetFrame(20,1); SetFrame(34,ColorTranslator.ToWin32(Theme.Border)); SetFrame(35,ColorTranslator.ToWin32(Theme.Background)); SetFrame(36,ColorTranslator.ToWin32(Theme.Text)); }
            catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
        }
        private void SetFrame(int attribute,int value) { DwmSetWindowAttribute(Handle,attribute,ref value,4); }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd,int msg,IntPtr wParam,IntPtr lParam);
    }
    internal sealed class ConfirmDialog : DarkForm
    {
        public ConfirmDialog(string title,string message) : base(title,"Conferma l'operazione prima di continuare.",Glyphs.Warning,null)
        {
            Heading.GlyphColor=Theme.Warning;
            ClientSize=new Size(700,440); MinimumSize=new Size(560,360); MaximizeBox=false; MinimizeBox=false;
            StackPanel stack=new StackPanel(); stack.Controls.Add(Theme.Label(message,Theme.Warning)); Content.Controls.Add(stack);
            CloseAction.Text="&Annulla";
            DarkButton confirm=(DarkButton)Theme.Button("&Conferma",delegate { DialogResult=DialogResult.Yes; Close(); }); confirm.Caution=true; confirm.ForeColor=Theme.Warning; Footer.Controls.Add(confirm);
            AcceptButton=CloseAction; Shown+=delegate { CloseAction.Focus(); };
        }
        public static bool Ask(IWin32Window owner,string title,string message)
        { using (ConfirmDialog dialog=new ConfirmDialog(title,message)) return dialog.ShowDialog(owner)==DialogResult.Yes; }
    }
}
