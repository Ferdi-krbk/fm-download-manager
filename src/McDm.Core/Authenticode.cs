using System.Runtime.InteropServices;

namespace McDm.Core;

internal static class Authenticode
{
    // Provider DLLs must pass Windows trust verification before direct COM activation.
    // No UI, downloads, registry changes or changes to AMSI's signing policy.
    internal static bool IsTrusted(string path)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(path)) return false;
        var file = new FileInfo { Size = (uint)Marshal.SizeOf<FileInfo>(), Path = path };
        IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
        bool initialized = false;
        try
        {
            Marshal.StructureToPtr(file, pointer, false);
            initialized = true;
            var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, UnionChoice = 1, File = pointer, Flags = 0x1000 };
            var action = new Guid("00aac56b-cd44-11d0-8cc2-00c04fc295ee");
            return WinVerifyTrust(new IntPtr(-1), ref action, ref data) == 0;
        }
        finally { if (initialized) Marshal.DestroyStructure<FileInfo>(pointer); Marshal.FreeHGlobal(pointer); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileInfo
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string Path;
        public IntPtr Handle, KnownSubject;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size;
        public IntPtr Policy, Sip;
        public uint UiChoice, Revocation, UnionChoice;
        public IntPtr File;
        public uint StateAction;
        public IntPtr State, Url;
        public uint Flags, UiContext;
        public IntPtr SignatureSettings;
    }
    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
}
