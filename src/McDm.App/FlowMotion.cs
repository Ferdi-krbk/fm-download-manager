using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace McDm.App;

public static class FlowMotion
{
    public static void Reveal(FrameworkElement element, bool translate = false, int delay = 0)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        var fade = new DoubleAnimation(.15, 1, TimeSpan.FromMilliseconds(300))
        { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        if (!translate) return;
        var transform = new TranslateTransform();
        element.RenderTransform = transform;
        var slide = new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(340))
        { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
        slide.Completed += (_, _) => { if (ReferenceEquals(element.RenderTransform, transform)) element.RenderTransform = Transform.Identity; };
        transform.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    public static void RevealRows(DataGrid grid)
    {
        grid.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            int order = 0;
            foreach (var row in Rows(grid)) Reveal(row, true, Math.Min(order++ * 38, 190));
        }));
    }

    private static IEnumerable<DataGridRow> Rows(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is DataGridRow row) yield return row;
            else foreach (var nested in Rows(child)) yield return nested;
        }
    }

    public static void Hover(FrameworkElement element, bool entered)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        var transform = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
        element.RenderTransform = transform;
        var motion = new DoubleAnimation(transform.Y, entered ? -2 : 0, TimeSpan.FromMilliseconds(180))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
        transform.Y = entered ? -2 : 0;
        motion.Completed += (_, _) => { if (!entered && ReferenceEquals(element.RenderTransform, transform)) element.RenderTransform = Transform.Identity; };
        transform.BeginAnimation(TranslateTransform.YProperty, motion);
    }

    public static void Reset(FrameworkElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.RenderTransform = Transform.Identity;
    }

    public static async Task ExitAsync(FrameworkElement element)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160)));
        var transform = new TranslateTransform();
        element.RenderTransform = transform;
        transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 24, TimeSpan.FromMilliseconds(180))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
        await Task.Delay(190);
    }

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.RegisterAttached(
        "Progress", typeof(double), typeof(FlowMotion), new PropertyMetadata(0d, OnProgress));
    public static double GetProgress(DependencyObject element) => (double)element.GetValue(ProgressProperty);
    public static void SetProgress(DependencyObject element, double value) => element.SetValue(ProgressProperty, value);
    private static void OnProgress(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not ProgressBar bar) return;
        double target = Math.Clamp((double)args.NewValue, bar.Minimum, bar.Maximum);
        double from = bar.Value;
        bar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null);
        bar.Value = target;
        if (bar.IsLoaded && SystemParameters.ClientAreaAnimation)
            bar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty,
                new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(260))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }
}
