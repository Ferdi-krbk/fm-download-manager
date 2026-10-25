using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using McDm.App.Resources;
using McDm.Core;

namespace McDm.App;

public partial class MainWindow : Window
{
    public static readonly RoutedCommand PasteCommand = new();
    private readonly DownloadManager _manager;
    private readonly TokenStore _tokenStore;
    private readonly AppSettings _settings;
    private readonly BridgeServer? _bridge;
    private readonly ICollectionView _view;
    private readonly DispatcherTimer _speedTimer;
    private bool _allowClose;
    private string _statusFilter = "all";
    private (int Active, int Queued, int Done) _filterCounts;
    private (bool Compact, bool Tiny, bool Narrow)? _layoutDensity;
    private bool _entered;
    private readonly bool _constrainToWorkArea;
    private readonly MultipartGroups _multipartGroups = new();

    public MainWindow(DownloadManager manager, TokenStore tokenStore, AppSettings settings, BridgeServer? bridge = null, bool constrainToWorkArea = true)
    {
        _constrainToWorkArea = constrainToWorkArea;
        InitializeComponent();
        _manager = manager;
        _tokenStore = tokenStore;
        _settings = settings;
        _bridge = bridge;

        _view = CollectionViewSource.GetDefaultView(_manager.Items);
        _view.Filter = FilterItem;
        _multipartGroups.Refresh(_view.Cast<DownloadItem>());
        _view.GroupDescriptions.Add(new PropertyGroupDescription { Converter = _multipartGroups });
        Grid.ItemsSource = _view;

        Grid.LoadingRow += (_, args) =>
        {
            FlowMotion.Reset(args.Row);
            args.Row.MouseEnter -= RowMouseEnter;
            args.Row.MouseLeave -= RowMouseLeave;
            args.Row.MouseEnter += RowMouseEnter;
            args.Row.MouseLeave += RowMouseLeave;
        };
        Grid.UnloadingRow += (_, args) => FlowMotion.Reset(args.Row);
        Grid.PreviewMouseLeftButtonDown += CardPointerDown;
        Grid.PreviewMouseRightButtonDown += (_, args) =>
        {
            for (var node = args.OriginalSource as DependencyObject; node != null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
                if (node is DataGridRow row)
                {
                    if (!Grid.SelectedItems.Contains(row.Item)) Grid.SelectedItem = row.Item;
                    break;
                }
        };
        Grid.SelectionChanged += (_, _) =>
        {
            UpdateActionButtons();
            if (FindName("FlowInlineDetails") is FrameworkElement panel && IsLoaded) FlowMotion.Reveal(panel, true);
        };
        AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler((_, args) =>
        {
            if (args.OriginalSource is Button button) FlowMotion.Tap(button);
        }));
        ApplyLocalization();

        _speedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _speedTimer.Tick += (_, _) => UpdateTotalSpeed();
        _speedTimer.Start();
        FlowMain.SizeChanged += (_, _) => UpdateLayoutDensity();
        Loaded += (_, _) =>
        {
            if (_constrainToWorkArea) WindowPlacement.Constrain(this, center: !_entered);
            if (!_entered)
            {
                FlowMotion.Reveal(FlowHeading, true);
                FlowMotion.Reveal(FlowPulse, true, 90);
                FlowMotion.Reveal(FlowSearchHost, true, 150);
                FlowMotion.Reveal(BtnAddUrls, true, 70);
                FlowMotion.Reveal(BtnAddFromClipboard, true, 130);
            }
            if (!_entered) FlowMotion.RevealRows(Grid);
            _entered = true;
        };
        DpiChanged += (_, _) => { if (_constrainToWorkArea) Dispatcher.BeginInvoke(new Action(() => WindowPlacement.Constrain(this))); };
        Closed += (_, _) => _speedTimer.Stop();

        // Enforce application icon on window and taskbar
        try
        {
            Uri icoUri = new Uri("pack://application:,,,/FMDownloadManager;component/Resources/app.ico");
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
                icoUri,
                System.Windows.Media.Imaging.BitmapCreateOptions.None,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        }
        catch
        {
            try
            {
                Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
                    new Uri("pack://application:,,,/Resources/app.ico"),
                    System.Windows.Media.Imaging.BitmapCreateOptions.None,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            }
            catch { }
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_SETICON = 0x0080;
    private static readonly IntPtr ICON_SMALL = IntPtr.Zero;
    private static readonly IntPtr ICON_BIG = new IntPtr(1);
    private System.Drawing.Icon? _iconBig;
    private System.Drawing.Icon? _iconSmall;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (_constrainToWorkArea) WindowPlacement.Constrain(this, center: true);
        try
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;
            if (hwnd != IntPtr.Zero)
            {
                System.Windows.Resources.StreamResourceInfo? sri = null;
                try { sri = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/FMDownloadManager;component/Resources/app.ico")); } catch { }
                if (sri == null)
                {
                    try { sri = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Resources/app.ico")); } catch { }
                }

                if (sri?.Stream != null)
                {
                    using var ms = new MemoryStream();
                    sri.Stream.CopyTo(ms);
                    ms.Position = 0;
                    _iconBig = new System.Drawing.Icon(ms, 128, 128);
                    ms.Position = 0;
                    _iconSmall = new System.Drawing.Icon(ms, 32, 32);
                }
                else
                {
                    string? exe = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                    {
                        var assoc = System.Drawing.Icon.ExtractAssociatedIcon(exe);
                        if (assoc != null)
                        {
                            _iconBig = assoc;
                            _iconSmall = assoc;
                        }
                    }
                }

                if (_iconBig != null) SendMessage(hwnd, WM_SETICON, ICON_BIG, _iconBig.Handle);
                if (_iconSmall != null) SendMessage(hwnd, WM_SETICON, ICON_SMALL, _iconSmall.Handle);
            }
        }
        catch { }
    }

    public void ApplyLocalization()
    {
        Title = Strings.AppTitle;

        TxtSearch.SetValue(WatermarkService.WatermarkProperty, Strings.SearchPlaceholder);

        CtxOpen.Header = Strings.Open;
        CtxOpenFolder.Header = Strings.OpenFolder;
        CtxPause.Header = Strings.Pause;
        CtxResume.Header = Strings.Resume;
        CtxCancel.Header = Strings.Cancel;
        CtxRemove.Header = Strings.Remove;
        CtxRemoveAndDelete.Header = Strings.RemoveAndDelete;
        CtxCopyUrl.Header = Strings.CopyUrl;
        CtxDetails.Header = Strings.Details;
        CtxAntivirusScan.Header = Strings.IsTurkish ? "Antivir\u00fcs ile tara" : "Scan with antivirus";

        ColHeaderName.Header = Strings.ColName;
        ColHeaderSize.Header = Strings.ColSize;
        ColHeaderProgress.Header = Strings.ColProgress;
        ColHeaderSpeed.Header = Strings.ColSpeed;
        ColHeaderConn.Header = Strings.ColConnections;
        ColHeaderStatus.Header = Strings.ColStatus;
        ColHeaderError.Header = Strings.IsTurkish ? "Ayrıntılar" : "Details";
        if (ColHeaderEta != null) ColHeaderEta.Header = Strings.TimeLeft;

        TxtBtnAdd.Text = Strings.IsTurkish ? "Panodan indir" : "From clipboard";
        TxtAddLinks.Text = Strings.IsTurkish ? "Bağlantı ekle" : "Add links";
        Resources["FlowEtaCaption"] = Strings.IsTurkish ? "Kalan:" : "Left:";
        Resources["FlowEtaHelp"] = Strings.IsTurkish ? "Boyut ve hız belli olduğunda kalan süre hesaplanır." : "Time left is calculated when size and speed are known.";
        BtnSelectAll.ToolTip = Strings.IsTurkish ? "Görünenlerin tümünü seç (Ctrl+A)" : "Select all visible downloads (Ctrl+A)";
        BtnToolbarDetails.ToolTip = Strings.Details;
        Resources["FlowGroupSelect"] = Strings.IsTurkish ? "Gruptaki par\u00e7alar\u0131 se\u00e7" : "Select all parts in this group";
        TxtBtnAddMulti.Text = Strings.AddUrls;
        TxtBtnFolder.Text = Strings.OpenFolder;
        TxtBtnExtension.Text = Strings.BrowserSetup;

        CatAllText.Text = Strings.CategoryAll;
        CatImagesText.Text = Strings.CategoryImages;
        CatMusicText.Text = Strings.CategoryMusic;
        CatVideoText.Text = Strings.CategoryVideo;
        CatAppsText.Text = Strings.CategoryApps;
        CatDocsText.Text = Strings.CategoryDocuments;
        CatArchivesText.Text = Strings.CategoryArchives;
        CatTorrentsText.Text = Strings.CategoryTorrents;
        CatOtherText.Text = Strings.CategoryOther;

        CatAll.ToolTip = Strings.CategoryAll;
        CatImages.ToolTip = Strings.CategoryImages;
        CatMusic.ToolTip = Strings.CategoryMusic;
        CatVideo.ToolTip = Strings.CategoryVideo;
        CatApps.ToolTip = Strings.CategoryApps;
        CatDocuments.ToolTip = Strings.CategoryDocuments;
        CatArchives.ToolTip = Strings.CategoryArchives;
        CatTorrents.ToolTip = Strings.CategoryTorrents;
        CatOther.ToolTip = Strings.CategoryOther;

        bool isTr = Strings.IsTurkish;
        CatOtherText.Text = isTr ? "Dosyalar" : "Files";
        CatMusicText.Text = isTr ? "Ses" : "Audio";
        TxtSearchWatermark.Text = isTr ? "Dosya adıyla ara…" : "Search downloads…";
        FlowWorkspaceLabel.Text = isTr ? "ÇALIŞMA ALANI" : "WORKSPACE";
        FlowWorkspaceAll.Text = isTr ? "İndirmeler" : "Downloads";
        FlowWorkspaceActive.Text = isTr ? "Aktif indirmeler" : "Active downloads";
        FlowWorkspaceDone.Text = isTr ? "Tamamlananlar" : "Completed";
        FlowListLabel.Text = isTr ? "İNDİRMELER" : "DOWNLOADS";
        FlowSizeLabel.Text = isTr ? "İndirilen" : "Downloaded";
        FlowEtaLabel.Text = isTr ? "Kalan süre" : "Time left";
        FlowHeading.Text = isTr ? "Her şey akışında." : "Everything in flow.";
        FlowEyebrow.Text = isTr ? "KİŞİSEL İNDİRME ALANIN" : "YOUR DOWNLOAD WORKSPACE";
        FlowSubtitle.Text = isTr ? "İndirmelerini tek yerden yönet." : "Your downloads, all in one place.";
        FlowSelectedLabel.Text = isTr ? "İNDİRME AYRINTILARI" : "DOWNLOAD DETAILS";
        FlowDetailsButton.Content = isTr ? "Ayrıntılar" : "Details";
        FilterAll.Content = isTr ? "Tümü" : "All";
        FilterActive.Content = isTr ? "İndiriliyor" : "Downloading";
        FilterPaused.Content = isTr ? "Duraklatılan" : "Paused";
        FilterDone.Content = isTr ? "Tamamlanan" : "Completed";
        LblCategories.Text = isTr ? "KİTAPLIK" : "LIBRARY";
        LblTotalSpeed.Text = isTr ? "TOPLAM İNDİRME HIZI" : "TOTAL DOWNLOAD SPEED";
        LblBridgeConnected.Text = isTr ? "Yerel köprü hazır" : "Local bridge ready";
        if (LblTransferStatus != null) LblTransferStatus.Text = isTr ? "TRANSFER DURUMU" : "TRANSFER STATUS";
        if (LblMultiThreadOptimize != null) LblMultiThreadOptimize.Text = isTr ? "Paralel HTTP aktarımı" : "Parallel HTTP transfers";
        if (LblStorageBridge != null) LblStorageBridge.Text = isTr ? "DEPOLAMA & EKLENTİ" : "STORAGE & EXTENSION";
        if (LblNativeBridgeActive != null) LblNativeBridgeActive.Text = isTr ? "● Yerel tarayıcı köprüsü" : "● Local browser bridge";
        if (BtnEmptyAddUrl != null) BtnEmptyAddUrl.Content = isTr ? "URL Ekle" : "Add URL";
        if (BtnThemeDark != null) BtnThemeDark.Content = isTr ? "Gece" : "Dark";
        if (BtnThemeLight != null) BtnThemeLight.Content = isTr ? "Gündüz" : "Light";
        if (LblPaletteHeader != null) LblPaletteHeader.Text = Strings.PaletteHeader;
        if (BtnPaletteOpenSettings != null) BtnPaletteOpenSettings.Content = Strings.PaletteOpenAllSettings;
        TxtEmptyTitle.Text = Strings.NoDownloadsTitle;
        TxtEmptySubtitle.Text = Strings.NoDownloadsSub;
        if (TxtDragDropTitle != null) TxtDragDropTitle.Text = Strings.DragDropTitle;
        if (TxtDragDropSubtitle != null) TxtDragDropSubtitle.Text = Strings.DragDropSubtitle;

        if (DiskSpaceText != null)
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(_settings.DownloadDirectory) ?? "C:\\");
                if (drive.IsReady)
                {
                    string free = BytesConverter.Format(drive.AvailableFreeSpace);
                    DiskSpaceText.Text = isTr ? $"DİSK: {free} BOŞ" : $"DISK: {free} FREE";
                }
                else
                {
                    DiskSpaceText.Text = isTr ? "DİSK: HAZIR" : "DISK: READY";
                }
            }
            catch
            {
                DiskSpaceText.Text = isTr ? "DİSK: HAZIR" : "DISK: READY";
            }
        }

