using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace McDm.App;

public static class ClipboardHelper
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    public static bool SetText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        // Run in a separate STA thread with a hard timeout to completely prevent UI thread freezes
        bool success = false;
        var thread = new Thread(() =>
        {
            // 1. Try Win32 direct clipboard first (fast, bypasses OLE synchronization locks)
            for (int i = 0; i < 5; i++)
            {
                if (TrySetWin32Text(text))
                {
                    success = true;
                    return;
                }
                Thread.Sleep(20);
            }

            // 2. Fallback to WPF Clipboard without OLE persistence
            try
            {
                Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text), false);
                success = true;
            }
            catch { }
        })
        {
            IsBackground = true,
            Name = "ClipboardHelperThread"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(500); // 500ms max timeout - caller never hangs

        return success;
    }

    private static bool TrySetWin32Text(string text)
    {
        try
        {
            if (!OpenClipboard(IntPtr.Zero)) return false;
            try
            {
                EmptyClipboard();
                int bytes = (text.Length + 1) * 2;
                IntPtr hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
                if (hGlobal == IntPtr.Zero) return false;

                IntPtr target = GlobalLock(hGlobal);
                if (target == IntPtr.Zero)
                {
                    GlobalFree(hGlobal);
                    return false;
                }

                Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
                Marshal.WriteInt16(target + (text.Length * 2), 0);
                GlobalUnlock(hGlobal);

                if (SetClipboardData(CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
                {
                    GlobalFree(hGlobal);
                    return false;
                }
                return true;
            }
            finally
            {
                CloseClipboard();
            }
        }
        catch
        {
            return false;
        }
    }
}
