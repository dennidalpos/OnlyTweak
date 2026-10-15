using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace OnlyTweakWindows
{
    internal sealed class PrinterItem
    {
        public string Name;
        public bool IsDefault, IsConnection;
        public override string ToString() { return Name + (IsDefault ? "  [predefinita]" : "") + (IsConnection ? "  [connessione]" : ""); }
    }
    internal sealed class PrinterService
    {
        public List<PrinterItem> Read()
        {
            List<PrinterItem> printers = new List<PrinterItem>();
            uint needed, count;
            // Level 4 uses the local cache for connections, avoiding a server round trip per printer.
            bool first = EnumPrinters(6, null, 4, IntPtr.Zero, 0, out needed, out count);
            int error = Marshal.GetLastWin32Error();
            if (!first && error != 122) throw new Win32Exception(error);
            if (needed == 0) return printers;
            IntPtr buffer = Marshal.AllocHGlobal(checked((int)needed));
            try
            {
                if (!EnumPrinters(6, null, 4, buffer, needed, out needed, out count)) throw new Win32Exception(Marshal.GetLastWin32Error());
                string defaultName = DefaultName();
                int size = Marshal.SizeOf(typeof(PRINTER_INFO_4));
                for (int i=0; i<count; i++)
                {
                    PRINTER_INFO_4 info = (PRINTER_INFO_4)Marshal.PtrToStructure(IntPtr.Add(buffer, checked(i * size)), typeof(PRINTER_INFO_4));
                    string name = Marshal.PtrToStringUni(info.Name);
                    if (!String.IsNullOrEmpty(name)) printers.Add(new PrinterItem { Name=name, IsDefault=String.Equals(name, defaultName, StringComparison.OrdinalIgnoreCase), IsConnection=(info.Attributes & 0x10) != 0 });
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
            printers.Sort(delegate(PrinterItem a, PrinterItem b) { return StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name); });
            return printers;
        }
        private static string DefaultName()
        {
            uint size=0;
            GetDefaultPrinter(null, ref size);
            if (size==0) return null;
            StringBuilder result = new StringBuilder(checked((int)size));
            return GetDefaultPrinter(result, ref size) ? result.ToString() : null;
        }
        public void Purge(string name, Action<string> log)
        {
            IntPtr printer;
            PRINTER_DEFAULTS defaults = new PRINTER_DEFAULTS { DesiredAccess=4 }; // PRINTER_ACCESS_ADMINISTER
            if (!OpenPrinter(name, out printer, ref defaults)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                if (!SetPrinter(printer, 0, IntPtr.Zero, 3)) throw new Win32Exception(Marshal.GetLastWin32Error());
                log("Richiesta di annullamento inviata per tutti i lavori della stampante: " + name);
                log("Un lavoro già trasferito alla stampante o in stampa può proseguire. Controlla la coda e, se necessario, il dispositivo.");
            }
            finally { ClosePrinter(printer); }
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct PRINTER_INFO_4 { public IntPtr Name, Server; public uint Attributes; }
        [StructLayout(LayoutKind.Sequential)]
        private struct PRINTER_DEFAULTS { public IntPtr DataType, DevMode; public uint DesiredAccess; }
        [DllImport("winspool.drv", EntryPoint="EnumPrintersW", CharSet=CharSet.Unicode, SetLastError=true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumPrinters(uint flags, string name, uint level, IntPtr buffer, uint size, out uint needed, out uint returned);
        [DllImport("winspool.drv", EntryPoint="GetDefaultPrinterW", CharSet=CharSet.Unicode, SetLastError=true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetDefaultPrinter(StringBuilder buffer, ref uint size);
        [DllImport("winspool.drv", EntryPoint="OpenPrinterW", CharSet=CharSet.Unicode, SetLastError=true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenPrinter(string name, out IntPtr printer, ref PRINTER_DEFAULTS defaults);
        [DllImport("winspool.drv", EntryPoint="SetPrinterW", SetLastError=true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetPrinter(IntPtr printer, uint level, IntPtr info, uint command);
        [DllImport("winspool.drv", SetLastError=true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClosePrinter(IntPtr printer);
    }
}
