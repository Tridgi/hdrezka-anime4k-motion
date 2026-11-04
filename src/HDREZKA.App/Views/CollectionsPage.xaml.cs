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

    private ToggleButton BuildFolderCard(CollectionSimple col)
    {
        var width = PosterMetrics.Base;
        var posterHeight = PosterMetrics.FolderHeight;

        var posterBorder = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Width = width,
            Height = posterHeight,
            Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry
            {
                Rect = new Windows.Foundation.Rect(0, 0, width, posterHeight),
            },
        };

        if (!string.IsNullOrEmpty(col.Poster) && Uri.TryCreate(col.Poster, UriKind.Absolute, out var uri))
        {
            posterBorder.Child = new Image
            {
                Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri),
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
            };
        }
        else
        {
            // Text-only folders: centered folder glyph instead of an empty box.
            posterBorder.Child = new FontIcon
            {
                Glyph = "\uE8B7",
                FontSize = 40,
                Style = Application.Current.Resources["StarIdleIcon"] as Style,
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
            };
        }

        var title = new TextBlock
        {
            Text = col.Title,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(2, 6, 2, 0),
            Style = Application.Current.Resources["PrimaryText"] as Style,
        };

        var card = new StackPanel { Width = width };
        card.Children.Add(posterBorder);
        card.Children.Add(title);
        if (col.Count > 0)
        {
            card.Children.Add(new TextBlock
            {
                Text = col.Count.ToString(),
                FontSize = 11,
                Margin = new Thickness(2, 2, 2, 0),
                Style = Application.Current.Resources["SecondaryText"] as Style,
            });
        }

        var button = new ToggleButton
        {
            Content = card,
            Tag = col.Id,
            MinWidth = 40,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(2),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        button.Click += CollectionButton_Click;
        return button;
    }

    private void CollectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not string collectionId) return;
        if (collectionId == _currentCollectionId) return;

        foreach (var child in CollectionsPanel.Children)
        {
            if (child is ToggleButton btn && btn != button)
            {
                btn.IsChecked = false;
            }
        }

        button.IsChecked = true;
        SelectCollection(collectionId);
    }

    private void SelectCollection(string collectionId)
    {
        _currentCollectionId = collectionId;
        _page = 1;
        _items.Clear();
        ItemsGrid.ItemsSource = null;
        _hasMore = true;
        SubtitleText.Visibility = Visibility.Collapsed;
        ItemsScroll.ScrollToVerticalOffset(0);
        ShowState(true, Loc.Get("Common.Loading"));
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_loading || string.IsNullOrEmpty(_currentCollectionId)) return;
        _loading = true;
        MoreRing.IsActive = true;
        MorePanel.Visibility = Visibility.Visible;
        MoreButton.IsEnabled = false;

        try
        {
            var (title, items) = await RezkaService.Instance.Client.GetCollectionsListAsync(_currentCollectionId, _page);
            _items.AddRange(items);
            ItemsGrid.ItemsSource = _items.ToList();
            _hasMore = items.Count > 0;
            Log.Info($"[Collections] items: id={_currentCollectionId} page={_page} got={items.Count} total={_items.Count}");

            if (!string.IsNullOrWhiteSpace(title))
            {
                SubtitleText.Text = title;
                SubtitleText.Visibility = Visibility.Visible;
            }

            if (_items.Count == 0)
            {
                ShowState(true, Loc.Get("Common.Empty"));
            }
            else
            {
                ShowState(false, "");
            }
        }
        catch (RezkaException ex)
        {
            Log.Info($"[Collections] items failed: id={_currentCollectionId} page={_page} {ex.Message}");

            if (!_mirrorRecovered && ex.Kind is RezkaError.Network or RezkaError.AccessDenied or RezkaError.MirrorBanned)
            {
                _mirrorRecovered = true;
                if (await MirrorRecovery.TryRecoverAsync())
                {
                    DispatcherQueue.TryEnqueue(() => _ = LoadAsync());
                    return;
                }
            }

            ShowState(true, RezkaService.Instance.ErrorText(ex), showRetry: true);
        }
        finally
        {
            _loading = false;
            MoreRing.IsActive = false;
            MoreButton.IsEnabled = true;
            MorePanel.Visibility = _hasMore && _items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void ShowState(bool visible, string text, bool showRetry = false)
    {
        StatePanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        LoadingRing.IsActive = visible && !showRetry;
        StateText.Text = text;
        RetryButton.Visibility = showRetry ? Visibility.Visible : Visibility.Collapsed;
        if (showRetry && StatePanel.Visibility == Visibility.Collapsed)
        {
            StatePanel.Visibility = Visibility.Visible;
        }

        ItemsGrid.Visibility = _items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        _page++;
        _ = LoadAsync();
    }

    private void ItemsScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate || !_hasMore || _loading || _items.Count == 0) return;

        var verticalOffset = ItemsScroll.VerticalOffset;
        var max = ItemsScroll.ScrollableHeight;
        if (max > 0 && verticalOffset / max >= 0.92)
        {
            _page++;
            _ = LoadAsync();
        }
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_collectionsLoaded || CollectionsPanel.Children.Count == 0)
        {
            // Collections themselves are missing: retry them, not the items.
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