        if (LblPaletteHeader != null) LblPaletteHeader.Text = Strings.PaletteHeader;
        if (BtnPaletteOpenSettings != null) BtnPaletteOpenSettings.Content = Strings.PaletteOpenAllSettings;

        BtnAddFromClipboard.ToolTip = isTr ? "Panodaki URL'yi İndir" : "Download URL from Clipboard";
        BtnAddUrls.ToolTip = isTr ? "Toplu Bağlantı Ekle" : "Add Multiple URLs";
        BtnPause.ToolTip = isTr ? "Seçileni Duraklat" : "Pause Selected";
        BtnResume.ToolTip = isTr ? "Seçileni Devam Ettir" : "Resume Selected";
        BtnCancel.ToolTip = isTr ? "Seçileni İptal Et" : "Cancel Selected";
        BtnRemove.ToolTip = isTr ? "Listeden Kaldır" : "Remove from List";
        BtnOpenFolder.ToolTip = isTr ? "İndirme Klasörünü Aç" : "Open Download Folder";
        BtnBrowserSetup.ToolTip = isTr ? "Tarayıcı Eklentisi Kurulumu" : "Browser Extension Setup";
        BtnPalette.ToolTip = Strings.PaletteTooltip;
        BtnSettings.ToolTip = Strings.Settings;
        BtnUpdateYtDlp.ToolTip = Strings.UpdateYtDlp;
        if (BtnUpdateAvailable != null) BtnUpdateAvailable.ToolTip = isTr ? "Yeni bir sürüm yayınlandı! Tıklayarak inceleyin." : "A new version is available! Click to check.";

