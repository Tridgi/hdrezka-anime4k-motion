using HDREZKA.App.Controls;
using HDREZKA.App.Services;
using HDREZKA.Core.Api;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace HDREZKA.App.Views;

public sealed record CatalogLaunch(Section Section, ListFilter Filter);
public sealed record CatalogGenreLaunch(MovieGenre Genre);

public sealed partial class CatalogPage : Page
{
    private static readonly Section[] Sections =
    [
        Section.Films, Section.Series, Section.Cartoons, Section.Show, Section.Anime,
    ];

    private static readonly ListFilter[] Filters =
    [
        ListFilter.Latest, ListFilter.Popular, ListFilter.Soon, ListFilter.WatchingNow,
    ];

    private readonly List<MovieSimple> _items = new();
    private Section _section = Section.Films;
    private ListFilter _filter = ListFilter.Latest;
    private int _page = 1;
    private string? _genrePath;
    private string? _genreName;
    private IReadOnlyList<MovieGenre> _availableGenres = [];
    private long _genreRequestVersion;
    private long _requestVersion;
    private long _activeRequestVersion;
    private bool _hasMore = true;
    private bool _loading;
    private bool _mirrorRecovered;
    private MovieCard? _hoveredCard;
    private string? _hoveredMovieId;
    private CancellationTokenSource? _hoverCts;
    private readonly Dictionary<string, MovieDetailed> _previewCache =
        new(StringComparer.OrdinalIgnoreCase);

    public CatalogPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ApplyLocalization();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

        if (e.Parameter is CatalogGenreLaunch genre)
        {
            _genrePath = genre.Genre.Id;
            _genreName = genre.Genre.Name;
            // Select the matching media section when a film's genre link
            // leads into a different section of the source website.
            foreach (var candidate in Sections)
                if (_genrePath.StartsWith(candidate.ToPath() + "/", StringComparison.OrdinalIgnoreCase))
                {
                    _section = candidate;
                    break;
                }
            _page = 1;
            _items.Clear();
            ItemsGrid.ItemsSource = null;
        }
        else if (e.Parameter is CatalogLaunch launch)
        {
            _genrePath = null;
            _genreName = null;
            _section = launch.Section;
            _filter = launch.Filter;
            // Fresh target state: drop stale items so the new section loads.
            _items.Clear();
            ItemsGrid.ItemsSource = null;
        }

        if (SectionPanel.Children.Count == 0)
        {
            foreach (var section in Sections)
            {
                var tag = section.ToString();
                var button = new ToggleButton
                {
                    Content = Loc.Get("Section." + tag),
                    Tag = tag,
                    MinWidth = 40,
                };
                button.Click += SectionButton_Click;
                SectionPanel.Children.Add(button);
            }
        }

        MarkSection(_section);
        BuildFilters();
        UpdateGenreFilter();
        RebuildGenreBox();
        _ = LoadGenresAsync();
        if (_items.Count == 0)
        {
            Reload();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        HideCatalogPreview();
    }

