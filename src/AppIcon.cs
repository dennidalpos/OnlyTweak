using System.Drawing;
using System.IO;
using System.Reflection;

namespace OnlyTweakWindows
{
    internal static class AppIcon
    {
        // Shared for the process lifetime; clone before disposing the resource stream.
        internal static readonly Icon Value=Load();
        private static Icon Load()
        {
            using (Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("OnlyTweak.Icon"))
            using (Icon icon=new Icon(stream,32,32)) return (Icon)icon.Clone();
        }
    }
}