        if (_settings.SpeedLimitKbps > 0)
        {
            SldSpeedLimit.Value = Math.Round((double)_settings.SpeedLimitKbps / 1024);
            _manager.SpeedLimiter.BytesPerSecond = (long)_settings.SpeedLimitKbps * 1024;
            TxtSpeedLimitVal.Text = $"{_settings.SpeedLimitKbps / 1024} MB/s";
        }
        else
        {
            TxtSpeedLimitVal.Text = Strings.Unlimited;
        }

        if (BtnSpeedAuto != null) BtnSpeedAuto.Content = isTr ? "Oto" : "Auto";
        StatusText.Text = Strings.StatusReady;
        UpdateTotalSpeed();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void UpdateTotalSpeed()
    {
        double totalSpeed = _manager.Items.Where(i => i.Status == DownloadStatus.Downloading).Sum(i => i.Speed);
        int active = _manager.Items.Count(i => i.Status == DownloadStatus.Downloading);
        int queued = _manager.Items.Count(i => i.Status == DownloadStatus.Queued);
        int done = _manager.Items.Count(i => i.Status == DownloadStatus.Completed);

        TotalSpeedText.Text = active > 0 ? $"{BytesConverter.Format(totalSpeed)}/s" : "0 B/s";
        SidebarSpeedGraph.PushSample(totalSpeed);
        UpdateActionButtons();
        if (_multipartGroups.Refresh(_view.Cast<DownloadItem>())) _view.Refresh();
        bool isTr = Strings.IsTurkish;
        ActiveCountText.Text = isTr ? $"{active} aktif indirme" : $"{active} active download{(active == 1 ? "" : "s")}";
        StatusCountsText.Text = isTr
            ? $"{active} aktif / {queued} sırada / {done} tamamlandı"
            : $"{active} active / {queued} queued / {done} completed";
        EmptyStateOverlay.Visibility = _manager.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_filterCounts != (active, queued, done))
        {
            _filterCounts = (active, queued, done);
            if (_statusFilter != "all") _view.Refresh();
        }
    }

    private bool FilterItem(object obj)
    {
        if (obj is not DownloadItem item) return false;
        if (_statusFilter switch
        {
            "active" => item.Status != DownloadStatus.Downloading,
            "paused" => item.Status != DownloadStatus.Paused,
            "done" => item.Status != DownloadStatus.Completed,
            _ => false
        }) return false;

        // Search text
        string q = TxtSearch?.Text?.Trim() ?? "";
        if (!string.IsNullOrEmpty(q))
        {
            bool nameMatch = item.FileName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false;
            bool urlMatch = item.Request?.Url?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false;
            if (!nameMatch && !urlMatch) return false;
        }

        // Category
        string cat = (LstCategories?.SelectedItem as ListBoxItem)?.Tag?.ToString() ?? "all";
        if (cat == "all") return true;

        string category = item.Request is { } request
            ? CategoryHelper.GetCategorySubfolder(request, item.FileName)
            : CategoryHelper.GetCategorySubfolder(item.FileName, null, DownloadKind.File);
        return cat switch
        {
            "video" => category == "Videos",
            "music" => category == "Music",
            "torrents" => category == "Torrents",
            "files" => category is not ("Videos" or "Music" or "Torrents"),
            "images" => category == "Images",
            "apps" => category == "Programs",
            "documents" => category == "Documents",
            "archives" => category == "Compressed",
            _ => true
        };
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        TxtSearchWatermark.Visibility = string.IsNullOrEmpty(TxtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        _view.Refresh();
    }

    private void Categories_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _view?.Refresh();
        if (Grid != null && IsLoaded) FlowMotion.RevealRows(Grid);
    }

    private void StatusFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Primitives.ToggleButton selected) return;
        _statusFilter = selected.Tag?.ToString() ?? "all";
        foreach (var button in new[] { FilterAll, FilterActive, FilterPaused, FilterDone })
            button.IsChecked = button == selected;
        _view?.Refresh();
        if (IsLoaded) FlowMotion.RevealRows(Grid);
    }

    private void UpdateLayoutDensity()
    {
        // Margins are included so density changes cannot shift the threshold.
        bool compact = FlowMain.ActualHeight + FlowMain.Margin.Top + FlowMain.Margin.Bottom < 750;
        bool tiny = FlowMain.ActualHeight + FlowMain.Margin.Top + FlowMain.Margin.Bottom < 480;
        bool narrow = FlowMain.ActualWidth + FlowMain.Margin.Left + FlowMain.Margin.Right < 700;
        var density = (compact, tiny, narrow);
        if (_layoutDensity is { } previous && previous == density) return;
        _layoutDensity = density;
        FlowMain.Margin = tiny ? new Thickness(12, 2, 12, 2) : narrow ? new Thickness(18, 16, 18, 14) : compact ? new Thickness(24, 20, 24, 16) : new Thickness(28, 29, 28, 20);
        FlowPulseRow.Height = new GridLength(tiny ? 0 : compact ? 82 : 116);
        FlowPulse.Visibility = tiny ? Visibility.Collapsed : Visibility.Visible;
        FlowPulse.Padding = compact ? new Thickness(0, 4, 0, 4) : new Thickness(0, 12, 0, 12);
        TotalSpeedText.FontSize = compact ? 34 : 43;
        SidebarSpeedGraph.Height = compact ? 42 : 52;
        FlowFiltersRow.Height = new GridLength(tiny ? 38 : compact ? 48 : 58);
        BtnAddUrls.Height = BtnAddFromClipboard.Height = tiny ? 32 : 38;
        FlowHeading.FontSize = narrow ? 24 : compact ? 26 : 30;
        FlowSearchHost.Width = narrow ? 140 : 180;
        foreach (var filter in new[] { FilterAll, FilterActive, FilterPaused, FilterDone })
        {
            filter.FontSize = narrow ? 11 : 12;
            filter.Padding = new Thickness(narrow ? 8 : 12, 7, narrow ? 8 : 12, 7);
        }
        FlowEyebrow.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        FlowSubtitle.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        FlowInlineDetails.MaxHeight = double.PositiveInfinity;
        FlowInlineDetails.Margin = new Thickness(0, compact ? 8 : 12, 0, 0);
        FlowDetailsBody.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        FlowCompactErrorHost.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
    }
    private void WorkspaceAll_Click(object sender, RoutedEventArgs e) { LstCategories.SelectedItem = CatAll; StatusFilter_Click(FilterAll, e); }
    private void WorkspaceActive_Click(object sender, RoutedEventArgs e) { LstCategories.SelectedItem = CatAll; StatusFilter_Click(FilterActive, e); }
    private void WorkspaceDone_Click(object sender, RoutedEventArgs e) { LstCategories.SelectedItem = CatAll; StatusFilter_Click(FilterDone, e); }

    public void AllowClose() { _allowClose = true; Close(); }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Dispatcher.BeginInvoke(new Action(Hide));
        }
        base.OnClosing(e);
    }

    private object? _selectionAnchor;
    private void CardPointerDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        DataGridRow? row = null;
        for (var node = source; node != null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is System.Windows.Controls.Primitives.ScrollBar) return;
            // Cells keep WPF's native keyboard/range selection and double-click handling.
            if (node is DataGridCell) { if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) _selectionAnchor = Grid.SelectedItem; return; }
            if (node is DataGridRow found) { row = found; break; }
        }
        if (row == null || e.ClickCount > 1) return;
        Grid.Focus();
        Grid.CurrentCell = new DataGridCellInfo(row.Item, Grid.Columns[0]);
        bool control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _selectionAnchor != null)
        {
            int from = Grid.Items.IndexOf(_selectionAnchor), to = Grid.Items.IndexOf(row.Item);
            if (!control) Grid.SelectedItems.Clear();
            if (from < 0) from = to;
            for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++)
                if (!Grid.SelectedItems.Contains(Grid.Items[i])) Grid.SelectedItems.Add(Grid.Items[i]);
        }
        else
        {
            if (!control && !Grid.SelectedItems.Contains(row.Item)) Grid.SelectedItems.Clear();
            if (control && Grid.SelectedItems.Contains(row.Item)) Grid.SelectedItems.Remove(row.Item);
            else if (!Grid.SelectedItems.Contains(row.Item)) Grid.SelectedItems.Add(row.Item);
            _selectionAnchor = row.Item;
        }
        e.Handled = true;
    }

    private void SelectMultipart_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CollectionViewGroup group) return;
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) Grid.SelectedItems.Clear();
        foreach (var item in group.Items.OfType<DownloadItem>())
            if (!Grid.SelectedItems.Contains(item)) Grid.SelectedItems.Add(item);
        e.Handled = true;
    }

    private void Multipart_Expanded(object sender, RoutedEventArgs e)
    {
        if (IsLoaded && ReferenceEquals(e.OriginalSource, sender) && sender is Expander { Content: FrameworkElement children })
            FlowMotion.RevealGroup(children);
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => Grid.SelectAll();
    private void UpdateActionButtons()
    {
        var selected = Selected.ToList();
        FlowListLabel.Text = Strings.IsTurkish
            ? (selected.Count > 0 ? $"\u0130ND\u0130RMELER \u00b7 {selected.Count} SE\u00c7\u0130L\u0130" : "\u0130ND\u0130RMELER")
            : (selected.Count > 0 ? $"DOWNLOADS \u00b7 {selected.Count} SELECTED" : "DOWNLOADS");
        BtnPause.IsEnabled = selected.Any(i => i.Status is DownloadStatus.Downloading or DownloadStatus.Queued);
        BtnResume.IsEnabled = selected.Any(i => i.Status is DownloadStatus.Paused or DownloadStatus.Failed);
        BtnCancel.IsEnabled = selected.Any(i => i.Status is DownloadStatus.Downloading or DownloadStatus.Queued or DownloadStatus.Paused);
        BtnRemove.IsEnabled = selected.Count > 0;
        BtnToolbarDetails.IsEnabled = selected.Count > 0;
        CtxAntivirusScan.IsEnabled = selected.Any(item => item.Status is DownloadStatus.Completed or DownloadStatus.Failed && !string.IsNullOrWhiteSpace(item.SavePath));
        BtnSelectAll.IsEnabled = Grid.Items.Count > 0 && selected.Count < Grid.Items.Count;
    }

    private IEnumerable<DownloadItem> Selected => Grid.SelectedItems.Cast<DownloadItem>().ToList();

    private void PasteUrl_Click(object sender, RoutedEventArgs e)
    {
        bool isTr = Strings.IsTurkish;
        var text = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
        if (string.IsNullOrWhiteSpace(text))
        {
            AddUrls_Click(sender, e);
            return;
        }

        if (text.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase) || text.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
        {
            _manager.Add(new DownloadRequest { Url = text, Kind = DownloadKind.Torrent });
            StatusText.Text = isTr ? "Torrent indirmesi eklendi." : "Torrent download added.";
            return;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            AddUrls_Click(sender, e);
            return;
        }

        if (VideoDownloader.IsVideoUrl(text))
        {
            var qDlg = new VideoQualityDialog(text) { Owner = this };
            if (qDlg.ShowDialog() == true)
            {
                var req = new DownloadRequest
                {
                    Url = !string.IsNullOrWhiteSpace(qDlg.ResolvedUrl) ? qDlg.ResolvedUrl : text,
                    FileName = !string.IsNullOrWhiteSpace(qDlg.DetectedTitle) ? qDlg.DetectedTitle : null,
                    Referer = !string.IsNullOrWhiteSpace(qDlg.ResolvedUrl) && qDlg.ResolvedUrl != text ? text : null,
                    Kind = DownloadKind.Video,
                    CustomFormat = qDlg.SelectedFormat,
                    Container = qDlg.AudioOnly ? "mp3" : "mp4",
                    AudioOnly = qDlg.AudioOnly,
                    WriteSubtitles = qDlg.DownloadSubtitles,
                    SubtitleLangs = qDlg.SubtitleLangs,
                    SubtitleUrl = qDlg.SelectedSubtitleUrl
                };
                _manager.Add(req);
                StatusText.Text = isTr
                    ? (qDlg.AudioOnly ? "Ses indirmesi eklendi." : "Video indirmesi eklendi.")
                    : (qDlg.AudioOnly ? "Audio download added." : "Video download added.");
            }
            return;
        }

        var kind = DownloadKind.File;
        _manager.Add(new DownloadRequest { Url = text, Kind = kind });
        StatusText.Text = (isTr ? "Eklendi: " : "Added: ") + uri.Host;
    }

    private void SldSpeedLimit_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtSpeedLimitVal == null) return;
        int mb = (int)Math.Round(SldSpeedLimit.Value);
        if (mb <= 0)
        {
            TxtSpeedLimitVal.Text = Strings.Unlimited;
            _manager.SpeedLimiter.BytesPerSecond = 0;
            _settings.SpeedLimitKbps = 0;
        }
        else
        {
            TxtSpeedLimitVal.Text = $"{mb} MB/s";
            _manager.SpeedLimiter.BytesPerSecond = (long)mb * 1024 * 1024;
            _settings.SpeedLimitKbps = mb * 1024;
        }
        _settings.Save();
    }

    private void SpeedPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && int.TryParse(fe.Tag?.ToString(), out int val))
        {
            SldSpeedLimit.Value = val;
        }
    }

    private void Palette_Click(object sender, RoutedEventArgs e)
    {
        PopulateQuickSwatches();
        PalettePopup.IsOpen = !PalettePopup.IsOpen;
    }

    private void PopulateQuickSwatches()
    {
        PnlQuickSwatches.Children.Clear();
        string current = ThemeManager.CurrentAccentHex;
        bool isTr = Strings.IsTurkish;

        foreach (var preset in ThemeManager.Presets)
        {
            if (!ThemeManager.TryParseHex(preset.Hex, out var col)) continue;

            bool isSelected = string.Equals(current, preset.Hex, StringComparison.OrdinalIgnoreCase);

            var border = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 6, 6),
                Background = new SolidColorBrush(col),
                BorderBrush = isSelected ? (ThemeManager.IsLightTheme ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.White) : new SolidColorBrush(Color.FromArgb(0x40, 0x88, 0x88, 0x88)),
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = ThemeManager.GetPresetDisplayName(preset, isTr)
            };

            if (isSelected)
            {
                border.Child = new System.Windows.Shapes.Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = System.Windows.Media.Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            string hex = preset.Hex;
            string name = ThemeManager.GetPresetDisplayName(preset, isTr);
            border.MouseLeftButtonUp += (_, _) =>
            {
                ThemeManager.ApplyAccentColor(hex);
                _settings.AccentColor = hex;
                _settings.Save();
                PalettePopup.IsOpen = false;
                StatusText.Text = isTr ? $"Vurgu rengi: {name}" : $"Accent color: {name}";
            };

            PnlQuickSwatches.Children.Add(border);
        }
    }

    private void OpenSettingsFromPalette_Click(object sender, RoutedEventArgs e)
    {
        PalettePopup.IsOpen = false;
        Settings_Click(sender, e);
    }

    private void QuickThemeDark_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.ApplyThemeMode("dark");
        _settings.AppTheme = "dark";
        _settings.Save();
        PopulateQuickSwatches();
        SidebarSpeedGraph?.InvalidateVisual();
    }

    private void QuickThemeLight_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.ApplyThemeMode("light");
        _settings.AppTheme = "light";
        _settings.Save();
        PopulateQuickSwatches();
        SidebarSpeedGraph?.InvalidateVisual();
    }

    private void AddUrls_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AddUrlsDialog(_manager.DefaultDownloadDirectory) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.ResultUrls.Count > 0)
        {
            string? targetDir = !string.IsNullOrWhiteSpace(dlg.SelectedDirectory) ? dlg.SelectedDirectory : null;
            foreach (var u in dlg.ResultUrls)
            {
                bool isVid = VideoDownloader.IsVideoUrl(u);
                var kind = isVid ? DownloadKind.Video : DownloadKind.File;
                _manager.Add(new DownloadRequest
                {
                    Url = u,
                    Directory = targetDir,
                    Kind = kind,
                    WriteSubtitles = isVid,
                    SubtitleLangs = isVid ? "tr,en" : null
                });
            }
            StatusText.Text = Strings.IsTurkish ? $"{dlg.ResultUrls.Count} bağlantı eklendi." : $"{dlg.ResultUrls.Count} link(s) added.";
        }
    }

    private void Pause_Click(object s, RoutedEventArgs e) { foreach (var i in Selected) _manager.Pause(i.Id); }
    private void Resume_Click(object s, RoutedEventArgs e) { foreach (var i in Selected) _manager.Resume(i.Id); }
    private void Cancel_Click(object s, RoutedEventArgs e) { foreach (var i in Selected) _manager.Cancel(i.Id); }
    private readonly HashSet<string> _removing = new();
    private void RowMouseEnter(object sender, MouseEventArgs e) { if (!SmoothScroll.IsScrolling(Grid)) FlowMotion.Hover((FrameworkElement)sender, true); }
    private void RowMouseLeave(object sender, MouseEventArgs e) { if (!SmoothScroll.IsScrolling(Grid)) FlowMotion.Hover((FrameworkElement)sender, false); }
    private async void Remove_Click(object s, RoutedEventArgs e)
    {
        var items = Selected.Where(item => _removing.Add(item.Id)).ToList();
        await Task.WhenAll(items.Select(async item =>
        {
            var row = Grid.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
            if (row != null) await FlowMotion.ExitAsync(row);
            _manager.Remove(item.Id);
            if (row != null && ReferenceEquals(row.Item, item)) FlowMotion.Reset(row);
            _removing.Remove(item.Id);
        }));
    }

    private async void RemoveAndDelete_Click(object s, RoutedEventArgs e)
    {
        var itemsToDelete = Selected.ToList();
        foreach (var i in itemsToDelete)
        {
            _manager.Remove(i.Id, deleteFile: true);
        }

        // Wait briefly and retry deletion in case active file handles were still releasing
        await Task.Run(async () =>
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                bool anyRemaining = false;
                foreach (var i in itemsToDelete)
                {
                    if (string.IsNullOrEmpty(i.SavePath)) continue;
                    SegmentedDownloader.TryDelete(i.SavePath);
                    SegmentedDownloader.TryDelete(i.SavePath + ".mcdm-part");
                    SegmentedDownloader.TryDelete(i.SavePath + ".mcdm");
                    SegmentedDownloader.TryDelete(i.SavePath + ".mcdm.tmp");
                    if (File.Exists(i.SavePath) || File.Exists(i.SavePath + ".mcdm-part"))
                        anyRemaining = true;
                }
                if (!anyRemaining) break;
                await Task.Delay(120);
            }
        });
    }

    private async void AntivirusScan_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Selected) await _manager.ScanExistingAsync(item.Id);
    }

    private void Open_Click(object s, RoutedEventArgs e)
    {
        if (Selected.FirstOrDefault() is { } i && !string.IsNullOrEmpty(i.SavePath))
        {
            if (File.Exists(i.SavePath) || Directory.Exists(i.SavePath))
                Process.Start(new ProcessStartInfo(i.SavePath) { UseShellExecute = true });
        }
    }

    private void OpenFolder_Click(object s, RoutedEventArgs e)
    {
        if (Selected.FirstOrDefault() is { } i && !string.IsNullOrEmpty(i.SavePath))
        {
            if (File.Exists(i.SavePath))
                Process.Start("explorer.exe", $"/select,\"{i.SavePath}\"");
            else if (Directory.Exists(i.SavePath))
                Process.Start("explorer.exe", $"\"{i.SavePath}\"");
            else
                Process.Start("explorer.exe", $"\"{_manager.DefaultDownloadDirectory}\"");
        }
        else
        {
            Process.Start("explorer.exe", $"\"{_manager.DefaultDownloadDirectory}\"");
        }
    }

    private void CopyUrl_Click(object s, RoutedEventArgs e)
    {
        if (Selected.FirstOrDefault() is { } i && !string.IsNullOrEmpty(i.Request.Url))
            ClipboardHelper.SetText(i.Request.Url);
    }

    private void Details_Click(object s, RoutedEventArgs e)
    {
        if (Selected.FirstOrDefault() is { } i)
        {
            var win = new DownloadDetailWindow(i) { Owner = this };
            win.ShowDialog();
        }
    }

    private void BrowserSetup_Click(object sender, RoutedEventArgs e)
    {
        var win = new BrowserSetupWindow(_tokenStore, _bridge) { Owner = this };
        win.ShowDialog();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(_settings, updated =>
        {
            _manager.DefaultDownloadDirectory = updated.DownloadDirectory;
            _manager.DefaultConnections = updated.ConnectionsPerDownload;
            _manager.MaxConcurrentDownloads = updated.MaxConcurrentDownloads;
            _manager.AutoCategorizeSubfolders = updated.AutoCategorizeSubfolders;
            _manager.VirusScanEnabled = updated.VirusScanEnabled;
            _manager.CustomAntivirusPath = updated.CustomAntivirusPath;
            _manager.CustomAntivirusArgs = updated.CustomAntivirusArgs;
            _manager.SpeedLimiter.BytesPerSecond = (long)updated.SpeedLimitKbps * 1024;
            SldSpeedLimit.Value = Math.Round((double)updated.SpeedLimitKbps / 1024);
            TxtSpeedLimitVal.Text = updated.SpeedLimitKbps > 0 ? $"{updated.SpeedLimitKbps / 1024} MB/s" : Strings.Unlimited;
            SidebarSpeedGraph?.InvalidateVisual();

            // Apply culture change if user switched language
            Strings.SetLanguage(updated.Language);
            ApplyLocalization();
        })
        { Owner = this };
        win.ShowDialog();
    }

    private async void UpdateYtDlp_Click(object sender, RoutedEventArgs e)
    {
        var tools = new ToolPaths();
        var ytDlpPath = tools.YtDlpPath;

        StatusText.Text = Strings.UpdatingYtDlp;
        BtnUpdateYtDlp.IsEnabled = false;
        try
        {
            if (string.IsNullOrEmpty(ytDlpPath))
            {
                StatusText.Text = Strings.IsTurkish ? "yt-dlp indiriliyor..." : "Downloading yt-dlp...";
                ytDlpPath = await tools.DownloadYtDlpAsync();
                StatusText.Text = Strings.YtDlpUpdated;
                DarkMessageBox.Show(this, Strings.IsTurkish ? "yt-dlp başarıyla indirildi ve kuruldu." : "yt-dlp was successfully downloaded and installed.", Strings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = ytDlpPath,
                Arguments = "-U",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                var errTask = p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();
                var errText = await errTask;
                if (p.ExitCode == 0)
                {
                    StatusText.Text = Strings.YtDlpUpdated;
                    DarkMessageBox.Show(this, Strings.YtDlpUpdated, Strings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    var msg = string.IsNullOrWhiteSpace(errText) ? $"Exit code {p.ExitCode}" : errText.Trim();
                    StatusText.Text = string.Format(Strings.YtDlpUpdateFailed, msg);
                    DarkMessageBox.Show(this, string.Format(Strings.YtDlpUpdateFailed, msg), Strings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = string.Format(Strings.YtDlpUpdateFailed, ex.Message);
            DarkMessageBox.Show(this, string.Format(Strings.YtDlpUpdateFailed, ex.Message), Strings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            BtnUpdateYtDlp.IsEnabled = true;
        }
    }

    private void Grid_DoubleClick(object s, MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is DownloadItem { Status: DownloadStatus.Completed } i && !string.IsNullOrEmpty(i.SavePath) && (File.Exists(i.SavePath) || Directory.Exists(i.SavePath)))
            Process.Start(new ProcessStartInfo(i.SavePath) { UseShellExecute = true });
        else if (Grid.SelectedItem is DownloadItem itm)
        {
            var win = new DownloadDetailWindow(itm) { Owner = this };
            win.ShowDialog();
        }
    }

    private UpdateInfo? _latestUpdate;

    public void NotifyUpdateAvailable(UpdateInfo info)
    {
        _latestUpdate = info;
        Dispatcher.Invoke(() =>
        {
            TxtUpdateVersion.Text = $"v{info.LatestVersion} " + (Strings.IsTurkish ? "Yayında!" : "Available!");
            BtnUpdateAvailable.Visibility = Visibility.Visible;
        });
    }

    private async void UpdateAvailable_Click(object sender, RoutedEventArgs e)
    {
        if (_latestUpdate == null) return;
        bool isTr = Strings.IsTurkish;
        var notes = string.IsNullOrWhiteSpace(_latestUpdate.ReleaseNotes) ? "" :
            (_latestUpdate.ReleaseNotes.Length > 250 ? _latestUpdate.ReleaseNotes[..250] + "..." : _latestUpdate.ReleaseNotes);
        var msg = isTr
            ? $"FM Download Manager için yeni bir güncelleme mevcut!\n\n" +
              $"Kurulu Sürüm: v{UpdateChecker.CurrentVersion}\n" +
              $"Yeni Sürüm: v{_latestUpdate.LatestVersion}\n\n" +
              (notes.Length > 0 ? $"Yenilikler:\n{notes}\n\n" : "") +
              "Güncellemeyi hemen indirip kurmak istiyor musunuz?"
            : $"A new update is available for FM Download Manager!\n\n" +
              $"Installed Version: v{UpdateChecker.CurrentVersion}\n" +
              $"Latest Version: v{_latestUpdate.LatestVersion}\n\n" +
              (notes.Length > 0 ? $"What's new:\n{notes}\n\n" : "") +
              "Would you like to download and install the update now?";
        var res = DarkMessageBox.Show(this, msg, Strings.AppTitle, MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (res == MessageBoxResult.Yes)
        {
            if (!string.IsNullOrEmpty(_latestUpdate.DownloadUrl))
            {
                try
                {
                    TxtUpdateVersion.Text = isTr ? "İndiriliyor..." : "Downloading...";
                    BtnUpdateAvailable.IsEnabled = false;
                    var tempFile = Path.Combine(Path.GetTempPath(), $"FMDownloadManager-Setup-{_latestUpdate.LatestVersion}.exe");
                    using (var http = new System.Net.Http.HttpClient())
                    using (var stream = await http.GetStreamAsync(_latestUpdate.DownloadUrl))
                    using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await stream.CopyToAsync(fs);
                    }

                    Process.Start(new ProcessStartInfo(tempFile) { UseShellExecute = true });
                    Application.Current.Shutdown();
                    return;
                }
                catch
                {
                    BtnUpdateAvailable.IsEnabled = true;
                    TxtUpdateVersion.Text = $"v{_latestUpdate.LatestVersion} " + (isTr ? "Yayında!" : "Available!");
                }
            }
            try { Process.Start(new ProcessStartInfo(_latestUpdate.ReleaseUrl) { UseShellExecute = true }); } catch { }
        }
    }

    #region Drag and Drop
    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (IsSupportedDropData(e.Data))
        {
            e.Effects = DragDropEffects.Copy;
            DragDropOverlay.Visibility = Visibility.Visible;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (IsSupportedDropData(e.Data))
        {
            e.Effects = DragDropEffects.Copy;
            if (DragDropOverlay.Visibility != Visibility.Visible)
                DragDropOverlay.Visibility = Visibility.Visible;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
            DragDropOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        DragDropOverlay.Visibility = Visibility.Collapsed;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        DragDropOverlay.Visibility = Visibility.Collapsed;
        try
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    foreach (var file in files)
                    {
                        ProcessDroppedFile(file);
                    }
                    return;
                }
            }

            if (e.Data.GetDataPresent(DataFormats.UnicodeText))
            {
                var text = e.Data.GetData(DataFormats.UnicodeText) as string;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    ProcessDroppedText(text);
                    return;
                }
            }

            if (e.Data.GetDataPresent(DataFormats.Text))
            {
                var text = e.Data.GetData(DataFormats.Text) as string;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    ProcessDroppedText(text);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error processing dropped item: {ex.Message}");
        }
    }

    private static bool IsSupportedDropData(IDataObject data)
    {
        return data.GetDataPresent(DataFormats.FileDrop) ||
               data.GetDataPresent(DataFormats.UnicodeText) ||
               data.GetDataPresent(DataFormats.Text) ||
               data.GetDataPresent("UniformResourceLocator");
    }

    private void ProcessDroppedFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext == ".torrent")
        {
            _manager.Add(new DownloadRequest { Url = filePath, Kind = DownloadKind.Torrent });
            StatusText.Text = Strings.IsTurkish ? Strings.TorrentAdded : "Torrent download added.";
            return;
        }

        if (ext == ".url")
        {
            try
            {
                var lines = File.ReadAllLines(filePath);
                foreach (var line in lines)
                {
                    if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    {
                        string targetUrl = line.Substring(4).Trim();
                        ProcessDroppedUrl(targetUrl);
                        return;
                    }
                }
            }
            catch { }
        }

        if (ext == ".txt")
        {
            try
            {
                var lines = File.ReadAllLines(filePath);
                int count = 0;
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#")) continue;
                    ProcessDroppedUrl(trimmed, showDialogForVideo: false);
                    count++;
                }
                if (count > 0)
                {
                    StatusText.Text = Strings.IsTurkish ? $"{count} bağlantı eklendi." : $"{count} link(s) added.";
                }
                return;
            }
            catch { }
        }
    }

    private void ProcessDroppedText(string text)
    {
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 1)
        {
            ProcessDroppedUrl(lines[0].Trim(), showDialogForVideo: true);
        }
        else
        {
            int count = 0;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;
                ProcessDroppedUrl(trimmed, showDialogForVideo: false);
                count++;
            }
            if (count > 0)
            {
                StatusText.Text = Strings.IsTurkish ? $"{count} bağlantı eklendi." : $"{count} link(s) added.";
            }
        }
    }

    private void ProcessDroppedUrl(string url, bool showDialogForVideo = true)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        bool isTr = Strings.IsTurkish;
        if (url.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase) || url.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
        {
            _manager.Add(new DownloadRequest { Url = url, Kind = DownloadKind.Torrent });
            StatusText.Text = isTr ? Strings.TorrentAdded : "Torrent download added.";
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            return;
        }

        if (VideoDownloader.IsVideoUrl(url))
        {
            if (showDialogForVideo)
            {
                var qDlg = new VideoQualityDialog(url) { Owner = this };
                if (qDlg.ShowDialog() == true)
                {
                    _manager.Add(new DownloadRequest
                    {
                        Url = !string.IsNullOrWhiteSpace(qDlg.ResolvedUrl) ? qDlg.ResolvedUrl : url,
                        FileName = !string.IsNullOrWhiteSpace(qDlg.DetectedTitle) ? qDlg.DetectedTitle : null,
                        Referer = !string.IsNullOrWhiteSpace(qDlg.ResolvedUrl) && qDlg.ResolvedUrl != url ? url : null,
                        Kind = DownloadKind.Video,
                        CustomFormat = qDlg.SelectedFormat,
                        Container = qDlg.AudioOnly ? "mp3" : "mp4",
                        AudioOnly = qDlg.AudioOnly,
                        WriteSubtitles = qDlg.DownloadSubtitles,
                        SubtitleLangs = qDlg.SubtitleLangs,
                        SubtitleUrl = qDlg.SelectedSubtitleUrl
                    });
                    StatusText.Text = isTr ? (qDlg.AudioOnly ? "Ses indirmesi eklendi." : "Video indirmesi eklendi.") : (qDlg.AudioOnly ? "Audio download added." : "Video download added.");
                }
            }
            else
            {
                _manager.Add(new DownloadRequest
                {
                    Url = url,
                    Kind = DownloadKind.Video,
                    WriteSubtitles = true,
                    SubtitleLangs = "tr,en"
                });
            }
            return;
        }

        _manager.Add(new DownloadRequest { Url = url, Kind = DownloadKind.File });
        StatusText.Text = isTr ? "İndirme eklendi." : "Download added.";
    }
    #endregion
}
