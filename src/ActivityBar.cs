using System;
using System.Drawing;
using System.Windows.Forms;

namespace OnlyTweakWindows
{
    internal sealed class ActivityBar : Control
    {
        public ActivityBar() { DoubleBuffered=true; BackColor=Theme.Track; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            int length=Math.Max(20,Width/4),position=(int)((uint)Environment.TickCount%2000)*(Width+length)/2000-length;
            using (SolidBrush brush=new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(brush,position,0,length,Height);
        }
    }
}
