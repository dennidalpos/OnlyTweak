using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace OnlyTweakWindows
{
    // Synthetic geometry/font scaling only: no claim of physical per-monitor DPI qualification.
    internal sealed class RenderScale : IDisposable
    {
        private readonly float scale;
        private readonly HashSet<Control> visited=new();
        private readonly List<Font> fonts=new();
        private readonly List<(Control Control,Font Font)> prepared=new();
        internal RenderScale(float scale) {this.scale=scale;}
        internal void Prepare(Control root)
        {
            if(scale==1F) return;
            void Collect(Control control) {
                if(visited.Add(control)) prepared.Add((control,new Font(control.Font.FontFamily,control.Font.SizeInPoints*scale,control.Font.Style)));
                foreach(Control child in control.Controls) Collect(child);
            }
            Collect(root);
        }
        internal void Apply(Control root)
        {
            if(scale==1F) return;
            var pending=new List<(Control Control,Font Font)>(prepared);prepared.Clear();
            void Collect(Control control) {
                if(visited.Add(control) && TypeDescriptor.GetProperties(control)["Font"].ShouldSerializeValue(control))
                    pending.Add((control,new Font(control.Font.FontFamily,control.Font.SizeInPoints*scale,control.Font.Style)));
                foreach(Control child in control.Controls) Collect(child);
            }
            Collect(root);
            root.SuspendLayout();
            try {foreach(var item in pending) {fonts.Add(item.Font);item.Control.Font=item.Font;}}
            finally {root.ResumeLayout(true);}
        }
        public void Dispose() {foreach(var font in fonts) font.Dispose();foreach(var item in prepared) item.Font.Dispose();}
    }
}
