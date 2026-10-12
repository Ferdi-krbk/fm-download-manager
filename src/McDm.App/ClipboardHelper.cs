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
        if (text == null) return false;

        // Try standard WPF clipboard up to 5 times
        for (int i = 0; i < 5; i++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch
            {
                Thread.Sleep(50);
            }
        }

        // Fallback: direct Win32 Clipboard API
        try
        {
            for (int i = 0; i < 5; i++)
            {
                if (OpenClipboard(IntPtr.Zero))
                {
                    try
                    {
                        EmptyClipboard();
                        var bytes = (text.Length + 1) * 2;
                        var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
                        if (hGlobal != IntPtr.Zero)
                        {
                            var target = GlobalLock(hGlobal);
                            if (target != IntPtr.Zero)
                            {
                                Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
                                Marshal.WriteInt16(target + (text.Length * 2), 0);
                                GlobalUnlock(hGlobal);
                                SetClipboardData(CF_UNICODETEXT, hGlobal);
                                return true;
                            }
                            GlobalFree(hGlobal);
                        }
                    }
                    finally
                    {
                        CloseClipboard();
                    }
                }
                Thread.Sleep(50);
            }
        }
        catch
        {
            // Ignore
        }

        return false;
    }
}
