using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using McDm.App;
using McDm.App.Resources;
using McDm.Core;
using AppSettings = McDm.App.AppSettings;

internal static class FlowUiChecks
{
    public static void Run(string directory)
    {
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        ThemeManager.ApplyThemeMode("dark");
        ThemeManager.ApplyAccentColor("#C3AFFF");
        Strings.SetLanguage("en");
        var appBuild = typeof(MainWindow).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .OfType<System.Reflection.AssemblyMetadataAttribute>().Single(attribute => attribute.Key == "StableBuild").Value;
        Require(appBuild == UpdateChecker.CurrentStableBuild.ToString(), "App/Core stable-build metadata differ");
        var family = FontCatalog.Interface;
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/FMDownloadManager;component/Resources/Fonts/Inter-Regular.ttf"));
        Console.WriteLine("Font resource bytes: " + resource?.Stream.Length);
        resource?.Stream.Dispose();
        foreach (var available in Fonts.GetFontFamilies(new Uri("pack://application:,,,/FMDownloadManager;component/Resources/Fonts/")))
            Console.WriteLine("Available family: " + available.Source + " / " + string.Join(",", available.FamilyNames.Values));
        foreach (var weight in new[] { FontWeights.Normal, FontWeights.Medium, FontWeights.SemiBold })
        {
            var typeface = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal);
            Require(typeface.TryGetGlyphTypeface(out var glyph), "embedded Inter face could not be loaded");
            Require(glyph.FontUri.ToString().Contains("Inter-", StringComparison.OrdinalIgnoreCase), "font fell back to a system font: " + glyph.FontUri);
            Console.WriteLine($"PASS embedded font: {weight} -> {glyph.FontUri}");
        }
        foreach (var scale in new[] { 1d, 1.25, 1.5, 2 })
        {
            var work = new Rect(-1280 / scale, 0, 1280 / scale, 720 / scale);
            var fit = WindowPlacement.FitBounds(work, new Rect(-1400, -80, 1160, 920), true);
            Require(work.Contains(fit), "startup window exceeds work area at " + scale);
        }
        Console.WriteLine("PASS startup bounds: 100/125/150/200% scaling, negative monitor coordinates");