    private void OnLanguageChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ApplyLocalization();
            BuildFilters();
            MarkSection(_section);
            RebuildGenreBox();
        });
    }

    private void ApplyLocalization()
    {
        TitleText.Text = Loc.Get("Catalog.Title");
        ClearGenreButton.Content = Loc.Get("Catalog.ClearGenre");
        UpdateGenreFilter();
        RebuildGenreBox();
        CatalogPreviewHeader.Text = Loc.Get("Details.AboutMovie");
        MoreButton.Content = Loc.Get("Common.LoadMore");
        RetryButton.Content = Loc.Get("Common.Retry");
        StateText.Text = Loc.Get("Common.Loading");

        var i = 0;
        foreach (var section in Sections)
        {
            if (i < SectionPanel.Children.Count &&
                SectionPanel.Children[i] is ToggleButton button)
            {
                button.Content = Loc.Get("Section." + section);
            }

            i++;
        }
    }

    private void MarkSection(Section section)
    {
        var i = 0;
        foreach (var s in Sections)
        {
            if (SectionPanel.Children[i] is ToggleButton button)
            {
                button.IsChecked = s == section;
            }

            i++;
        }
    }

    private void BuildFilters()
    {
        FilterBox.SelectionChanged -= FilterBox_SelectionChanged;
        FilterBox.Items.Clear();
        foreach (var filter in Filters)
        {
            FilterBox.Items.Add(new ComboBoxItem
            {
                Content = Loc.Get("Filter." + filter),
                Tag = filter,
            });
        }

        FilterBox.SelectedIndex = Array.IndexOf(Filters, _filter);
        FilterBox.SelectionChanged += FilterBox_SelectionChanged;
    }

    private void RebuildGenreBox()
    {
        GenreBox.SelectionChanged -= GenreBox_SelectionChanged;
        GenreBox.Header = Loc.Get("Details.Genre");
        GenreBox.Items.Clear();
        GenreBox.Items.Add(new ComboBoxItem { Content = Loc.Get("Catalog.AllGenres") });
        foreach (var genre in _availableGenres)
            GenreBox.Items.Add(new ComboBoxItem { Content = genre.Name, Tag = genre });

        var selectedIndex = _availableGenres.ToList().FindIndex(g =>
            string.Equals(g.Id, _genrePath, StringComparison.OrdinalIgnoreCase));
        if (selectedIndex < 0 && !string.IsNullOrWhiteSpace(_genrePath))
        {
            // Preserve a genre opened directly from a movie, even when the
            // active mirror omitted it from its main navigation menu.
            GenreBox.Items.Add(new ComboBoxItem
            {
                Content = _genreName ?? _genrePath,
                Tag = new MovieGenre(_genreName ?? _genrePath, _genrePath)
            });
            GenreBox.SelectedIndex = GenreBox.Items.Count - 1;
        }
        else GenreBox.SelectedIndex = selectedIndex + 1;
        GenreBox.SelectionChanged += GenreBox_SelectionChanged;
    }

    private async Task LoadGenresAsync()
    {
        var section = _section;
        var version = ++_genreRequestVersion;
        try
        {
            var genres = await RezkaService.Instance.Client.GetGenreOptionsAsync(section);
            if (version != _genreRequestVersion || section != _section) return;
            _availableGenres = genres;
            Log.Info($"[Catalog] section={section} source genres={genres.Count}");
        }
        catch (Exception ex)
        {
            if (version != _genreRequestVersion || section != _section) return;
            _availableGenres = [];
            Log.Info("[Catalog] Genre menu unavailable: " + ex.Message);
        }
        RebuildGenreBox();
    }

    private void GenreBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GenreBox.SelectedItem is not ComboBoxItem choice) return;
        var selected = choice.Tag as MovieGenre;
        var path = selected?.Id;
        if (string.Equals(_genrePath, path, StringComparison.OrdinalIgnoreCase)) return;
        _genrePath = path;
        _genreName = selected?.Name;
        UpdateGenreFilter();
        Reload();
    }

    private void UpdateGenreFilter()
    {
        if (GenreFilterPanel == null) return;
        GenreFilterPanel.Visibility = string.IsNullOrWhiteSpace(_genrePath)
            ? Visibility.Collapsed : Visibility.Visible;
        GenreFilterText.Text = Loc.Get("Catalog.GenreSelected") + " " + (_genreName ?? "");
    }

    private void ClearGenreButton_Click(object sender, RoutedEventArgs e)
    {
        _genreName = null;
        _genrePath = null;
        UpdateGenreFilter();
        RebuildGenreBox();
        Reload();
    }

    private void SectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not string tag) return;
        if (!Enum.TryParse<Section>(tag, out var section) || section == _section) return;

        _section = section;
        _genrePath = null;
        _genreName = null;
        UpdateGenreFilter();
        MarkSection(section);
        _availableGenres = [];
        RebuildGenreBox();
        _ = LoadGenresAsync();
        Reload();
    }

    private void FilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FilterBox.SelectedItem is ComboBoxItem { Tag: ListFilter filter } && filter != _filter)
        {
            _filter = filter;
            Reload();
        }
    }

    private void Reload()
    {
        // Invalidate an old response when the user changes a genre or filter
        // while a previous request is still in flight.
        _requestVersion++;
        HideCatalogPreview();
        _page = 1;
        _items.Clear();
        ItemsGrid.ItemsSource = null;
        _hasMore = true;
        ShowState(true, Loc.Get("Common.Loading"));
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_loading && _activeRequestVersion == _requestVersion) return;
        var version = _requestVersion;
        var section = _section;
        var filter = _filter;
        var genrePath = _genrePath;
        var page = _page;
        _activeRequestVersion = version;
        _loading = true;
        MoreRing.IsActive = true;
        MorePanel.Visibility = Visibility.Visible;
        MoreButton.IsEnabled = false;
        bool IsCurrent() => version == _requestVersion;

        try
        {
            (string _, IReadOnlyList<MovieSimple> items) result;
            if (genrePath is { Length: > 0 } genre)
            {
                result = genre.StartsWith("id:", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(genre.AsSpan(3), out var genreId)
                    ? await RezkaService.Instance.Client.GetSectionListAsync(section, filter, genreId, page)
                    : await RezkaService.Instance.Client.GetGenreListAsync(genre, page, filter: filter);
            }
            else result = await RezkaService.Instance.Client.GetSectionListAsync(section, filter, 0, page);

            if (!IsCurrent()) return;
            _items.AddRange(result.items);
            ItemsGrid.ItemsSource = _items.ToList();
            _hasMore = result.items.Count > 0;
            ShowState(false, "");
        }
        catch (RezkaException ex)
        {
            if (!IsCurrent()) return;
            if (!_mirrorRecovered && ex.Kind is RezkaError.Network or RezkaError.AccessDenied or RezkaError.MirrorBanned)
            {
                _mirrorRecovered = true;
                if (await MirrorRecovery.TryRecoverAsync())
                {
                    if (IsCurrent()) DispatcherQueue.TryEnqueue(() => _ = LoadAsync());
                    return;
                }
            }

            if (IsCurrent()) ShowState(true, RezkaService.Instance.ErrorText(ex), showRetry: true);
        }
        catch (Exception ex)
        {
            if (!IsCurrent()) return;
            Log.Error("[Catalog] Loading failed", ex);
            ShowState(true, Loc.Get("Error.Network"), showRetry: true);
        }
        finally
        {
            if (IsCurrent())
            {
                _loading = false;
                MoreRing.IsActive = false;
                MoreButton.IsEnabled = true;
                MorePanel.Visibility = _hasMore && _items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
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
        HideCatalogPreview();
        if (e.IsIntermediate || !_hasMore || _loading || _items.Count == 0) return;

        var verticalOffset = ItemsScroll.VerticalOffset;
        var max = ItemsScroll.ScrollableHeight;
        if (max > 0 && verticalOffset / max >= 0.92)
        {
            _page++;
            _ = LoadAsync();
        }
    }

    private void CatalogMovie_HoverChanged(object sender, RecommendationHoverEventArgs e)
    {
        if (sender is not MovieCard card) return;
        if (!e.IsEntering)
        {
            if (ReferenceEquals(_hoveredCard, card))
                HideCatalogPreview();
            return;
        }
        if (e.Movie == null) return;
        HideCatalogPreview();
        _hoveredCard = card;
        _hoveredMovieId = e.Movie.Id;
        _hoverCts = new CancellationTokenSource();
        _ = ShowCatalogPreviewAsync(card, e.Movie, _hoverCts.Token);
    }

    private void HideCatalogPreview()
    {
        try { _hoverCts?.Cancel(); } catch { }
        _hoverCts = null;
        _hoveredCard = null;
        _hoveredMovieId = null;
        CatalogPreviewPanel.Visibility = Visibility.Collapsed;
    }

    private bool CatalogPreviewCurrent(MovieCard card, MovieSimple movie,
        CancellationToken token) =>
        !token.IsCancellationRequested && ReferenceEquals(_hoveredCard, card)
        && _hoveredMovieId == movie.Id && card.Movie?.Id == movie.Id;

    private static SolidColorBrush PreviewColor(byte r, byte g, byte b)
        => new(Windows.UI.Color.FromArgb(255, r, g, b));

    private void StyleCatalogPreviewRatings(float? rezka, float? imdb, float? kp)
    {
        var colorful = SettingsService.Instance.DetailPalette == DetailsPalette.Colorful;
        var light = ActualTheme == ElementTheme.Light;
        var muted = light ? PreviewColor(228, 233, 238) : PreviewColor(49, 54, 61);
        var ink = light ? PreviewColor(34, 38, 43) : PreviewColor(243, 245, 247);
        CatalogPreviewRezkaBadge.Visibility = rezka.HasValue
            ? Visibility.Visible : Visibility.Collapsed;
        CatalogPreviewImdbBadge.Visibility = imdb.HasValue
            ? Visibility.Visible : Visibility.Collapsed;
        CatalogPreviewKpBadge.Visibility = kp.HasValue
            ? Visibility.Visible : Visibility.Collapsed;
        CatalogPreviewRezka.Text = rezka.HasValue ? "R " + rezka.Value.ToString("0.0") : "";
        CatalogPreviewImdb.Text = imdb.HasValue ? "IMDb " + imdb.Value.ToString("0.0") : "";
        CatalogPreviewKp.Text = kp.HasValue ? "КП " + kp.Value.ToString("0.0") : "";
        CatalogPreviewRezkaBadge.Background = rezka.HasValue && colorful
            ? RezkaScoreColors.Gradient(rezka.Value) : muted;
        CatalogPreviewRezka.Foreground = rezka.HasValue && colorful
            ? PreviewColor(255, 255, 255) : ink;
        CatalogPreviewImdbBadge.Background = colorful
            ? (light ? PreviewColor(250, 231, 173) : PreviewColor(91, 78, 39)) : muted;
        CatalogPreviewKpBadge.Background = colorful
            ? (light ? PreviewColor(250, 216, 194) : PreviewColor(96, 63, 47)) : muted;
        CatalogPreviewImdb.Foreground = ink;
        CatalogPreviewKp.Foreground = ink;
    }

    private void FillCatalogPreviewGenres(IReadOnlyList<MovieGenre>? genres)
    {
        CatalogPreviewGenres.Inlines.Clear();
        if (genres is not { Count: > 0 }) return;
        var names = genres.Take(5).Select(x => x.Name).ToArray();
        var colors = GenreColors.Palette(names,
            ActualTheme == ElementTheme.Light,
            SettingsService.Instance.DetailPalette == DetailsPalette.Colorful);
        for (var i = 0; i < names.Length; i++)
        {
            CatalogPreviewGenres.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text = (i == 0 ? "" : "  ·  ") + names[i],
                Foreground = colors[i].Fg,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
        }
    }

    private void SetCatalogPreviewDetails(MovieDetailed details)
    {
        CatalogPreviewTitle.Text = details.Name;
        CatalogPreviewOriginal.Text = details.OriginalName ?? "";
        StyleCatalogPreviewRatings(details.SiteRating?.Value,
            details.ImdbRating?.Value, details.KpRating?.Value);
        CatalogPreviewDescription.Text = details.Description ?? "";
        FillCatalogPreviewGenres(details.Genres);
        CatalogPreviewDirector.Text = details.Producers is { Count: > 0 }
            ? Loc.Get("Details.Director") + ": " +
              string.Join(", ", details.Producers.Take(3).Select(x => x.Name)) : "";
        CatalogPreviewActors.Text = details.Actors is { Count: > 0 }
            ? Loc.Get("Details.Actors") + ": " +
              string.Join(", ", details.Actors.Take(4).Select(x => x.Name)) : "";
    }

    private void PositionCatalogPreview(MovieCard card)
    {
        var availableWidth = CatalogPreviewLayer.ActualWidth;
        var availableHeight = CatalogPreviewLayer.ActualHeight;
        if (availableWidth < 220 || availableHeight < 220)
        {
            CatalogPreviewPanel.Visibility = Visibility.Collapsed;
            return;
        }
        CatalogPreviewPanel.Width = Math.Min(336, availableWidth - 28);
        CatalogPreviewPanel.MaxHeight = availableHeight - 24;
        CatalogPreviewDescription.MaxLines = availableHeight < 540 ? 3 : 5;
        var cardPoint = card.TransformToVisual(CatalogPreviewLayer)
            .TransformPoint(new Windows.Foundation.Point(0, 0));
        CatalogPreviewPanel.Measure(new Windows.Foundation.Size(
            CatalogPreviewPanel.Width, CatalogPreviewPanel.MaxHeight));
        var panelHeight = Math.Min(CatalogPreviewPanel.DesiredSize.Height,
            CatalogPreviewPanel.MaxHeight);
        const double margin = 14;
        var right = cardPoint.X + card.ActualWidth + margin;
        var left = cardPoint.X - CatalogPreviewPanel.Width - margin;
        var x = right + CatalogPreviewPanel.Width <= availableWidth - 12
            ? right : left >= 12 ? left
            : Math.Max(12, (availableWidth - CatalogPreviewPanel.Width) / 2);
        var y = Math.Clamp(cardPoint.Y - 5, 12,
            Math.Max(12, availableHeight - panelHeight - 12));
        Canvas.SetLeft(CatalogPreviewPanel, x);
        Canvas.SetTop(CatalogPreviewPanel, y);
    }

    private async Task ShowCatalogPreviewAsync(MovieCard card, MovieSimple movie,
        CancellationToken token)
    {
        try
        {
            // Cheap immediate preview first. Fetch details only for the card
            // that remains hovered, never for the entire catalog grid.
            await Task.Delay(170, token);
            if (!CatalogPreviewCurrent(card, movie, token)) return;
            CatalogPreviewHeader.Text = Loc.Get("Details.AboutMovie");
            CatalogPreviewTitle.Text = movie.Name ?? "";
            CatalogPreviewOriginal.Text = movie.Details ?? "";
            StyleCatalogPreviewRatings(movie.Rating, null, null);
            CatalogPreviewDescription.Text = "";
            CatalogPreviewGenres.Inlines.Clear();
            CatalogPreviewDirector.Text = "";
            CatalogPreviewActors.Text = "";
            CatalogPreviewPanel.Visibility = Visibility.Visible;
            PositionCatalogPreview(card);
            if (_previewCache.TryGetValue(movie.Id, out var cached))
            {
                SetCatalogPreviewDetails(cached);
                PositionCatalogPreview(card);
                return;
            }
            await Task.Delay(230, token);
            if (!CatalogPreviewCurrent(card, movie, token)) return;
            var details = await RezkaService.Instance.Client.GetDetailsAsync(movie.Id, token);
            if (!CatalogPreviewCurrent(card, movie, token)) return;
            if (_previewCache.Count >= 24)
                _previewCache.Remove(_previewCache.Keys.First());
            _previewCache[movie.Id] = details;
            SetCatalogPreviewDetails(details);
            PositionCatalogPreview(card);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Info("[Catalog] Hover preview unavailable: " + ex.Message);
        }
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e) => Reload();
}
