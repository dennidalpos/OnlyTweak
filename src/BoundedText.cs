using System;
using System.Text;

namespace OnlyTweakWindows
{
    internal sealed class BoundedText
    {
        internal const int DefaultLimit = 200000;
        private readonly int limit;
        private readonly StringBuilder text = new StringBuilder();
        private readonly object gate = new object();
        public bool Truncated { get; private set; }
        public BoundedText(int limit=DefaultLimit) { this.limit=limit>64 ? limit : throw new ArgumentOutOfRangeException(nameof(limit)); }
        public void Append(string value)
        {
            if (String.IsNullOrEmpty(value)) return;
            lock (gate)
            {
                int overflow=text.Length+value.Length-limit;
                if (overflow>0) { Truncated=true; text.Remove(0,Math.Min(overflow,text.Length)); }
                if (value.Length>limit) text.Append(value,value.Length-limit,limit); else text.Append(value);
            }
        }
        public string Read(bool marker=true)
        {
            lock (gate)
            {
                const string note="[Output precedente troncato]\r\n";
                string result=text.ToString();
                return marker && Truncated ? note+result.Substring(Math.Max(0,result.Length-(limit-note.Length))) : result;
            }
        }
        public string Drain()
        { lock (gate) { string result=Read(); text.Clear(); Truncated=false; return result; } }
    }
}