        var manager = new DownloadManager(Path.Combine(directory, "data"), directory,
            uiContext: new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new MainWindow(manager, new TokenStore(Path.Combine(directory, "bridge.token")),
            new AppSettings { DownloadDirectory = directory, ClipboardWatcherEnabled = false, NotificationsEnabled = false });
        try
        {
            new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            var workArea = WindowPlacement.WorkAreaFor(window);
            Require(workArea.Contains(new Rect(window.Left, window.Top, window.Width, window.Height)), "native startup window is outside its monitor work area");
            Console.WriteLine("PASS native startup window bounds on the current monitor");
            foreach (var actual in new[] { window.FontFamily, ((System.Windows.Controls.TextBlock)window.FindName("FlowHeading")).FontFamily })
            {
                Require(new Typeface(actual, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal).TryGetGlyphTypeface(out var glyph), "a visible control fell back from Inter");
                Require(glyph.FontUri.ToString().Contains("Inter-", StringComparison.OrdinalIgnoreCase), "visible control is using a system font");
            }
            var content = (FrameworkElement)window.Content;
            // Layout fixtures are confined to this test's manager; no user history is loaded.
            for (int n = 0; n < 24; n++)
                manager.Items.Add(new DownloadItem { FileName = $"Layout fixture {n:00}.txt", Request = new DownloadRequest { Url = "https://example.invalid/layout" }, Status = DownloadStatus.Completed, TotalBytes = 256, DownloadedBytes = 256 });
            var selectionGrid = (DataGrid)window.FindName("Grid");
            selectionGrid.SelectedIndex = 0;
            var held = manager.Items.Skip(3).ToList();
            foreach (var heldItem in held) manager.Items.Remove(heldItem);
            Layout(content, new Size(1160, 800));
            var shortRow = (DataGridRow)selectionGrid.ItemContainerGenerator.ContainerFromIndex(0)!;
            Require(Descendants<TextBlock>(shortRow).Any(text => text.Text == "Layout fixture 00.txt" && text.ActualWidth > 100),
                "few-row card content disappeared");
            foreach (var heldItem in held) manager.Items.Add(heldItem);
            foreach (var size in new[] { new Size(1024, 700), new Size(900, 600), new Size(800, 520), new Size(616, 336), new Size(1280, 900), new Size(1024, 700) })
            {
                Layout(content, size);
                var list = (DataGrid)window.FindName("Grid");
                Require(VirtualizingPanel.GetScrollUnit(list) == ScrollUnit.Pixel, "list still scrolls in whole rows");
                Require(SmoothScroll.GetIsEnabled(list), "smooth scrolling was not attached");
                Require(list.ActualHeight >= 85, $"compact layout leaves too little space for the download list at {size}: {list.ActualHeight}");
                var detailsPanel = (FrameworkElement)window.FindName("FlowInlineDetails");
                Require(detailsPanel.IsVisible || detailsPanel.Visibility == Visibility.Visible, "details panel was hidden");
                Require(detailsPanel.ActualHeight >= 30 && detailsPanel.MaxHeight > 0, "selected details disappeared in compact mode");
                var bar = Descendants<ScrollBar>(content).FirstOrDefault(b => b.Orientation == Orientation.Vertical);
                if (bar != null) Require(bar.Width <= 10, "legacy wide scrollbar returned");
            }
            Console.WriteLine("PASS compact/full layouts and pixel scrolling without layout oscillation");
            var grid = (DataGrid)window.FindName("Grid");
            var firstRow = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0)!;
            Require(Descendants<TextBlock>(firstRow).Any(text => text.Text == "Layout fixture 00.txt" && text.ActualWidth > 100),
                "download filename was not laid out inside the card");
            Require(firstRow.ActualHeight <= 68, "completed download cards are still oversized");
            var changing = (DownloadItem)firstRow.Item;
            Task.Run(() => { changing.TotalBytes = 1000; changing.DownloadedBytes = 750; changing.Speed = 100; changing.Status = DownloadStatus.Downloading; }).GetAwaiter().GetResult();
            Pump(120);
            var changingBar = Descendants<ProgressBar>(firstRow).First();
            Require(Math.Abs(FlowMotion.GetProgress(changingBar) - .75) < .01, "background download progress did not update its actual card binding");
            var remaining = Descendants<TextBlock>(firstRow).First(text => System.Windows.Data.BindingOperations.GetBindingExpression(text, TextBlock.TextProperty)?.ParentBinding.Path?.Path == "EtaFormatted");
            Require(remaining.Text.Any(char.IsDigit), "actual card did not show the remaining-time estimate");
            changing.Status = DownloadStatus.Completed; changing.TotalBytes = changing.DownloadedBytes = 256; changing.Speed = 0;
            Pump(80);
            Console.WriteLine("PASS background progress and remaining-time updates in actual card bindings");
            grid.SelectedItems.Clear();
            grid.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Source = firstRow });
            Require(grid.SelectedItems.Contains(firstRow.Item), "clicking card whitespace did not select it");
            ((Button)window.FindName("BtnSelectAll")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(grid.SelectedItems.Count == grid.Items.Count, "select-all did not select visible downloads");
            Require(!((Button)window.FindName("BtnPause")).IsEnabled, "pause is enabled for completed files");
            Require(((Button)window.FindName("BtnRemove")).IsEnabled, "remove is disabled for selected files");
            grid.SelectedIndex = 0;
            Console.WriteLine("PASS compact selected details, full-card selection, select-all and action availability");
            var gridViewer = SmoothScroll.FindViewer(grid)!;
            Require(gridViewer.ScrollableHeight > 200, "download list extent is still expressed in rows");
            grid.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, -120)
                { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent });
            Pump(45);
            if (SystemParameters.ClientAreaAnimation) Require(gridViewer.VerticalOffset > 0 && gridViewer.VerticalOffset < 80, "the real download list still jumps by whole rows");
            Pump(260);
            Require(gridViewer.VerticalOffset > 0, "download list did not handle wheel input");
            double beforeTouchpad = gridViewer.VerticalOffset;
            grid.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, -30)
                { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent });
            Pump(35);
            Require(gridViewer.VerticalOffset > beforeTouchpad, "high-resolution touchpad input was swallowed");
            Console.WriteLine("PASS actual download-list wheel routing and pixel offset animation");

            var parts = new[] {
                new DownloadItem { FileName = "Archive.part01.rar", Status = DownloadStatus.Completed, TotalBytes = 1000, DownloadedBytes = 1000 },
                new DownloadItem { FileName = "Archive.part02.rar", Status = DownloadStatus.Completed, TotalBytes = 2000, DownloadedBytes = 2000 }
            };
            foreach (var part in parts) manager.Items.Add(part);
            Pump(650);
            Layout(content, new Size(1024, 700));
            gridViewer.ScrollToEnd();
            Pump(180);
            var expander = Descendants<Expander>(content).FirstOrDefault(item => item.DataContext is System.Windows.Data.CollectionViewGroup { Name: MultipartGroup { IsMultipart: true } });
            Require(expander != null && !expander.IsExpanded, "multipart volumes were not collapsed into one group");
            var group = (System.Windows.Data.CollectionViewGroup)expander!.DataContext;
            var summary = ((MultipartGroup)group.Name).Summary;
            Require(group.ItemCount == 2 && summary.TotalBytes == 3000 && summary.Progress == 1, "multipart aggregate did not reflect its actual jobs");
            expander.IsExpanded = true;
            Layout(content, new Size(1024, 700));
            var selectGroup = Descendants<Button>(expander).First(button => Equals(button.Tag, "multipart-select"));
            selectGroup.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(grid.SelectedItems.Count == 2 && parts.All(part => grid.SelectedItems.Contains(part)), "group selection did not select both underlying jobs");
            foreach (var part in parts) manager.Items.Remove(part);
            grid.SelectedItems.Clear();
            Console.WriteLine("PASS multipart aggregate, collapsed/expanded groups, group selection and row virtualization");

            var viewer = new ScrollViewer { Height = 180, Width = 300, Content = new Border { Height = 2400 }, CanContentScroll = false };
            var motionWindow = new Window { Content = viewer, WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000, Top = -32000, Width = 300, Height = 180, ShowInTaskbar = false, ShowActivated = false };
            try
            {
                motionWindow.Show();
                Pump(80);
                Layout(viewer, new Size(300, 180));
                SmoothScroll.ScrollTo(viewer, 420);
                Pump(45);
                if (SystemParameters.ClientAreaAnimation)
                    Require(viewer.VerticalOffset > 0 && viewer.VerticalOffset < 420, "scroll jumped directly to its target instead of animating");
                Pump(260);
                Require(Math.Abs(viewer.VerticalOffset - 420) < 1, "scroll did not settle at target: " + viewer.VerticalOffset);
                SmoothScroll.ScrollTo(viewer, 800);
                Pump(30);
                SmoothScroll.ScrollTo(viewer, 1000);
                Pump(280);
                Require(Math.Abs(viewer.VerticalOffset - 1000) < 1, "interrupted wheel animation settled incorrectly");
                FlowMotion.Reveal(viewer);
                Pump(35);
                if (SystemParameters.ClientAreaAnimation) Require(viewer.Opacity < 1, "reveal animation did not start");
                Pump(380);
                Require(Math.Abs(viewer.Opacity - 1) < .01, "reveal did not settle");
                FlowMotion.Hover(viewer, true);
                Pump(45);
                if (SystemParameters.ClientAreaAnimation)
                {
                    var lift = (TranslateTransform)viewer.RenderTransform;
                    Require(lift.Y < 0 && lift.Y > -2, "hover lift skipped intermediate frames");
                }
                FlowMotion.Hover(viewer, false);
                Pump(230);
                Require(viewer.RenderTransform == Transform.Identity, "hover left text under a permanent transform");
                FlowMotion.Reveal(viewer, true, 80);
                Pump(100);
                FlowMotion.Reset(viewer);
                Pump(450);
                Require(Math.Abs(viewer.Opacity - 1) < .01 && viewer.RenderTransform == Transform.Identity,
                    "recycled row retained an entrance animation");
                var progress = new ProgressBar { Minimum = 0, Maximum = 1 };
                FlowMotion.SetProgress(progress, .65);
                Require(Math.Abs(progress.Value - .65) < .001, "progress animation changed the reported target");
                Console.WriteLine("PASS hover motion, recycled-row reset and actual progress target");
                Console.WriteLine("PASS scroll/reveal animation: intermediate frames, interruption and final position");
            }
            finally { motionWindow.Close(); }
            for (int n = 0; n < 300; n++) manager.Items.Add(new DownloadItem { FileName = $"Scroll stress {n}.txt", Status = DownloadStatus.Completed });
            Layout(content, new Size(1024, 700));
            Require(Descendants<DataGridRow>(grid).Count() < 60, "grouping disabled row virtualization and made scrolling expensive");
            Console.WriteLine("PASS grouped row virtualization with 300 downloads");

        }
        finally
        {
            window.AllowClose();
            Task.Run(async () => await manager.DisposeAsync()).GetAwaiter().GetResult();
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Layout(FrameworkElement element, Size size)
    {
        element.Measure(size); element.Arrange(new Rect(size)); element.UpdateLayout(); Pump(80);
        element.Measure(size); element.Arrange(new Rect(size)); element.UpdateLayout();
    }
    private static void Pump(int milliseconds)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame); Thread.Sleep(5);
        }
    }
}
