using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace McDm.Core;

/// <summary>Windows AMSI file streams: registered vendors, no vendor-specific executable required.</summary>
public static class WindowsAntivirus
{
    public static bool IsAvailable
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            try { using var providers = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\AMSI\Providers"); return providers?.SubKeyCount > 0; }
            catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
        }
    }

    public static bool IsThreat(uint result) => result >= 32768;
    public static bool IsPolicyBlock(uint result) => result is >= 0x4000 and <= 0x4fff;

    // This entry point runs in an isolated helper process; the parent enforces timeout/cancellation.
    public static int RunWorker(string file)
    {
        if (!OperatingSystem.IsWindows()) return 20;
        int result = 20;
        var worker = new Thread(() => result = RunCore(file)) { IsBackground = true };
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
        worker.Join();
        return result;
    }

    private static int RunCore(string file)
    {
        if (!OperatingSystem.IsWindows() || !IsAvailable) return 20;
        object? engine = null;
        IAntimalwareProvider? provider = null;
        try
        {
            engine = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("fdb00e52-a214-4aa1-8fba-4357bb0072ec"), throwOnError: true)!);
            using var stream = new FileContent(File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete), file);
            int result = ((IAntimalware)engine!).Scan(stream, out uint verdict, out provider);
            if (result < 0) return 20;
            if (IsThreat(verdict)) { Console.WriteLine("Detected: 1"); return 10; }
            if (IsPolicyBlock(verdict)) { Console.WriteLine("FM_AV_POLICY_BLOCK"); return 21; }
            // S_OK is the registered Windows provider's answer, not a GUI launch exit code.
            Console.WriteLine("FM_AV_SCAN_COMPLETED");
            return 0;
        }
        catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException or InvalidCastException) { return 20; }
        finally
        {
            if (provider != null && Marshal.IsComObject(provider)) Marshal.FinalReleaseComObject(provider);
            if (engine != null && Marshal.IsComObject(engine)) Marshal.FinalReleaseComObject(engine);
        }
    }

    [ComVisible(true), Guid("3e47f2e5-81d4-4d3b-897f-545096770373"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAmsiStream
    {
        [PreserveSig] int GetAttribute(uint attribute, uint bufferSize, IntPtr buffer, out uint actualSize);
        [PreserveSig] int Read(ulong position, uint size, IntPtr buffer, out uint readSize);
    }

    [ComImport, Guid("82d29c2e-f062-44e6-b5c9-3d9a2f24a2df"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAntimalware
    {
        [PreserveSig] int Scan([MarshalAs(UnmanagedType.Interface)] IAmsiStream stream, out uint result,
            [MarshalAs(UnmanagedType.Interface)] out IAntimalwareProvider? provider);
        [PreserveSig] void CloseSession(ulong session);
    }

    [ComImport, Guid("b2cabfe3-fe04-42b1-a5df-08d483d4d125"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAntimalwareProvider
    {
        [PreserveSig] int Scan([MarshalAs(UnmanagedType.Interface)] IAmsiStream stream, out uint result);
        [PreserveSig] void CloseSession(ulong session);
        [PreserveSig] int DisplayName(out IntPtr name);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class FileContent(Stream source, string name) : IAmsiStream, IDisposable
    {
        public int GetAttribute(uint attribute, uint bufferSize, IntPtr buffer, out uint actualSize)
        {
            byte[]? value = attribute switch
            {
                0 => Encoding.Unicode.GetBytes("FM Download Manager\0"),
                1 => Encoding.Unicode.GetBytes(name + "\0"),
                2 => BitConverter.GetBytes((ulong)source.Length),
                4 => new byte[IntPtr.Size],
                _ => null
            };
            actualSize = (uint)(value?.Length ?? 0);
            if (value == null) return unchecked((int)0x80004001);
            if (bufferSize < actualSize) return unchecked((int)0x8007007a);
            if (buffer == IntPtr.Zero) return unchecked((int)0x80070057);
            Marshal.Copy(value, 0, buffer, value.Length);
            return 0;
        }

        public int Read(ulong position, uint size, IntPtr buffer, out uint readSize)
        {
            readSize = 0;
            if (position > long.MaxValue || (size > 0 && buffer == IntPtr.Zero)) return unchecked((int)0x80070057);
            if (size == 0 || position >= (ulong)source.Length) return 0;
            byte[] bytes = System.Buffers.ArrayPool<byte>.Shared.Rent((int)Math.Min(size, 1024 * 1024u));
            try
            {
                lock (source)
                {
                    source.Position = (long)position;
                    int count = source.Read(bytes, 0, (int)Math.Min(size, (uint)bytes.Length));
                    Marshal.Copy(bytes, 0, buffer, count);
                    readSize = (uint)count;
                }
                return 0;
            }
            catch (IOException) { return unchecked((int)0x8007001e); }
            finally { System.Buffers.ArrayPool<byte>.Shared.Return(bytes); }
        }
        public void Dispose() => source.Dispose();
    }
}
