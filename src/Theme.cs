using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OnlyTweakWindows
{
    internal static class Theme
    {
        public static readonly Color Background=Color.FromArgb(16,22,31), Surface=Color.FromArgb(25,34,47),
            Raised=Color.FromArgb(33,46,61), Text=Color.FromArgb(235,241,247), Muted=Color.FromArgb(158,175,195),
            Accent=Color.FromArgb(104,226,195), Border=Color.FromArgb(46,62,80), Warning=Color.FromArgb(255,198,119),
            Track=Color.FromArgb(18,27,38), Thumb=Color.FromArgb(71,93,116), Selection=Color.FromArgb(28,65,67);
        public static Button Button(string text,EventHandler clicked)
        {
            Button button=new DarkButton { Text=text,BackColor=Surface,ForeColor=Text,Font=new Font("Segoe UI",10F),
                Size=new Size(150,40),MinimumSize=new Size(120,40),AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,
                Margin=new Padding(5),Cursor=Cursors.Hand,UseVisualStyleBackColor=false,FlatStyle=FlatStyle.Flat };
            if (clicked!=null) button.Click+=clicked;
            return button;
        }
        public static Button Card(string title,string description,EventHandler clicked)
        {
            DarkButton button=(DarkButton)Button(title,clicked);
            button.Description=description; button.AccessibleDescription=description;
            button.AutoSize=false; button.Height=74; button.TextAlign=ContentAlignment.MiddleLeft;
            button.Padding=new Padding(16,8,16,8); button.Margin=new Padding(0,0,0,10); button.UseMnemonic=false;
            return button;
        }
        public static Label Label(string text,Color color)
        { return new Label { Text=text,AutoSize=true,ForeColor=color,BackColor=Color.Transparent,Margin=new Padding(0,0,0,10),UseMnemonic=false }; }
        public static Label Section(string text)
        {
            Label label=Label(text.ToUpperInvariant(),Muted);
            label.Font=new Font("Segoe UI",9F,FontStyle.Bold); label.Margin=new Padding(0,14,0,10); return label;
        }
        public static DarkOutput Output() { return new DarkOutput { Dock=DockStyle.Fill }; }
        public static GraphicsPath Round(Rectangle rectangle,int radius)
        {
            GraphicsPath path=new GraphicsPath(); int size=Math.Max(2,Math.Min(radius*2,Math.Min(rectangle.Width,rectangle.Height)));
            path.AddArc(rectangle.Left,rectangle.Top,size,size,180,90); path.AddArc(rectangle.Right-size,rectangle.Top,size,size,270,90);
            path.AddArc(rectangle.Right-size,rectangle.Bottom-size,size,size,0,90); path.AddArc(rectangle.Left,rectangle.Bottom-size,size,size,90,90); path.CloseFigure(); return path;
        }
    }
    internal sealed class DarkButton : Button
    {
        public string Description;
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
            Color background=BackColor;
            if (Enabled&&(hovered||pressed)) background=BackColor==Theme.Accent ? Color.FromArgb(132,237,210) : Theme.Raised;
            using (GraphicsPath shape=Theme.Round(new Rectangle(1,1,Width-3,Height-3),8))
            using (SolidBrush fill=new SolidBrush(background))
            using (Pen border=new Pen(Enabled&&(Focused||hovered) ? Theme.Accent : Theme.Border))
            { e.Graphics.FillPath(fill,shape); e.Graphics.DrawPath(border,shape); }
            Rectangle text=new Rectangle(Padding.Left+10,Padding.Top+3,Math.Max(1,Width-Padding.Horizontal-20),Math.Max(1,Height-Padding.Vertical-6));
            Color color=Enabled ? ForeColor : Theme.Muted;
            TextFormatFlags flags=TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter;
            flags|=TextAlign==ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
            if (!UseMnemonic) flags|=TextFormatFlags.NoPrefix; else if (!ShowKeyboardCues) flags|=TextFormatFlags.HidePrefix;
            if (String.IsNullOrEmpty(Description)) TextRenderer.DrawText(e.Graphics,Text,Font,text,color,flags);
            else
            {
                using (Font titleFont=new Font(Font,FontStyle.Bold))
                using (Font descriptionFont=new Font(Font.FontFamily,Math.Max(8F,Font.Size-1F)))
                {
                    int titleHeight=titleFont.Height+4;
                    TextRenderer.DrawText(e.Graphics,Text,titleFont,new Rectangle(text.X,text.Y,text.Width,titleHeight),color,flags);
                    TextRenderer.DrawText(e.Graphics,Description,descriptionFont,new Rectangle(text.X,text.Y+titleHeight,text.Width,Math.Max(1,text.Height-titleHeight)),Theme.Muted,
                        TextFormatFlags.Left|TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
                }
            }
            if (Focused&&ShowFocusCues&&Enabled) ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(6,6,Width-13,Height-13),Theme.Accent,background);
        }
    }
    internal class DarkForm : Form
    {
        protected readonly TableLayoutPanel Root;
        protected readonly FlowLayoutPanel Footer;
        protected readonly Button CloseAction;
        protected readonly Panel Content;
        protected readonly Label Subtitle,Feedback;
        public DarkForm(string title,string subtitle)
        {
            WheelRouter.Install();
            Text=title=="OnlyTweakWindows" ? title : title+" · OnlyTweakWindows";
            BackColor=Theme.Background; ForeColor=Theme.Text; Font=new Font("Segoe UI",10F);
            AutoScaleDimensions=new SizeF(96F,96F); AutoScaleMode=AutoScaleMode.Dpi;
            ClientSize=new Size(920,740); MinimumSize=new Size(660,540); StartPosition=FormStartPosition.CenterParent; Icon=SystemIcons.Application;
            Root=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Margin=Padding.Empty,Padding=Padding.Empty };
            Root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            Root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); Root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            TableLayoutPanel header=new TableLayoutPanel { Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Padding=new Padding(26,22,26,8),Margin=Padding.Empty };
            Label eyebrow=Theme.Label("ONLYTWEAK  /  WINDOWS 11",Theme.Accent); eyebrow.Font=new Font("Segoe UI",8F,FontStyle.Bold);
            Label heading=Theme.Label(title,Theme.Text); heading.Font=new Font("Segoe UI",22F,FontStyle.Bold); heading.Margin=new Padding(0,0,0,8);
            Subtitle=Theme.Label(subtitle,Theme.Muted);
            header.Controls.Add(eyebrow,0,0); header.Controls.Add(heading,0,1); header.Controls.Add(Subtitle,0,2);
            header.SizeChanged+=delegate { int width=Math.Max(100,header.ClientSize.Width-header.Padding.Horizontal); heading.MaximumSize=new Size(width,0); Subtitle.MaximumSize=new Size(width,0); };
            Content=new Panel { Dock=DockStyle.Fill,Margin=Padding.Empty };
            Footer=new FlowLayoutPanel { Dock=DockStyle.Fill,AutoSize=true,FlowDirection=FlowDirection.RightToLeft,WrapContents=true,Padding=new Padding(20,10,20,12),Margin=Padding.Empty };
            CloseAction=Theme.Button("&Chiudi",delegate { Close(); }); Footer.Controls.Add(CloseAction); CancelButton=CloseAction;
            Feedback=Theme.Label("",Theme.Muted); Feedback.Dock=DockStyle.Top; Feedback.Padding=new Padding(26,9,26,0); Feedback.Visible=false;
            Footer.Paint+=delegate(object sender,PaintEventArgs e) { using (Pen pen=new Pen(Theme.Border)) e.Graphics.DrawLine(pen,24,0,Footer.Width-24,0); };
            Root.Controls.Add(header,0,0); Root.Controls.Add(Content,0,1); Root.Controls.Add(Feedback,0,2); Root.Controls.Add(Footer,0,3); Controls.Add(Root);
        }
        protected void SetFeedback(string text)
        { if (IsDisposed) return; Feedback.Text=text; Feedback.Visible=!String.IsNullOrEmpty(text); Feedback.MaximumSize=new Size(Math.Max(100,ClientSize.Width-16),0); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyFrame(); }
        protected override void OnActivated(EventArgs e) { base.OnActivated(e); ApplyFrame(); }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); ApplyFrame(); }
        private void ApplyFrame()
        {
            if (!IsHandleCreated) return;
            try { SetFrame(20,1); SetFrame(34,ColorTranslator.ToWin32(Theme.Border)); SetFrame(35,ColorTranslator.ToWin32(Theme.Background)); SetFrame(36,ColorTranslator.ToWin32(Theme.Text)); }
            catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
        }
        private void SetFrame(int attribute,int value) { DwmSetWindowAttribute(Handle,attribute,ref value,4); }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    }
    internal sealed class ConfirmDialog : DarkForm
    {
        public ConfirmDialog(string title,string message) : base(title,"Conferma l'operazione prima di continuare.")
        {
            ClientSize=new Size(700,440); MinimumSize=new Size(560,360); MaximizeBox=false; MinimizeBox=false;
            StackPanel stack=new StackPanel(); stack.Controls.Add(Theme.Label(message,Theme.Warning)); Content.Controls.Add(stack);
            CloseAction.Text="&Annulla";
            Button confirm=Theme.Button("&Conferma",delegate { DialogResult=DialogResult.Yes; Close(); }); confirm.BackColor=Theme.Accent; confirm.ForeColor=Theme.Background; Footer.Controls.Add(confirm);
            AcceptButton=CloseAction; Shown+=delegate { CloseAction.Focus(); };
        }
        public static bool Ask(IWin32Window owner,string title,string message)
        { using (ConfirmDialog dialog=new ConfirmDialog(title,message)) return dialog.ShowDialog(owner)==DialogResult.Yes; }
    }
}
