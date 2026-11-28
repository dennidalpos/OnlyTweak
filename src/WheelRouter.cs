using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OnlyTweakWindows
{
    internal interface IWheelSurface
    {
        bool ScrollWheel(int delta, bool horizontal);
    }
    // One handler owns each wheel message, irrespective of which descendant has focus.
    internal sealed class WheelRouter : IMessageFilter
    {
        private static bool installed;
        internal static void Install()
        {
            if (installed) return;
            installed=true;
            WheelRouter router=new WheelRouter(); Application.AddMessageFilter(router);
            Application.ApplicationExit+=delegate { Application.RemoveMessageFilter(router); installed=false; };
        }
        public bool PreFilterMessage(ref Message message)
        {
            if (message.Msg!=0x020a && message.Msg!=0x020e) return false;
            long coordinates=message.LParam.ToInt64();
            Point point=new Point((short)(coordinates&0xffff),(short)((coordinates>>16)&0xffff));
            Control target=Control.FromChildHandle(WindowFromPoint(point));
            return RouteMessage(ref message,target,Form.ActiveForm,(Control.ModifierKeys&Keys.Shift)!=0);
        }
        internal static bool RouteMessage(ref Message message,Control target,Form active,bool shift)
        {
            if (message.Msg!=0x020a && message.Msg!=0x020e) return false;
            return Route(target,active,(short)((message.WParam.ToInt64()>>16)&0xffff),
                message.Msg==0x020e || shift,message.Msg==0x020e);
        }
        internal static bool Route(Control target,Form active,int delta,bool horizontal,bool nativeHorizontal)
        {
            if (active==null || !active.Enabled || target==null || target.FindForm()!=active || !target.Enabled) return false;
            for (Control current=target;current!=null;current=current.Parent)
            {
                IWheelSurface surface=current as IWheelSurface;
                if (surface!=null && surface.ScrollWheel(nativeHorizontal ? -delta : delta,horizontal)) return true;
            }
            return false;
        }
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    }
    internal sealed class WheelSteps
    {
        private int vertical,horizontal;
        internal int Pixels(int delta,bool axis,int line,int page,int lines)
        {
            int accumulated=(axis ? horizontal : vertical)+delta;
            int steps=accumulated/120;
            if (axis) horizontal=accumulated%120; else vertical=accumulated%120;
            return steps*(lines<0 ? Math.Max(1,page) : Math.Max(0,lines)*Math.Max(1,line));
        }
        internal int Pixels(int delta,bool axis,int line,int page)
        { return Pixels(delta,axis,line,page,SystemInformation.MouseWheelScrollLines); }
    }
}
