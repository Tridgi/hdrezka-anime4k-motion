using HDREZKA.App.Services;
using HDREZKA.Core.Api;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media;

namespace HDREZKA.App.Views;

public sealed partial class CollectionsPage : Page
{
    private readonly List<MovieSimple> _items = new();
    private readonly List<CollectionSimple> _collections = new();
    private string _currentCollectionId = "";
    private int _page = 1;
    private bool _hasMore = true;
    private bool _loading;
    private bool _collectionsLoaded;
    private bool _mirrorRecovered;
    private bool _collectionViewActive;
    private long _requestVersion;
    private long _activeRequestVersion;

    public CollectionsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        SettingsService.Instance.PosterSizeChanged += OnPosterSizeChanged;
        ApplyLocalization();

        // Do not open the first folder automatically: it can be stale,
        // removed or blocked and then the page appears completely empty.
        if (!_collectionsLoaded) LoadCollections();
        else if (_collectionViewActive && !string.IsNullOrEmpty(_currentCollectionId))
            SetView(selected: true);
        else SetView(selected: false);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        SettingsService.Instance.PosterSizeChanged -= OnPosterSizeChanged;
    }

    private void OnPosterSizeChanged() => DispatcherQueue.TryEnqueue(RebuildFolderCards);

    private void OnLanguageChanged() => DispatcherQueue.TryEnqueue(ApplyLocalization);

    private void ApplyLocalization()
    {
        TitleText.Text = Loc.Get("Nav.Collections");
        BrowseHint.Text = Loc.Get("Collections.BrowseHint");
        BrowseSearchLabel.Text = Loc.Get("Collections.Search");
        BrowseSearchBox.PlaceholderText = Loc.Get("Collections.SearchPlaceholder");
        BackToCollectionsText.Text = Loc.Get("Collections.Back");
        DetailsBackButton.Content = Loc.Get("Collections.Back");
        MoreButton.Content = Loc.Get("Common.LoadMore");
        RetryButton.Content = Loc.Get("Common.Retry");
        BrowseRetryButton.Content = Loc.Get("Common.Retry");
        if (_collectionViewActive) UpdateSelectionHeading();
    }

    private void SetView(bool selected)
    {
        _collectionViewActive = selected;
        BrowseScroll.Visibility = selected ? Visibility.Collapsed : Visibility.Visible;
        CollectionDetailsPanel.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetBrowseState(bool visible, string text = "",
        bool loading = false, bool retry = false)
    {
        BrowseStatePanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        BrowseStateText.Text = text;
        BrowseRing.IsActive = loading;
        BrowseRing.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        BrowseRetryButton.Visibility = retry ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RebuildFolderCards()
    {
        FoldersGrid.Items.Clear();
        if (!_collectionsLoaded) return;
        var query = (BrowseSearchBox.Text ?? "").Trim();
        var matches = _collections.Where(col =>
            query.Length == 0 || col.Title.Contains(query,
                StringComparison.CurrentCultureIgnoreCase)).ToList();
        foreach (var collection in matches)
            FoldersGrid.Items.Add(BuildFolderCard(collection));

        // Search is local: even when there are no results or an individual
        // folder is unavailable, the user can change the query immediately.
        if (matches.Count == 0)
            SetBrowseState(true, _collections.Count == 0
                ? Loc.Get("Collections.NoFolders")
                : Loc.Get("Collections.NoSearchResults"));
        else
            SetBrowseState(false);
    }

    private void BrowseSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_collectionsLoaded) RebuildFolderCards();
    }

    private async void LoadCollections()
    {
        SetView(selected: false);
        SetBrowseState(true, Loc.Get("Common.Loading"), loading: true);
        try
        {
            var collections = await RezkaService.Instance.Client.GetAllCollectionsAsync();
            _collections.Clear();
            _collections.AddRange(collections);
            _collectionsLoaded = true;
            Log.Info($"[Collections] available folders={collections.Count}");
            RebuildFolderCards();
        }
        catch (RezkaException ex)
        {
            Log.Error("[Collections] Folder list failed", ex);
            if (!_mirrorRecovered &&
                ex.Kind is RezkaError.Network or RezkaError.AccessDenied or RezkaError.MirrorBanned)
            {
                _mirrorRecovered = true;
                if (await MirrorRecovery.TryRecoverAsync())
                {
                    DispatcherQueue.TryEnqueue(LoadCollections);
                    return;
                }
            }
            SetBrowseState(true, RezkaService.Instance.ErrorText(ex), retry: true);
        }
        catch (Exception ex)
        {
            Log.Error("[Collections] Folder list failed", ex);
            SetBrowseState(true, Loc.Get("Error.Network"), retry: true);
        }
    }

    private void BrowseRetryButton_Click(object sender, RoutedEventArgs e) => LoadCollections();

    private void BackToCollectionsButton_Click(object sender, RoutedEventArgs e)
        => ReturnToBrowse();

    private void ReturnToBrowse(string? notice = null)
    {
        _requestVersion++;
        _currentCollectionId = "";
        _loading = false;
        _page = 1;
        _items.Clear();
        ItemsGrid.ItemsSource = null;
        MorePanel.Visibility = Visibility.Collapsed;
        SetView(selected: false);
        BrowseScroll.ChangeView(null, 0, null, true);
        if (notice != null)
            SetBrowseState(true, notice);
        else
            RebuildFolderCards();
    }

    private void UpdateSelectionHeading()
    {
        var selected = _collections.FirstOrDefault(c => c.Id == _currentCollectionId);
        SelectedCollectionTitle.Text = selected?.Title ?? Loc.Get("Nav.Collections");
        SelectedCollectionCount.Text = selected != null && selected.Count > 0
            ? Loc.Get("Collections.ItemsCount", selected.Count)
            : Loc.Get("Collections.SelectedHint");
    }

    private Button BuildFolderCard(CollectionSimple col)
    {
        var width = PosterMetrics.Base + 10;
        var posterHeight = PosterMetrics.FolderHeight;
        var posterBorder = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 47, 53, 59)),
            Width = width, Height = posterHeight,
            Clip = new RectangleGeometry
            {
                Rect = new Windows.Foundation.Rect(0, 0, width, posterHeight)
            }
        };
        if (!string.IsNullOrWhiteSpace(col.Poster) &&
            Uri.TryCreate(col.Poster, UriKind.Absolute, out var uri))
        {
            posterBorder.Child = new Image
            {
                Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri),
                Stretch = Stretch.UniformToFill
            };
        }
        else
        {
            posterBorder.Child = new FontIcon
            {
                Glyph = "\uE8B7", FontSize = 32,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        var details = new StackPanel { Spacing = 4, MinHeight = 56 };
        details.Children.Add(new TextBlock
        {
            Text = col.Title, FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (col.Count > 0)
            details.Children.Add(new TextBlock
            {
                Text = Loc.Get("Collections.ItemsCount", col.Count),
                FontSize = 11,
                Foreground = ActualTheme == ElementTheme.Light
                    ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 91, 95, 104))
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 186, 196))
            });

        var card = new StackPanel { Width = width, Spacing = 8 };
        card.Children.Add(posterBorder);
        card.Children.Add(details);
        var button = new Button
        {
            Content = card, Tag = col.Id,
            Padding = new Thickness(8),
            MinWidth = 0, CornerRadius = new CornerRadius(12),
            Background = ActualTheme == ElementTheme.Light
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 246, 248))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 47, 49, 53)),
            BorderThickness = new Thickness(1),
            BorderBrush = ActualTheme == ElementTheme.Light
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 230, 235))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 66, 70, 76))
        };
        ToolTipService.SetToolTip(button, col.Title);
        button.Click += CollectionButton_Click;
        return button;
    }

    private void CollectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string collectionId }) return;
        SelectCollection(collectionId);
    }

    private void SelectCollection(string collectionId)
    {
        _requestVersion++;
        _currentCollectionId = collectionId;
        _loading = false;
        _page = 1;
        _items.Clear();
        ItemsGrid.ItemsSource = null;
        _hasMore = true;
        SetView(selected: true);
        UpdateSelectionHeading();
        ItemsScroll.ChangeView(null, 0, null, true);
        ShowState(true, Loc.Get("Common.Loading"));
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (string.IsNullOrEmpty(_currentCollectionId) || !_collectionViewActive)
            return;
        if (_loading && _activeRequestVersion == _requestVersion) return;

        var version = _requestVersion;
        var collectionId = _currentCollectionId;
        var page = _page;
        _activeRequestVersion = version;
        _loading = true;
        MoreRing.IsActive = true;
        MorePanel.Visibility = Visibility.Collapsed;
        MoreButton.IsEnabled = false;

        bool IsCurrent() => version == _requestVersion &&
            _collectionViewActive && _currentCollectionId == collectionId;
        try
        {
            var (title, items) = await RezkaService.Instance.Client
                .GetCollectionsListAsync(collectionId, page);
            if (!IsCurrent()) return;
            _items.AddRange(items);
            ItemsGrid.ItemsSource = _items.ToList();
            _hasMore = items.Count > 0;

            if (!string.IsNullOrWhiteSpace(title))
                SelectedCollectionTitle.Text = title;
            if (_items.Count == 0)
                ShowState(true, Loc.Get("Collections.EmptyFolder"));
            else
                ShowState(false, "");
            Log.Info($"[Collections] id={collectionId} page={page} fetched={items.Count}");
        }
        catch (RezkaException ex)
        {
            if (!IsCurrent()) return;
            Log.Error($"[Collections] Folder {collectionId} loading failed", ex);

            if (ex.Kind == RezkaError.NotFound)
            {
                // An unavailable folder should never strand the user on
                // an empty full-height detail page.
                ReturnToBrowse(Loc.Get("Collections.Unavailable"));
                return;
            }
            if (!_mirrorRecovered &&
                ex.Kind is RezkaError.Network or RezkaError.AccessDenied or RezkaError.MirrorBanned)
            {
                _mirrorRecovered = true;
                if (await MirrorRecovery.TryRecoverAsync())
                {
                    if (IsCurrent())
                        DispatcherQueue.TryEnqueue(() => _ = LoadAsync());
                    return;
                }
            }
            if (IsCurrent())
                ShowState(true, RezkaService.Instance.ErrorText(ex), showRetry: true);
        }
        catch (Exception ex)
        {
            if (!IsCurrent()) return;
            Log.Error("[Collections] Folder loading failed", ex);
            ShowState(true, Loc.Get("Error.Network"), showRetry: true);
        }
        finally
        {
            if (IsCurrent())
            {
                _loading = false;
                MoreRing.IsActive = false;
                MoreButton.IsEnabled = true;
                MorePanel.Visibility = _hasMore && _items.Count > 0
                    ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private void ShowState(bool visible, string text, bool showRetry = false)
    {
        StatePanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        LoadingRing.IsActive = visible && !showRetry && text == Loc.Get("Common.Loading");
        LoadingRing.Visibility = LoadingRing.IsActive
            ? Visibility.Visible : Visibility.Collapsed;
        StateText.Text = text;
        RetryButton.Visibility = showRetry ? Visibility.Visible : Visibility.Collapsed;
        DetailsBackButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ItemsGrid.Visibility = _items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || !_hasMore || !_collectionViewActive) return;
        _page++;
        _ = LoadAsync();
    }

    private void ItemsScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate || !_collectionViewActive || !_hasMore ||
            _loading || _items.Count == 0) return;
        var max = ItemsScroll.ScrollableHeight;
        if (max > 0 && ItemsScroll.VerticalOffset / max >= .92)
        {
            _page++;
            _ = LoadAsync();
        }
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_collectionViewActive || string.IsNullOrWhiteSpace(_currentCollectionId))
        {
            LoadCollections();
            return;
        }
        _page = 1;
        _items.Clear();
        ItemsGrid.ItemsSource = null;
        _hasMore = true;
        ShowState(true, Loc.Get("Common.Loading"));
        _ = LoadAsync();
    }
}
