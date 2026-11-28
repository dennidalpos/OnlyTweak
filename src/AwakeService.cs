using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace OnlyTweakWindows
{
    internal interface IAwakeBackend {void Set(bool active,bool display);}
    internal sealed class WindowsAwakeBackend : IAwakeBackend {
        public void Set(bool active,bool display) {
            uint flags=0x80000000u | (active ? 1u : 0u) | (active && display ? 2u : 0u);
            if(SetThreadExecutionState(flags)==0) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        [DllImport("kernel32.dll",SetLastError=true)] private static extern uint SetThreadExecutionState(uint flags);
    }
    internal sealed class AwakeService : IDisposable
    {
        private readonly IAwakeBackend backend;
        private ManualResetEventSlim stop;
        private Thread thread;
        private readonly object sync=new();
        private bool active;
        private string error;
        private DateTimeOffset? expires;
        internal AwakeService(IAwakeBackend backend) {this.backend=backend;}
        internal bool Active {get {lock(sync) return active;}}
        internal DateTimeOffset? Expires {get {lock(sync) return expires;}}
        internal string Error {get {lock(sync) return error;}}
        internal void Start(TimeSpan duration,bool display) {
            if(duration<=TimeSpan.Zero || duration>TimeSpan.FromHours(12)) throw new ArgumentException("Durata consentita: fino a 12 ore.");
            Stop();stop=new ManualResetEventSlim();var signal=stop;
            using var ready=new ManualResetEventSlim();
            thread=new Thread(()=> {
                try {
                    backend.Set(true,display);lock(sync) {active=true;expires=DateTimeOffset.UtcNow+duration;error=null;}ready.Set();
                    signal.Wait(duration);
                } catch(Exception ex) {lock(sync) error=ex.Message;ready.Set();}
                finally {
                    try {backend.Set(false,false);} catch(Exception ex) {lock(sync) error=ex.Message;}
                    lock(sync) {active=false;expires=null;}
                }
            }) {IsBackground=true,Name="OnlyTweak Awake"};
            thread.Start();ready.Wait();
            if(!Active && Error!=null) throw new InvalidOperationException(Error);
        }
        internal void Stop() {
            stop?.Set();thread?.Join();stop?.Dispose();stop=null;thread=null;
        }
        public void Dispose()=>Stop();
    }
}
