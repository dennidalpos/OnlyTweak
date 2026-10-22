using System.Drawing;
using System.IO;
using System.Reflection;

namespace OnlyTweakWindows
{
    internal static class AppIcon
    {
        // Shared for the process lifetime; clone before disposing the resource stream.
        internal static readonly Icon Large=Load(32);
        internal static readonly Icon Small=Load(16);
        internal static readonly Icon Value=Large;
        private static Icon Load(int size)
        {
            using (Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("OnlyTweak.Icon"))
            using (Icon icon=new Icon(stream,size,size)) return (Icon)icon.Clone();
        }
    }
}
