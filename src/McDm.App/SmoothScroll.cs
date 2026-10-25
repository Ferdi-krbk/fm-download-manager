using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace McDm.App;

/// <summary>Pixel scrolling with a short eased transition; recycling stays enabled.</summary>
public static class SmoothScroll
{
    private sealed class ScrollState { public double Target; public bool Active; public int Generation; }
    private static readonly ConditionalWeakTable<ScrollViewer, ScrollState> States = new();
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false, Changed));
    private static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached(
        "Offset", typeof(double), typeof(SmoothScroll), new PropertyMetadata(0d, (o, e) =>
        { if (o is ScrollViewer viewer) viewer.ScrollToVerticalOffset((double)e.NewValue); }));
    public static void SetIsEnabled(DependencyObject o, bool value) => o.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject o) => (bool)o.GetValue(IsEnabledProperty);

    private static void Changed(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not FrameworkElement element) return;
        if ((bool)e.NewValue)
        {
            element.AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler(Wheel), true);
            element.PreviewMouseDown += PointerDown;
            element.PreviewKeyDown += KeyDown;
            element.Unloaded += Unloaded;
        }
        else
        {
            element.RemoveHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler(Wheel));
            element.PreviewMouseDown -= PointerDown;
            element.PreviewKeyDown -= KeyDown;
            element.Unloaded -= Unloaded;
            if (FindViewer(element) is { } viewer) Cancel(viewer);
        }
    }

    public static ScrollViewer? FindViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer) return viewer;
        for (int n = 0; n < VisualTreeHelper.GetChildrenCount(root); n++)
            if (FindViewer(VisualTreeHelper.GetChild(root, n)) is { } child) return child;
        return null;
    }

    private static void Wheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || SystemParameters.WheelScrollLines == 0) return;
        if (FindViewer((DependencyObject)sender) is not { } viewer || viewer.ScrollableHeight <= 0) return;
        var state = States.GetValue(viewer, static _ => new ScrollState());
        double distance = SystemParameters.WheelScrollLines < 0 ? viewer.ViewportHeight : SystemParameters.WheelScrollLines * 24d;
        double target = Math.Clamp((state.Active ? state.Target : viewer.VerticalOffset) - e.Delta / 120d * distance, 0, viewer.ScrollableHeight);
        if (!state.Active && Math.Abs(target - viewer.VerticalOffset) < .1) return;
        ScrollTo(viewer, target);
        e.Handled = true;
    }

    public static void ScrollTo(ScrollViewer viewer, double target, bool animate = true)
    {
        double start = viewer.VerticalOffset;
        Cancel(viewer);
        var state = States.GetValue(viewer, static _ => new ScrollState());
        state.Target = Math.Clamp(target, 0, viewer.ScrollableHeight);
        if (!animate || !SystemParameters.ClientAreaAnimation)
        {
            viewer.ScrollToVerticalOffset(state.Target);
            return;
        }
        state.Active = true;
        int generation = state.Generation;
        double finalTarget = state.Target;
        viewer.SetValue(OffsetProperty, start);
        var animation = new DoubleAnimation(start, state.Target, TimeSpan.FromMilliseconds(180))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        animation.Completed += (_, _) =>
        {
            if (state.Generation != generation) return;
            viewer.SetValue(OffsetProperty, finalTarget);
            viewer.BeginAnimation(OffsetProperty, null);
            viewer.ScrollToVerticalOffset(finalTarget);
            state.Active = false;
        };
        viewer.BeginAnimation(OffsetProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void Cancel(ScrollViewer viewer)
    {
        double current = viewer.VerticalOffset;
        viewer.SetValue(OffsetProperty, current);
        viewer.BeginAnimation(OffsetProperty, null);
        viewer.ScrollToVerticalOffset(current);
        var state = States.GetValue(viewer, static _ => new ScrollState());
        state.Generation++;
        state.Target = current;
        state.Active = false;
    }
    private static void PointerDown(object sender, MouseButtonEventArgs e) { if (FindViewer((DependencyObject)sender) is { } v) Cancel(v); }
    private static void KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.PageDown or Key.PageUp or Key.Home or Key.End or Key.Up or Key.Down)
            if (FindViewer((DependencyObject)sender) is { } viewer) Cancel(viewer);
    }
    private static void Unloaded(object sender, RoutedEventArgs e) { if (FindViewer((DependencyObject)sender) is { } v) Cancel(v); }
}
