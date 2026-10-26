using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OnlyTweakWindows
{
    internal sealed class MainForm : Form
    {
        internal NavigationService Navigation { get; }
        private bool closing,approved;
        internal MainForm(AppServices app,DarkPage initial=null)
        {
            Text="OnlyTweakWindows"; Icon=AppIcon.Value; BackColor=Theme.Background;
            Font=new Font("Segoe UI",10F); AutoScaleDimensions=new SizeF(96,96); AutoScaleMode=AutoScaleMode.Dpi;
            ClientSize=new Size(960,760); MinimumSize=new Size(660,540); StartPosition=FormStartPosition.CenterScreen;
            Navigation=new NavigationService(this,app,initial??new HomePage(app));
            Shown+=delegate { Navigation.Current.ActivatePage(); };
            Move+=delegate { if(Navigation.Depth>0) Navigation.Current.HideTips(); };
            ResizeBegin+=delegate { if(Navigation.Depth>0) Navigation.Current.HideTips(); };
            Deactivate+=delegate { if(Navigation.Depth>0) Navigation.Current.HideTips(); };
            FormClosing+=async delegate(object sender,FormClosingEventArgs e) {
                if(approved) return;
                e.Cancel=true;
                if(closing) return;
                closing=true;
                try { if(await Navigation.CanCloseAsync()) {approved=true;Close();} }
                finally {closing=false;}
            };
        }
        protected override void OnHandleCreated(EventArgs e) {base.OnHandleCreated(e);ApplyFrame();}
        protected override void OnActivated(EventArgs e) {base.OnActivated(e);ApplyFrame();}
        private void ApplyFrame() {
            try {int dark=1,border=ColorTranslator.ToWin32(Theme.Border),background=ColorTranslator.ToWin32(Theme.Background),text=ColorTranslator.ToWin32(Theme.Text);
                DwmSetWindowAttribute(Handle,20,ref dark,4);DwmSetWindowAttribute(Handle,34,ref border,4);DwmSetWindowAttribute(Handle,35,ref background,4);DwmSetWindowAttribute(Handle,36,ref text,4);
            } catch(DllNotFoundException) {} catch(EntryPointNotFoundException) {}
        }
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
        protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
        {
            if(keyData==(Keys.Alt|Keys.Left)) {_=Navigation.BackAsync();return true;}
            return base.ProcessCmdKey(ref msg,keyData);
        }
        protected override void Dispose(bool disposing) {if(disposing) Navigation?.Dispose();base.Dispose(disposing);}
    }

    internal sealed class NavigationService : IDisposable
    {
        private readonly MainForm host;
        private readonly AppServices app;
        private readonly List<DarkPage> stack=new List<DarkPage>();
        private readonly HashSet<DarkPage> retained=new HashSet<DarkPage>();
        private readonly Dictionary<DarkPage,Control> focused=new();
        private readonly HashSet<DarkPage> leaving=new HashSet<DarkPage>();
        private readonly Dictionary<DarkPage,TaskCompletionSource<DialogResult>> presentations=new();
        private EnterpriseDialog enterprise;
        internal DarkPage Current => stack[^1];
        internal int Depth => stack.Count;
        internal MainForm Window => host;
        internal NavigationService(MainForm host,AppServices app,DarkPage home)
        {this.host=host;this.app=app;Add(home);}
        internal static NavigationService For(Control owner) =>
            (owner as DarkPage)?.Navigation ?? (owner?.FindForm() as MainForm)?.Navigation ??
            throw new InvalidOperationException("Navigazione applicativa non disponibile.");
        internal void Push(DarkPage page)
        {
            if(Current.BlocksPush) {page.Dispose();return;}
            Add(page);
        }
        private void Add(DarkPage page)
        {
            if(stack.Count>0) {focused[Current]=Current.ActiveControl;Current.HideTips();host.Controls.Remove(Current);}
            stack.Add(page);page.Navigation=this;retained.Add(page);Display();
        }
        private void Display()
        {
            DarkPage page=Current;page.Dock=DockStyle.Fill;page.BackButton.Visible=stack.Count>1;
            host.Controls.Add(page);page.BringToFront();
            host.Text=page.PageTitle=="OnlyTweakWindows" ? page.PageTitle : page.PageTitle+" · OnlyTweakWindows";
            host.AcceptButton=page.AcceptButton;host.CancelButton=page.CancelButton;
            page.ResumePage();
            if(host.Visible) page.ActivatePage();
            if(page is ConfirmDialog) page.BackButton.Focus();
            else if(focused.TryGetValue(page,out var focus) && focus!=null && !focus.IsDisposed) focus.Focus();
            else page.SelectNextControl(null,true,true,true,false);
        }
        internal Task<DialogResult> PresentAsync(DarkPage page)
        {
            var completion=new TaskCompletionSource<DialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            presentations.Add(page,completion);Add(page);return completion.Task;
        }
        internal void OpenEnterprise(bool technician)
        {
            enterprise??=new EnterpriseDialog {Persistent=true};
            enterprise.ShowIndex(technician);Push(enterprise);
            if(technician) enterprise.OpenSection(enterprise.Sections.ElementAt(1));
        }
        internal async Task BackAsync()
        {
            DarkPage page=Current;
            if(stack.Count<2 || !leaving.Add(page)) return;
            try {
                if(!await page.CanLeaveAsync(false) || Current!=page) return;
                page.HideTips();host.Controls.Remove(page);stack.RemoveAt(stack.Count-1);page.OnRemoved();focused.Remove(page);
                bool presented=presentations.Remove(page,out var completion);
                if(presented) {retained.Remove(page);completion.TrySetResult(page.DialogResult);}
                Display();
                if(!page.Persistent && !presented) {retained.Remove(page);page.Dispose();}
            } finally {leaving.Remove(page);}
        }
        internal async Task<bool> CanCloseAsync()
        {
            // Includes retained customer workspaces; no worker is lost behind another page.
            foreach(DarkPage page in stack.AsEnumerable().Reverse().Concat(retained).Distinct().ToArray())
                if(!await page.CanLeaveAsync(true)) return false;
            return true;
        }
        public void Dispose()
        {
            foreach(var completion in presentations.Values) completion.TrySetResult(DialogResult.Cancel);
            presentations.Clear();
            foreach(DarkPage page in stack.AsEnumerable().Reverse().Concat(retained).Distinct().ToArray()) page.Dispose();
            retained.Clear();stack.Clear();
        }
    }
}
