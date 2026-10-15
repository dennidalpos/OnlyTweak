using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OnlyTweakWindows
{
    internal sealed class DarkScrollBar : Control, IWheelSurface
    {
        private int extent,viewport,value,dragStart,dragValue;
        private bool dragging,hovered;
        private readonly WheelSteps wheel=new WheelSteps();
        public bool Horizontal { get; set; }
        public event EventHandler ValueChanged;
        public int Maximum { get { return Math.Max(0,extent-viewport); } }
        public int Value
        {
            get { return value; }
            set { int next=Math.Max(0,Math.Min(Maximum,value)); if (this.value==next) return; this.value=next; Invalidate(); if (ValueChanged!=null) ValueChanged(this,EventArgs.Empty); }
        }
        public DarkScrollBar()
        {
            SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
            BackColor=Theme.Track; TabStop=false; AccessibleRole=AccessibleRole.ScrollBar; AccessibleName="Scorrimento";
        }
        public void Configure(int total,int visible,int position)
        { extent=Math.Max(0,total); viewport=Math.Max(1,visible); value=Math.Max(0,Math.Min(Maximum,position)); Invalidate(); }
        private int Length { get { return Math.Max(1,(Horizontal ? Width : Height)-8); } }
        private int ThumbLength { get { return Math.Min(Length,Math.Max(24,(int)((long)Length*viewport/Math.Max(viewport,extent)))); } }
        private Rectangle ThumbRectangle
        {
            get
            {
                int position=4+(Maximum==0 ? 0 : (int)((long)(Length-ThumbLength)*Value/Maximum));
                return Horizontal ? new Rectangle(position,4,ThumbLength,Math.Max(2,Height-8)) : new Rectangle(4,position,Math.Max(2,Width-8),ThumbLength);
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            if (Maximum==0) return;
            using (GraphicsPath path=Theme.Round(ThumbRectangle,4))
            using (SolidBrush brush=new SolidBrush(dragging||hovered ? Theme.Accent : Theme.Thumb)) e.Graphics.FillPath(brush,path);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (e.Button!=MouseButtons.Left) return;
            int coordinate=Horizontal ? e.X : e.Y;
            if (ThumbRectangle.Contains(e.Location)) { dragging=true; Capture=true; dragStart=coordinate; dragValue=Value; }
            else Value+=(coordinate<(Horizontal ? ThumbRectangle.Left : ThumbRectangle.Top) ? -1 : 1)*viewport;
            Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        { base.OnMouseMove(e); if (dragging) Value=dragValue+(int)((long)((Horizontal ? e.X : e.Y)-dragStart)*Maximum/Math.Max(1,Length-ThumbLength)); }
        protected override void OnMouseUp(MouseEventArgs e) { dragging=false; Capture=false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) dragging=false; base.OnMouseCaptureChanged(e); }
        protected override void OnMouseEnter(EventArgs e) { hovered=true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered=false; Invalidate(); base.OnMouseLeave(e); }
        public bool ScrollWheel(int delta,bool horizontal)
        {
            IWheelSurface parent=Parent as IWheelSurface;
            if (parent!=null) return parent.ScrollWheel(delta,horizontal||Horizontal);
            Value-=wheel.Pixels(delta,horizontal,Font.Height,viewport); return true;
        }
        protected override AccessibleObject CreateAccessibilityInstance() { return new ScrollAccessibility(this); }
        private sealed class ScrollAccessibility : ControlAccessibleObject
        {
            private readonly DarkScrollBar owner;
            public ScrollAccessibility(DarkScrollBar owner) : base(owner) { this.owner=owner; }
            public override string Value { get { return owner.Value.ToString(); } set { int position; if (Int32.TryParse(value,out position)) owner.Value=position; } }
        }
    }
    internal sealed class StackPanel : UserControl, IWheelSurface
    {
        private readonly Panel viewport;
        private readonly FlowLayoutPanel flow;
        private readonly DarkScrollBar bar;
        private bool arranging;
        private readonly WheelSteps wheel=new WheelSteps();
        public new ControlCollection Controls { get { return flow.Controls; } }
        internal int ScrollOffset { get { return bar.Value; } }
        internal int ScrollMaximum { get { return bar.Maximum; } }
        public StackPanel()
        {
            AutoScaleMode=AutoScaleMode.None; Dock=DockStyle.Fill; BackColor=Theme.Background;
            viewport=new Panel { BackColor=Theme.Background,Margin=Padding.Empty };
            flow=new FlowLayoutPanel { FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=false,BackColor=Theme.Background,Margin=Padding.Empty };
            bar=new DarkScrollBar();
            viewport.Controls.Add(flow); base.Controls.Add(viewport); base.Controls.Add(bar);
            Padding=new Padding(26,6,26,14);
            bar.ValueChanged+=delegate { flow.Top=-bar.Value; };
            flow.ControlAdded+=delegate(object sender,ControlEventArgs e) { Wire(e.Control); PerformLayout(); };
            flow.ControlRemoved+=delegate { PerformLayout(); };
            flow.Layout+=delegate { if (!arranging) PerformLayout(); };
        }
        private void Wire(Control control)
        {
            control.Enter+=delegate { EnsureVisible(control); };
            control.ControlAdded+=delegate(object sender,ControlEventArgs e) { Wire(e.Control); };
            foreach (Control child in control.Controls) Wire(child);
        }
        public bool ScrollWheel(int delta,bool horizontal)
        {
            if (horizontal) return true;
            ScrollTo(bar.Value-wheel.Pixels(delta,false,Font.Height,viewport.Height)); return true;
        }
        internal void ScrollTo(int value) { bar.Value=value; }
        internal void EnsureVisible(Control control)
        {
            if (control.IsDisposed||!control.IsHandleCreated) return;
            Point point=flow.PointToClient(control.PointToScreen(Point.Empty));
            if (point.Y<bar.Value) ScrollTo(point.Y);
            else if (point.Y+control.Height>bar.Value+viewport.Height) ScrollTo(point.Y+control.Height-viewport.Height);
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            if (flow==null||arranging) return;
            arranging=true;
            try
            {
                int gutter=Math.Max(14,Font.Height),width=Math.Max(80,ClientSize.Width-gutter-4);
                flow.SuspendLayout(); flow.Padding=Padding; flow.Width=width;
                foreach (Control child in flow.Controls)
                {
                    int target=Math.Max(20,width-Padding.Horizontal-child.Margin.Horizontal);
                    if (child is Label) child.MaximumSize=new Size(target,0); else child.Width=target;
                    DarkButton card=child as DarkButton;
                    if (card!=null && !String.IsNullOrEmpty(card.Description))
                    {
                        using (Font title=new Font(card.Font,FontStyle.Bold))
                        using (Font detail=new Font(card.Font.FontFamily,Math.Max(8F,card.Font.Size-1F)))
                        {
                            int textWidth=Math.Max(1,target-card.Padding.Horizontal-20);
                            int detailHeight=TextRenderer.MeasureText(card.Description,detail,new Size(textWidth,Int32.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix).Height;
                            card.Height=Math.Max(74,card.Padding.Vertical+6+title.Height+4+detailHeight);
                        }
                    }
                }
                flow.ResumeLayout(true);
                int total=Padding.Vertical;
                foreach (Control child in flow.Controls) if (child.Visible) total+=child.Height+child.Margin.Vertical;
                flow.Height=Math.Max(1,total); viewport.SetBounds(0,0,width,ClientSize.Height);
                bar.SetBounds(width,0,gutter,ClientSize.Height); bar.Visible=total>ClientSize.Height;
                bar.Configure(total,ClientSize.Height,bar.Value); flow.Location=new Point(0,-bar.Value);
            }
            finally { arranging=false; }
            base.OnLayout(e);
        }
    }
    internal sealed class DarkOutput : UserControl, IWheelSurface
    {
        private readonly RichTextBox editor;
        private readonly DarkScrollBar vertical,horizontal;
        private bool syncing;
        private int textWidth,textHeight;
        private readonly WheelSteps wheel=new WheelSteps();
        public bool ScrollWheel(int delta,bool axis)
        {
            DarkScrollBar bar=axis ? horizontal : vertical;
            bar.Value-=wheel.Pixels(delta,axis,editor.Font.Height,axis ? editor.Width : editor.Height);
            return true;
        }
        public DarkOutput()
        {
            AutoScaleMode=AutoScaleMode.None; BackColor=Theme.Track; Padding=new Padding(1); DoubleBuffered=true;
            editor=new RichTextBox { ReadOnly=true,WordWrap=false,ScrollBars=RichTextBoxScrollBars.None,BorderStyle=BorderStyle.None,
                BackColor=Theme.Track,ForeColor=Theme.Text,Font=new Font("Consolas",10F),DetectUrls=false,HideSelection=false,
                AccessibleName="Output dell'operazione",TabStop=true };
            vertical=new DarkScrollBar(); horizontal=new DarkScrollBar { Horizontal=true };
            Controls.Add(editor); Controls.Add(vertical); Controls.Add(horizontal);
            vertical.ValueChanged+=ScrollEditor; horizontal.ValueChanged+=ScrollEditor;
            editor.TextChanged+=delegate { MeasureText(); }; editor.VScroll+=delegate { SyncPosition(); }; editor.HScroll+=delegate { SyncPosition(); };
            editor.Resize+=delegate { MeasureText(); }; editor.KeyUp+=delegate { SyncPosition(); }; editor.MouseWheel+=delegate { SyncPosition(); };
            AccessibleName="Risultato e cronologia dell'operazione";
        }
        public override string Text { get { return editor==null ? "" : editor.Text; } set { if (editor!=null) { editor.Text=value; editor.SelectionStart=0; editor.SelectionLength=0; } } }
        public int TextLength { get { return editor.TextLength; } }
        public void Clear() { editor.Clear(); }
        public void AppendText(string text) { editor.AppendText(text); editor.SelectionStart=editor.TextLength; editor.ScrollToCaret(); SyncPosition(); }
        private void MeasureText()
        {
            if (syncing) return;
            int width=0;
            foreach (string line in editor.Lines) width=Math.Max(width,TextRenderer.MeasureText(line,editor.Font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine).Width);
            textWidth=width+16; textHeight=Math.Max(1,editor.Lines.Length)*editor.Font.Height+8;
            PerformLayout(); SyncPosition();
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            if (editor==null) return;
            int gutter=Math.Max(14,Font.Height), inset=10;
            bool v=textHeight>ClientSize.Height-2*inset;
            bool h=textWidth>ClientSize.Width-2*inset-(v ? gutter : 0);
            v=textHeight>ClientSize.Height-2*inset-(h ? gutter : 0);
            int w=Math.Max(1,ClientSize.Width-2*inset-(v ? gutter : 0)),height=Math.Max(1,ClientSize.Height-2*inset-(h ? gutter : 0));
            editor.SetBounds(inset,inset,w,height); vertical.SetBounds(ClientSize.Width-gutter-1,1,gutter,ClientSize.Height-(h ? gutter : 0)-2);
            horizontal.SetBounds(1,ClientSize.Height-gutter-1,ClientSize.Width-(v ? gutter : 0)-2,gutter);
            vertical.Visible=v; horizontal.Visible=h;
            vertical.Configure(textHeight,height,vertical.Value); horizontal.Configure(textWidth,w,horizontal.Value);
            base.OnLayout(e);
        }
        private void SyncPosition()
        {
            if (syncing||!editor.IsHandleCreated) return;
            syncing=true;
            try { Point point=new Point(); SendMessage(editor.Handle,0x04dd,IntPtr.Zero,ref point); vertical.Configure(textHeight,editor.ClientSize.Height,point.Y); horizontal.Configure(textWidth,editor.ClientSize.Width,point.X); }
            finally { syncing=false; }
        }
        private void ScrollEditor(object sender,EventArgs e)
        {
            if (syncing||!editor.IsHandleCreated) return;
            Point point=new Point(horizontal.Value,vertical.Value); SendMessage(editor.Handle,0x04de,IntPtr.Zero,ref point); SyncPosition();
        }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using (Pen pen=new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1); }
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,ref Point point);
    }
    internal sealed class PrinterItems : List<object>
    {
        private readonly Action changed;
        public PrinterItems(Action changed) { this.changed=changed; }
        public new int Add(object item) { base.Add(item); changed(); return Count-1; }
        public new void Clear() { base.Clear(); changed(); }
    }
    internal sealed class DarkPrinterList : Control, IWheelSurface
    {
        public readonly PrinterItems Items;
        private readonly DarkScrollBar bar;
        private int selected=-1;
        private readonly WheelSteps wheel=new WheelSteps();
        public event EventHandler SelectedIndexChanged;
        private int RowHeight { get { return Math.Max(46,Font.Height*3); } }
        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                int next=value>=0&&value<Items.Count ? value : -1;
                if (next==selected) return; selected=next; EnsureSelection(); Invalidate();
                AccessibilityNotifyClients(AccessibleEvents.Selection,selected);
                if (SelectedIndexChanged!=null) SelectedIndexChanged(this,EventArgs.Empty);
            }
        }
        public object SelectedItem { get { return selected>=0&&selected<Items.Count ? Items[selected] : null; } }
        public DarkPrinterList()
        {
            SetStyle(ControlStyles.Selectable|ControlStyles.StandardClick|ControlStyles.StandardDoubleClick|ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw,true);
            BackColor=Theme.Surface; ForeColor=Theme.Text; TabStop=true; AccessibleRole=AccessibleRole.List; AccessibleName="Stampanti di sistema";
            Items=new PrinterItems(delegate { if (selected>=Items.Count) SelectedIndex=-1; PerformLayout(); Invalidate(); });
            bar=new DarkScrollBar(); bar.ValueChanged+=delegate { Invalidate(); }; Controls.Add(bar);
        }
        private void EnsureSelection()
        {
            if (selected<0) return;
            int y=selected*RowHeight;
            if (y<bar.Value) bar.Value=y; else if (y+RowHeight>bar.Value+ClientSize.Height-4) bar.Value=y+RowHeight-ClientSize.Height+4;
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            if (bar==null) return;
            int gutter=Math.Max(14,Font.Height); bar.SetBounds(Width-gutter-1,1,gutter,Math.Max(1,Height-2));
            bar.Configure(Items.Count*RowHeight,Math.Max(1,Height-4),bar.Value); bar.Visible=bar.Maximum>0; base.OnLayout(e);
        }
        protected override bool IsInputKey(Keys keyData)
        { Keys key=keyData&Keys.KeyCode; return key==Keys.Up||key==Keys.Down||key==Keys.Home||key==Keys.End||key==Keys.PageUp||key==Keys.PageDown||base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int page=Math.Max(1,Height/RowHeight);
            if (e.KeyCode==Keys.Down) SelectedIndex=Math.Min(Items.Count-1,selected+1);
            else if (e.KeyCode==Keys.Up) SelectedIndex=Math.Max(0,selected-1);
            else if (e.KeyCode==Keys.Home) SelectedIndex=0;
            else if (e.KeyCode==Keys.End) SelectedIndex=Items.Count-1;
            else if (e.KeyCode==Keys.PageDown) SelectedIndex=Math.Min(Items.Count-1,selected+page);
            else if (e.KeyCode==Keys.PageUp) SelectedIndex=Math.Max(0,selected-page);
            else { base.OnKeyDown(e); return; }
            e.Handled=true;
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); if (e.Button==MouseButtons.Left) SelectedIndex=(e.Y-2+bar.Value)/RowHeight; }
        public bool ScrollWheel(int delta,bool horizontal)
        { if (!horizontal) bar.Value-=wheel.Pixels(delta,false,RowHeight,ClientSize.Height); return true; }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            int width=Width-(bar.Visible ? bar.Width : 0)-4;
            for (int i=Math.Max(0,bar.Value/RowHeight);i<Items.Count;i++)
            {
                int y=2+i*RowHeight-bar.Value; if (y>=Height) break;
                if (i==selected)
                {
                    using (SolidBrush brush=new SolidBrush(Theme.Selection)) e.Graphics.FillRectangle(brush,2,y,width,RowHeight-2);
                    using (SolidBrush brush=new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(brush,2,y+8,3,RowHeight-18);
                }
                PrinterItem printer=Items[i] as PrinterItem; string name=printer==null ? Convert.ToString(Items[i]) : printer.Name;
                string detail=printer==null ? "" : (printer.IsDefault ? "Predefinita  ·  " : "")+(printer.IsConnection ? "Connessione di rete" : "Stampante locale");
                TextRenderer.DrawText(e.Graphics,name,Font,new Rectangle(14,y+7,Math.Max(1,width-22),Font.Height+3),Theme.Text,TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
                using (Font small=new Font(Font.FontFamily,Math.Max(8F,Font.Size-1F))) TextRenderer.DrawText(e.Graphics,detail,small,new Rectangle(14,y+Font.Height+11,Math.Max(1,width-22),small.Height+2),i==selected ? Theme.Accent : Theme.Muted,TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
            }
            if (Items.Count==0) TextRenderer.DrawText(e.Graphics,"Nessuna stampante nell'elenco",Font,new Rectangle(12,12,Width-24,Font.Height*2),Theme.Muted,TextFormatFlags.NoPrefix);
            using (Pen border=new Pen(Focused ? Theme.Accent : Theme.Border)) e.Graphics.DrawRectangle(border,0,0,Width-1,Height-1);
        }
        protected override AccessibleObject CreateAccessibilityInstance() { return new ListAccessibility(this); }
        private sealed class ListAccessibility : ControlAccessibleObject
        {
            private readonly DarkPrinterList owner;
            public ListAccessibility(DarkPrinterList owner) : base(owner) { this.owner=owner; }
            public override int GetChildCount() { return owner.Items.Count; }
            public override AccessibleObject GetChild(int index) { return index>=0&&index<owner.Items.Count ? new ItemAccessibility(owner,index) : null; }
            public override AccessibleObject GetSelected() { return GetChild(owner.SelectedIndex); }
            public override AccessibleObject GetFocused() { return owner.Focused ? GetSelected() : null; }
        }
        private sealed class ItemAccessibility : AccessibleObject
        {
            private readonly DarkPrinterList owner;
            private readonly int index;
            public ItemAccessibility(DarkPrinterList owner,int index) { this.owner=owner; this.index=index; }
            public override string Name { get { return index<owner.Items.Count ? Convert.ToString(owner.Items[index]) : ""; } set { } }
            public override AccessibleRole Role { get { return AccessibleRole.ListItem; } }
            public override AccessibleObject Parent { get { return owner.AccessibilityObject; } }
            public override string DefaultAction { get { return "Seleziona"; } }
            public override Rectangle Bounds
            {
                get
                {
                    Rectangle row=new Rectangle(2,2+index*owner.RowHeight-owner.bar.Value,Math.Max(0,owner.Width-4),owner.RowHeight);
                    row.Intersect(owner.ClientRectangle); return owner.RectangleToScreen(row);
                }
            }
            public override AccessibleStates State
            {
                get
                {
                    AccessibleStates state=AccessibleStates.Selectable|AccessibleStates.Focusable;
                    if (owner.SelectedIndex==index) state|=AccessibleStates.Selected;
                    if (owner.SelectedIndex==index&&owner.Focused) state|=AccessibleStates.Focused;
                    if (Bounds.Height==0) state|=AccessibleStates.Offscreen;
                    return state;
                }
            }
            public override void DoDefaultAction() { owner.SelectedIndex=index; owner.Focus(); }
            public override void Select(AccessibleSelection flags) { owner.SelectedIndex=index; if ((flags&AccessibleSelection.TakeFocus)!=0) owner.Focus(); }
        }
    }
}
