using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace McDm.App;

public static class WindowPlacement
{
    public static Rect FitBounds(Rect workArea, Rect desired, bool center)
    {
        const double margin = 12;
        var usable = new Rect(workArea.Left + margin, workArea.Top + margin,
            Math.Max(1, workArea.Width - margin * 2), Math.Max(1, workArea.Height - margin * 2));
        double width = Math.Min(desired.Width, usable.Width);
        double height = Math.Min(desired.Height, usable.Height);
        double left = center ? usable.Left + (usable.Width - width) / 2 : Math.Clamp(desired.Left, usable.Left, usable.Right - width);
        double top = center ? usable.Top + (usable.Height - height) / 2 : Math.Clamp(desired.Top, usable.Top, usable.Bottom - height);
        return new Rect(left, top, width, height);
    }

    public static void Constrain(Window window, bool center = false)
    {
        if (window.WindowState != WindowState.Normal) return;
        var area = WorkAreaFor(window);
        var desired = new Rect(double.IsNaN(window.Left) ? area.Left : window.Left,
            double.IsNaN(window.Top) ? area.Top : window.Top, window.Width, window.Height);
        var bounds = FitBounds(area, desired, center);
        window.MinWidth = Math.Min(800, bounds.Width);
        window.MinHeight = Math.Min(480, bounds.Height);
        window.Width = bounds.Width;
        window.Height = bounds.Height;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = bounds.Left;
        window.Top = bounds.Top;
    }

    public static Rect WorkAreaFor(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var screen = System.Windows.Forms.Screen.FromHandle(handle);
        var pixels = screen.WorkingArea;
        var source = HwndSource.FromHwnd(handle);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return Rect.Transform(new Rect(pixels.Left, pixels.Top, pixels.Width, pixels.Height), transform);
    }
}
