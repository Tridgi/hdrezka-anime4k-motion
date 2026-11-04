using HDREZKA.App.Services;
using HDREZKA.App.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace HDREZKA.App;

public sealed partial class MainWindow : Window
{
    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value;
            Bindings.Update();
        }
    }

    private string _searchText = "";
    private bool _refreshingTitleBar;

    public MainWindow()
    {
        InitializeComponent();

        Title = "HDREZKA Anime4K Motion v2";
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        try { AppWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Tall; }
        catch (Exception ex) { App.TryLog(ex); }

        try
        {
            AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico"));
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }

        ThemeHelper.SetRoot(RootGrid);
        RootGrid.ActualThemeChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, RefreshCaptionButtons);
        };
        try
        {
            SetTitleBar(CustomTitleBar);
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }
        RootGrid.SizeChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, RefreshTitleBarLayout);
            UpdateCenteredModalLayout();
        };
        CustomTitleBar.Loaded += (_, _) => RefreshTitleBarLayout();
        CustomTitleBar.SizeChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, RefreshTitleBarLayout);
        AppWindow.Changed += (_, e) =>
        {
            if (e.DidPresenterChange || e.DidSizeChange)
            {
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, RefreshTitleBarLayout);
            }
        };
        RefreshTitleBarLayout();
        RefreshCaptionButtons();
        // Catch mouse Back/Forward side buttons even when a nested control
        // marks PointerPressed as handled. This follows Frame's history.
        RootGrid.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler(RootGrid_PointerPressed), true);
        Nav.SetFrame(ContentFrame);
        ContentFrame.Navigated += ContentFrame_Navigated;
        LocalizationService.Instance.LanguageChanged += ApplyLocalization;
        RezkaService.Instance.AuthStateChanged += () => DispatcherQueue.TryEnqueue(ApplyAccountState);
        TrackedSeriesService.UpdatesChanged += () => DispatcherQueue.TryEnqueue(UpdateBadge);
        DownloadService.Instance.Changed += () => DispatcherQueue.TryEnqueue(UpdateDownloadsButton);
        ApplyLocalization();
        ApplyAccountState();
        UpdateBadge();
        UpdateDownloadsButton();

        ContentFrame.Navigate(typeof(HomePage));
        NavView.SelectedItem = NavHome;
    }

    private TaskCompletionSource<bool>? _centeredModalCompletion;

    /// <summary>
    /// A modal overlay hosted in RootGrid, which spans the complete application
    /// window. The overlay remains centred even with the navigation pane open.
    /// Avoid ContentDialog.Popup here: it does not control the popup's geometry.
    /// </summary>
    private double _centeredModalPreferredWidth = 560;

    public Task ShowCenteredModalAsync(string title, FrameworkElement body, double preferredWidth = 560)
    {
        if (_centeredModalCompletion is not null)
            return Task.CompletedTask;

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _centeredModalCompletion = completion;
        CenteredModalTitle.Text = title;
        _centeredModalPreferredWidth = Math.Clamp(preferredWidth, 360, 850);
        CenteredModalBody.Content = body;
        UpdateCenteredModalLayout();

        // Keep keyboard navigation and clicks within the modal.
        NavView.IsEnabled = false;
        TitleBarActions.IsHitTestVisible = false;
        DownloadsFab.IsEnabled = false;
        CenteredModalLayer.Visibility = Visibility.Visible;
        DispatcherQueue.TryEnqueue(() =>
            CenteredModalCloseButton.Focus(FocusState.Programmatic));
        return completion.Task;
    }

    private void UpdateCenteredModalLayout()
    {
        if (_centeredModalCompletion is null) return;
        var width = RootGrid.ActualWidth;
        var height = RootGrid.ActualHeight;
        if (width <= 0 || height <= 0) return;

        CenteredModalCard.Width = Math.Max(240, Math.Min(_centeredModalPreferredWidth, width - 40));
        CenteredModalCard.MaxHeight = Math.Max(180, height - 36);
        if (CenteredModalBody.Content is ScrollViewer scroll)
            scroll.MaxHeight = Math.Max(100, height - 190);
    }

    private void CloseCenteredModal()
    {
        var completion = _centeredModalCompletion;
        if (completion is null) return;
        _centeredModalCompletion = null;
        CenteredModalLayer.Visibility = Visibility.Collapsed;
        CenteredModalBody.Content = null;
        NavView.IsEnabled = true;
        TitleBarActions.IsHitTestVisible = true;
        DownloadsFab.IsEnabled = true;
        completion.TrySetResult(true);
    }

    private void CenteredModalCloseButton_Click(object sender, RoutedEventArgs e)
        => CloseCenteredModal();

    private void CenteredModalLayer_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        e.Handled = true;
        CloseCenteredModal();
    }

    public bool IsTheaterMode { get; private set; }

    public void SetTheaterMode(bool on)
    {
        if (IsTheaterMode == on) return;
        IsTheaterMode = on;
        NavView.IsPaneVisible = !on;
        // PlayerPage has its own back button, so title-bar navigation stays hidden in theater mode.
        TitleBarActions.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        NavView.IsPaneToggleButtonVisible = false;
        NavView.IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
        RefreshTitleBarLayout();
        RefreshCaptionButtons();

        try
        {
            CustomTitleBar.Background = on
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black)
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Keeps the custom title bar aligned with system caption buttons:
    /// reserves insets, matches the system height and re-applies the drag
    /// region. Must run on launch, SizeChanged and every presenter change,
    /// because SetPresenter resets drag rectangles.
    /// </summary>

    private void RefreshTitleBarLayout()
    {
        if (_refreshingTitleBar) return;
        _refreshingTitleBar = true;
        try
        {
            var tb = AppWindow.TitleBar;
            if (tb is null) return;

            // WinUI measures in DIPs; AppWindow.TitleBar insets are physical pixels.
            var scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
            if (scale <= 0) scale = 1.0;
            CustomTitleBar.Height = tb.Height / scale;
            LeftInsetColumn.Width = new GridLength(Math.Max(0, tb.LeftInset / scale));
            RightInsetColumn.Width = new GridLength(Math.Max(0, tb.RightInset / scale));

            // Only the region to the right of the two buttons is draggable.
            // Dragging the entire title bar would swallow clicks on Back/Pane.
            if (TitleBarDragRegion.ActualWidth > 0 && CustomTitleBar.ActualHeight > 0)
            {
                var start = TitleBarDragRegion.TransformToVisual(CustomTitleBar)
                    .TransformPoint(new Windows.Foundation.Point(0, 0));
                var x = (int)Math.Round(start.X * scale);
                var width = (int)Math.Round(TitleBarDragRegion.ActualWidth * scale);
                var height = (int)Math.Round(CustomTitleBar.ActualHeight * scale);
                if (width > 0 && height > 0)
                {
                    tb.SetDragRectangles(new[] { new Windows.Graphics.RectInt32(x, 0, width, height) });
                }
            }

            // Mark both XAML buttons as clickable non-client pass-through regions,
            // including at Windows scaling of 125-175%.
            var clickable = TitleBarActions.Visibility == Visibility.Visible
                ? new[] { AppBackButton, AppPaneButton }
                : Array.Empty<Button>();
            var regions = new List<Windows.Graphics.RectInt32>();
            foreach (var button in clickable)
            {
                if (button.ActualWidth <= 0 || button.ActualHeight <= 0) continue;
                var bounds = button.TransformToVisual(CustomTitleBar).TransformBounds(
                    new Windows.Foundation.Rect(0, 0, button.ActualWidth, button.ActualHeight));
                regions.Add(new Windows.Graphics.RectInt32(
                    (int)Math.Round(bounds.X * scale),
                    (int)Math.Round(bounds.Y * scale),
                    (int)Math.Round(bounds.Width * scale),
                    (int)Math.Round(bounds.Height * scale)));
            }
            Microsoft.UI.Input.InputNonClientPointerSource.GetForWindowId(AppWindow.Id)
                .SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.Passthrough, regions.ToArray());
        }
        catch (Exception ex) { App.TryLog(ex); }
        finally { _refreshingTitleBar = false; }
    }

    /// <summary>
    /// Single place for caption button colors. Theater mode forces the black
    /// scheme; otherwise colors follow the effective theme — the system
    /// defaults leave _ □ X nearly invisible on the light theme.
    /// </summary>
    private void RefreshCaptionButtons()
    {
        try
        {
            var tb = AppWindow.TitleBar;
            if (IsTheaterMode)
            {
                tb.BackgroundColor = Microsoft.UI.Colors.Black;
                tb.InactiveBackgroundColor = Microsoft.UI.Colors.Black;
                tb.ForegroundColor = Microsoft.UI.Colors.White;
                tb.InactiveForegroundColor = Microsoft.UI.Colors.Gray;
                tb.ButtonBackgroundColor = Microsoft.UI.Colors.Black;
                tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Black;
                tb.ButtonForegroundColor = Microsoft.UI.Colors.White;
                tb.ButtonInactiveForegroundColor = Microsoft.UI.Colors.Gray;
                tb.ButtonHoverBackgroundColor = Microsoft.UI.Colors.DimGray;
                tb.ButtonHoverForegroundColor = Microsoft.UI.Colors.White;
                return;
            }

            var dark = RootGrid.ActualTheme == ElementTheme.Dark;
            var fg = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
            var fgDim = dark ? Microsoft.UI.Colors.Gray : Microsoft.UI.Colors.DimGray;
            tb.BackgroundColor = null;
            tb.InactiveBackgroundColor = null;
            tb.ForegroundColor = null;
            tb.InactiveForegroundColor = null;
            tb.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            tb.ButtonForegroundColor = fg;
            tb.ButtonInactiveForegroundColor = fgDim;
            tb.ButtonHoverForegroundColor = fg;
            tb.ButtonPressedForegroundColor = fg;
            tb.ButtonHoverBackgroundColor = dark
                ? Microsoft.UI.Colors.DimGray
                : Windows.UI.Color.FromArgb(0xFF, 0xE5, 0xE5, 0xE5);
            tb.ButtonPressedBackgroundColor = dark
                ? Microsoft.UI.Colors.Gray
                : Windows.UI.Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC);
        }
        catch
        {
            // title bar theming is best effort
        }
    }

    private void ApplyLocalization()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                NavHome.Content = Loc.Get("Nav.Home");
                NavCatalog.Content = Loc.Get("Nav.Catalog");
                NavBookmarks.Content = Loc.Get("Nav.Bookmarks");
                NavContinue.Content = Loc.Get("Nav.Continue");
                NavCollections.Content = Loc.Get("Nav.Collections");
                NavAppearance.Content = Loc.Get("Nav.Appearance");
                NavSupport.Content = Loc.Get("Nav.Support");
                NavUpdates.Content = Loc.Get("Nav.Updates");
                NavAccount.Content = Loc.Get("Nav.Account");
                NavSettings.Content = Loc.Get("Nav.Settings");
                SearchBox.PlaceholderText = Loc.Get("Search.Placeholder");
                ApplyAccountState();
            });
        }

    private void ApplyAccountState()
    {
        NavAccount.Content = RezkaService.Instance.IsLoggedIn
            ? Loc.Get("Nav.Account")
            : Loc.Get("Common.Login");
    }

    private void UpdateBadge()
    {
        try
        {
            var count = TrackedSeriesService.UnreadCount;
            UpdatesBadge.Value = count;
            UpdatesBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch
        {
        }
    }

    private void UpdateDownloadsButton()
    {
        try
        {
            var active = DownloadService.Instance.Jobs
                .Where(j => j.State is DownloadState.Queued or DownloadState.Resolving or DownloadState.Downloading)
                .ToList();
            DownloadsFab.Visibility = active.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (active.Count == 0) return;
            DownloadsCountText.Text = active.Count.ToString();
            DownloadsRing.Value = active.Average(j => j.State == DownloadState.Downloading ? j.Progress : 0);
            ToolTipService.SetToolTip(DownloadsFab, $"{Loc.Get("Downloads.Title")} ({active.Count})");
        }
        catch
        {
        }
    }

    private async void DownloadsButton_Click(object sender, RoutedEventArgs e)
    {
        await Views.DownloadDialog.ShowActiveAsync(ContentFrame.XamlRoot);
    }

private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            NavView.IsBackEnabled = ContentFrame.CanGoBack;
            AppBackButton.IsEnabled = ContentFrame.CanGoBack;
            SetTheaterMode(e.SourcePageType == typeof(PlayerPage));

            var tag = e.SourcePageType switch
            {
                var t when t == typeof(HomePage) => "Home",
                var t when t == typeof(CatalogPage) => "Catalog",
                var t when t == typeof(BookmarksPage) => "Bookmarks",
                var t when t == typeof(SettingsPage) => "Settings",
                var t when t == typeof(AppearancePage) => "Appearance",
                var t when t == typeof(SupportPage) => "Support",
                var t when t == typeof(ContinueWatchingPage) => "Continue",
                var t when t == typeof(CollectionsPage) => "Collections",
                var t when t == typeof(UpdatesPage) => "Updates",
                var t when t == typeof(AccountPage) => "Account",
                _ => (string?)null,
            };

        if (tag == null)
        {
            NavView.SelectedItem = null;
        }
else
            {
                NavView.SelectedItem = tag switch
                {
                    "Home" => NavHome,
                    "Catalog" => NavCatalog,
                    "Bookmarks" => NavBookmarks,
                    "Settings" => NavSettings,
                    "Appearance" => NavAppearance,
                    "Support" => NavSupport,
                    "Continue" => NavContinue,
                    "Collections" => NavCollections,
                    "Updates" => NavUpdates,
                    "Account" => NavAccount,
                    _ => null,
                };
            }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag) return;

        switch (tag)
        {
            case "Home":
                if (ContentFrame.Content is not HomePage) ContentFrame.Navigate(typeof(HomePage));
                break;
            case "Catalog":
                if (ContentFrame.Content is not CatalogPage) ContentFrame.Navigate(typeof(CatalogPage));
                break;
            case "Bookmarks":
                if (!RezkaService.Instance.IsLoggedIn)
                {
                    _ = ShowLoginDialog();
                    sender.SelectedItem = null;
                    return;
                }
                if (ContentFrame.Content is not BookmarksPage) ContentFrame.Navigate(typeof(BookmarksPage));
                break;
            case "Continue":
                if (!RezkaService.Instance.IsLoggedIn)
                {
                    _ = ShowLoginDialog();
                    sender.SelectedItem = null;
                    return;
                }
                if (ContentFrame.Content is not ContinueWatchingPage) ContentFrame.Navigate(typeof(ContinueWatchingPage));
                break;
            case "Settings":
                if (ContentFrame.Content is not SettingsPage) ContentFrame.Navigate(typeof(SettingsPage));
                break;
            case "Appearance":
                if (ContentFrame.Content is not AppearancePage) ContentFrame.Navigate(typeof(AppearancePage));
                break;
            case "Support":
                if (ContentFrame.Content is not SupportPage) ContentFrame.Navigate(typeof(SupportPage));
                break;
            case "Collections":
                if (ContentFrame.Content is not CollectionsPage) ContentFrame.Navigate(typeof(CollectionsPage));
                break;
            case "Updates":
                if (ContentFrame.Content is not UpdatesPage) ContentFrame.Navigate(typeof(UpdatesPage));
                break;
            case "Account":
                if (RezkaService.Instance.IsLoggedIn)
                {
                    if (ContentFrame.Content is not AccountPage) ContentFrame.Navigate(typeof(AccountPage));
                }
                else
                {
                    sender.SelectedItem = null;
                    _ = ShowLoginDialog();
                }
                break;
        }
    }

    public async Task ShowLoginDialog()
    {
        var dialog = new LoginDialog
        {
            XamlRoot = ContentFrame.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private void GoBack()
    {
        if (ContentFrame.CanGoBack) ContentFrame.GoBack();
    }

    private void GoForward()
    {
        if (ContentFrame.CanGoForward) ContentFrame.GoForward();
    }

    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // XButton1/XButton2 are the two side buttons found on most mice.
        // The modal owns input while displayed.
        if (CenteredModalLayer.Visibility == Visibility.Visible) return;
        var properties = e.GetCurrentPoint(RootGrid).Properties;
        if (properties.IsXButton1Pressed && ContentFrame.CanGoBack)
        {
            GoBack();
            e.Handled = true;
        }
        else if (properties.IsXButton2Pressed && ContentFrame.CanGoForward)
        {
            GoForward();
            e.Handled = true;
        }
    }

    private void AppBackButton_Click(object sender, RoutedEventArgs e) => GoBack();

    private void AppPaneButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsTheaterMode) return;
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void NavView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        => GoBack();

    private void NavView_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        // reserved for adaptive tweaks
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var query = args.QueryText.Trim();
        if (query.Length < 2)
        {
            sender.Text = "";
            return;
        }

        ContentFrame.Navigate(typeof(SearchPage), query);
        sender.Text = "";
    }
}
