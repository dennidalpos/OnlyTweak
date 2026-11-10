using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OnlyTweakWindows
{
    internal sealed class RegistryValueState
    {
        public bool KeyExists, ValueExists;
        public object Data;
        public RegistryValueKind Kind;
    }
    internal interface IRegistryReader
    {
        RegistryValueState Read(string path, string name);
    }
}
