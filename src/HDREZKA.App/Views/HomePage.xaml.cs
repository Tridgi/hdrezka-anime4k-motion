using HDREZKA.App.Controls;
using HDREZKA.App.Services;
using HDREZKA.Core.Api;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace HDREZKA.App.Views;

public sealed partial class HomePage : Page
{
    private const int SectionPreviewCount = 12;
    private const int HeroCount = 5;
    private const int HomeContinueCount = 3;

    private readonly Dictionary<string, bool> _sectionHasContent = new(StringComparer.OrdinalIgnoreCase);

    private bool _loaded;
    private DateTime _loadedAt;
    private CancellationTokenSource? _loadCts;
    private bool _mirrorRecovered;
    private readonly List<MovieSimple> _heroItems = new();
    private int _heroIndex;
    private readonly DispatcherTimer _heroTimer = new()
        { Interval = TimeSpan.FromSeconds(10) };
    private CancellationTokenSource? _heroSynopsisCancellation;
    private readonly Dictionary<string, MovieDetailed> _heroDetailsCache = new();
    private readonly Dictionary<string, IReadOnlyList<MovieSimple>> _sectionItems = new();
    private readonly Dictionary<string, (StackPanel Row, ScrollViewer Scroll,
        Button Previous, Button Next, Grid Frame)> _carouselWidgets = new();
    private readonly Dictionary<string, CatalogLaunch> _carouselLaunches = new();
    private MovieCard? _activePreviewCard;
    private string? _activePreviewId;
    private CancellationTokenSource? _previewCancellation;
    private readonly Dictionary<string, MovieDetailed> _previewCache = new();


    public HomePage()
    {
        InitializeComponent();

        // Страница кэшируется: возврат с деталей не пересоздаёт главную.
        NavigationCacheMode = NavigationCacheMode.Required;

        _heroTimer.Tick += (_, _) => ShowHero(_heroIndex + 1);

        FilmsAllButton.Tag = new CatalogLaunch(Section.Films, ListFilter.Latest);
        SeriesAllButton.Tag = new CatalogLaunch(Section.Series, ListFilter.Latest);
        CartoonsAllButton.Tag = new CatalogLaunch(Section.Cartoons, ListFilter.Latest);
        PopularAllButton.Tag = new CatalogLaunch(Section.Films, ListFilter.Popular);
        WatchingAllButton.Tag = new CatalogLaunch(Section.Films, ListFilter.WatchingNow);
        SoonAllButton.Tag = new CatalogLaunch(Section.Films, ListFilter.Soon);

        _carouselWidgets["Films"] = (FilmsRow, FilmsScroll, FilmsPrevious, FilmsNext, FilmsCarousel);
        _carouselWidgets["Series"] = (SeriesRow, SeriesScroll, SeriesPrevious, SeriesNext, SeriesCarousel);
        _carouselWidgets["Cartoons"] = (CartoonsRow, CartoonsScroll, CartoonsPrevious, CartoonsNext, CartoonsCarousel);
        _carouselWidgets["Popular"] = (PopularRow, PopularScroll, PopularPrevious, PopularNext, PopularCarousel);
        _carouselWidgets["Watching"] = (WatchingRow, WatchingScroll, WatchingPrevious, WatchingNext, WatchingCarousel);
        _carouselWidgets["Soon"] = (SoonRow, SoonScroll, SoonPrevious, SoonNext, SoonCarousel);
        _carouselLaunches["Films"] = (CatalogLaunch)FilmsAllButton.Tag;
        _carouselLaunches["Series"] = (CatalogLaunch)SeriesAllButton.Tag;
        _carouselLaunches["Cartoons"] = (CatalogLaunch)CartoonsAllButton.Tag;
        _carouselLaunches["Popular"] = (CatalogLaunch)PopularAllButton.Tag;
        _carouselLaunches["Watching"] = (CatalogLaunch)WatchingAllButton.Tag;
        _carouselLaunches["Soon"] = (CatalogLaunch)SoonAllButton.Tag;
        foreach (var pair in _carouselWidgets)
        {
            var id = pair.Key;
            pair.Value.Scroll.SizeChanged += (_, _) =>
                DispatcherQueue.TryEnqueue(() => UpdateCarouselButtons(id));
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ApplyLocalization();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        if (!_loaded || DateTime.UtcNow - _loadedAt > TimeSpan.FromMinutes(10))
        {
            _loaded = true;
            _loadedAt = DateTime.UtcNow;
            Load();
        }
        else if (_heroItems.Count > 1)
        {
            _heroTimer.Start();
        }

        ApplyHeroMetrics();
        SettingsService.Instance.PosterSizeChanged += OnPosterSizeChanged;
        TrackedSeriesService.UpdatesChanged += OnTrackedUpdatesChanged;
        RenderContinue();
        RenderTracked();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _heroTimer.Stop();
        _heroSynopsisCancellation?.Cancel();
        HideHomePreview();
        try { _loadCts?.Cancel(); } catch { }
        SettingsService.Instance.PosterSizeChanged -= OnPosterSizeChanged;
        TrackedSeriesService.UpdatesChanged -= OnTrackedUpdatesChanged;
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnTrackedUpdatesChanged()
    {
        DispatcherQueue.TryEnqueue(RenderTracked);
    }

    private void OnPosterSizeChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        ApplyHeroMetrics();
        RefreshCarousels();
    });

    private void ApplyHeroMetrics()
    {
        HeroCard.Height = PosterMetrics.HeroCardHeight;
        HeroPosterBorder.Width = PosterMetrics.HeroWidth;
        HeroPosterBorder.Height = PosterMetrics.HeroHeight;
        HeroPosterClip.Rect = new Windows.Foundation.Rect(0, 0,
            PosterMetrics.HeroWidth, PosterMetrics.HeroHeight);
        foreach (var section in _carouselWidgets.Values)
            section.Frame.Height = PosterMetrics.CardHeight + 120;
    }

    private void OnLanguageChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ApplyLocalization();
            RenderContinue();
            RenderTracked();
        });
    }

    private void ApplyLocalization()
    {
        LoadingText.Text = Loc.Get("Common.Loading");
        RetryButton.Content = Loc.Get("Common.Retry");
        MirrorButton.Content = Loc.Get("Settings.AutoMirror");
        HeroWatchButton.Content = Loc.Get("Common.Watch");
        HomePreviewHeading.Text = Loc.Get("Details.AboutMovie");
        RefreshCarousels();
        ContinueHeader.Text = Loc.Get("Continue.Home");
        TrackedHeader.Text = Loc.Get("Home.Tracked");
        FilmsHeader.Text = Loc.Get("Section.Films") + " — " + Loc.Get("Filter.Latest");
        SeriesHeader.Text = Loc.Get("Section.Series") + " — " + Loc.Get("Filter.Latest");
        CartoonsHeader.Text = Loc.Get("Section.Cartoons") + " — " + Loc.Get("Filter.Latest");
        PopularHeader.Text = Loc.Get("Filter.Popular");
        WatchingHeader.Text = Loc.Get("Filter.WatchingNow");
        SoonHeader.Text = Loc.Get("Filter.Soon");

        var showAll = Loc.Get("Common.ShowAll");
        FilmsAllText.Text = showAll;
        SeriesAllText.Text = showAll;
        CartoonsAllText.Text = showAll;
        PopularAllText.Text = showAll;
        WatchingAllText.Text = showAll;
        SoonAllText.Text = showAll;
        ContinueAllText.Text = showAll;
        TrackedAllText.Text = showAll;
    }

    private async void Load()
    {
        LoadingPanel.Visibility = Visibility.Visible;
        ErrorPanel.Visibility = Visibility.Collapsed;

        try { _loadCts?.Cancel(); } catch { }
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        var client = RezkaService.Instance.Client;
        var ttl = TimeSpan.FromMinutes(3);

        // Главная = 7 запросов. Кэш на 3 минуты убирает их при каждом
        // возвращении на страницу; смена зеркала кэш сбрасывает.
        Task<IReadOnlyList<MovieSimple>> CachedHot() =>
            ListCache.Instance.GetOrAddAsync("home.hot", ttl, () => client.GetHotMoviesAsync(ct: token));

        Task<(string Title, IReadOnlyList<MovieSimple> Items)> CachedSection(Section section) =>
            ListCache.Instance.GetOrAddAsync($"home.{section}", ttl,
                () => client.GetSectionListAsync(section, ListFilter.Latest, ct: token));

        Task<(string Title, IReadOnlyList<MovieSimple> Items)> CachedHome(ListFilter filter) =>
            ListCache.Instance.GetOrAddAsync($"home.{filter}", ttl,
                () => client.GetHomeListAsync(filter, ct: token));

        try
        {
            var hot = WrapList(CachedHot());
            var films = WrapTitle(CachedSection(Section.Films));
            var series = WrapTitle(CachedSection(Section.Series));
            var cartoons = WrapTitle(CachedSection(Section.Cartoons));
            var popular = WrapTitle(CachedHome(ListFilter.Popular));
            var watching = WrapTitle(CachedHome(ListFilter.WatchingNow));
            var soon = WrapTitle(CachedHome(ListFilter.Soon));

            await Task.WhenAll(hot, films, series, cartoons, popular, watching, soon);
            if (token.IsCancellationRequested) return;

            BindHero(hot.Result.Take(HeroCount).ToList());
            BindSection("Films", FilmsSection, films.Result.Items);
            BindSection("Series", SeriesSection, series.Result.Items);
            BindSection("Cartoons", CartoonsSection, cartoons.Result.Items);
            BindSection("Popular", PopularSection, popular.Result.Items);
            BindSection("Watching", WatchingSection, watching.Result.Items);
            BindSection("Soon", SoonSection, soon.Result.Items);
            ApplySectionLayout();

            LoadingPanel.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
            // Ушли со страницы — молча.
        }
        catch (RezkaException ex)
        {
            // 403/бан/сеть: одна попытка молча сменить зеркало и перезагрузиться.
            if (!_mirrorRecovered && ex.Kind is RezkaError.Network or RezkaError.AccessDenied or RezkaError.MirrorBanned)
            {
                _mirrorRecovered = true;
                if (await MirrorRecovery.TryRecoverAsync())
                {
                    Load();
                    return;
                }
            }

            ShowError(RezkaService.Instance.ErrorText(ex),
                ex.Kind is RezkaError.MirrorBanned or RezkaError.AccessDenied or RezkaError.Network);
        }
    }

    private void BindHero(IReadOnlyList<MovieSimple> movies)
    {
        _heroTimer.Stop();
        _heroItems.Clear();
        _heroItems.AddRange(movies.Where(m => m != null));
        _heroIndex = 0;
        _sectionHasContent["Hero"] = _heroItems.Count > 0;

        HeroDots.Children.Clear();
        for (var i = 0; i < _heroItems.Count; i++)
        {
            var dot = new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(100, 255, 255, 255)),
                BorderThickness = new Thickness(0),
                Tag = i
            };
            dot.Tapped += HeroDot_Tapped;
            HeroDots.Children.Add(dot);
        }

        var single = _heroItems.Count < 2;
        HeroPrevButton.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
        HeroNextButton.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
        HeroDots.Visibility = single ? Visibility.Collapsed : Visibility.Visible;

        if (_heroItems.Count == 0)
        {
            HeroCard.Visibility = Visibility.Collapsed;
            return;
        }

        HeroCard.Visibility = Visibility.Visible;
        ShowHero(0, animate: false);
        if (_heroItems.Count > 1)
        {
            _heroTimer.Start();
        }
    }

    private void ShowHero(int index, bool animate = true)
    {
        if (_heroItems.Count == 0) return;
        _heroIndex = ((index % _heroItems.Count) + _heroItems.Count) % _heroItems.Count;

        if (animate)
        {
            FadeHeroCard(apply: ApplyHeroContent);
        }
        else
        {
            ApplyHeroContent();
        }
    }

    private void ApplyHeroContent()
    {
        var movie = _heroItems[_heroIndex];
        HeroKicker.Text = $"{Loc.Get("Filter.Hot")} · #{_heroIndex + 1}";
        HeroTitle.Text = movie.Name ?? "";
        HeroDetails.Text = movie.Details ?? "";
        _heroSynopsisCancellation?.Cancel();
        HeroSynopsis.Text = "";
        _heroSynopsisCancellation = new CancellationTokenSource();
        _ = LoadHeroSynopsisAsync(movie, _heroSynopsisCancellation.Token);

        if (!string.IsNullOrEmpty(movie.Poster))
        {
            // Через общий кэш: раньше герой каждый раз качал постер заново.
            PosterCache.SetSource(HeroPoster, movie.Poster);
            PosterCache.SetSource(HeroBackdrop, movie.Poster);
        }
        else
        {
            PosterCache.SetSource(HeroPoster, null);
            PosterCache.SetSource(HeroBackdrop, null);
        }

        if (movie.Rating is { } rating)
        {
            HeroRatingBadge.Visibility = Visibility.Visible;
            HeroRatingText.Text = rating.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            HeroRatingBadge.Visibility = Visibility.Collapsed;
        }

        for (var i = 0; i < HeroDots.Children.Count; i++)
        {
            if (HeroDots.Children[i] is Border dot)
            {
                // Selected page remains a perfect circle (never a pill).
                var selected = i == _heroIndex;
                dot.Width = dot.Height = selected ? 11 : 9;
                dot.CornerRadius = new CornerRadius(selected ? 5.5 : 4.5);
                dot.BorderThickness = selected ? new Thickness(2) : new Thickness(0);
                dot.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.White);
                dot.Background = selected ? ThemeHelper.AccentBrush :
                    new SolidColorBrush(Windows.UI.Color.FromArgb(120, 255, 255, 255));
            }
        }
    }

    private async Task LoadHeroSynopsisAsync(MovieSimple movie, CancellationToken token)
    {
        try
        {
            if (!_heroDetailsCache.TryGetValue(movie.Id, out var details))
            {
                await Task.Delay(250, token);
                details = await RezkaService.Instance.Client.GetDetailsAsync(movie.Id, token);
                if (token.IsCancellationRequested) return;
                if (_heroDetailsCache.Count >= 5)
                    _heroDetailsCache.Remove(_heroDetailsCache.Keys.First());
                _heroDetailsCache[movie.Id] = details;
            }
            if (!token.IsCancellationRequested && _heroItems.Count > _heroIndex &&
                _heroItems[_heroIndex].Id == movie.Id)
                HeroSynopsis.Text = details.Description ?? "";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Info("[Home] Hero synopsis unavailable: " + ex.Message);
        }
    }

    private void HeroCard_PointerEntered(object sender, PointerRoutedEventArgs e)
        => _heroTimer.Stop();

    private void HeroCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_heroItems.Count > 1) RestartHeroTimer();
    }

    private void FadeHeroCard(Action apply)
    {
        var fadeOut = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var toZero = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(150)),
            EnableDependentAnimation = true,
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(toZero, HeroCard);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(toZero, "Opacity");
        fadeOut.Children.Add(toZero);
        fadeOut.Completed += (_, _) =>
        {
            apply();
            var fadeIn = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            var toOne = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(250)),
                EnableDependentAnimation = true,
            };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(toOne, HeroCard);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(toOne, "Opacity");
            fadeIn.Children.Add(toOne);
            fadeIn.Begin();
        };
        fadeOut.Begin();
    }

    private void RestartHeroTimer()
    {
        if (_heroItems.Count <= 1) return;
        _heroTimer.Stop();
        _heroTimer.Start();
    }

    private void BindSection(string id, StackPanel section,
        IReadOnlyList<MovieSimple> items)
    {
        var preview = items.Take(SectionPreviewCount).ToList();
        _sectionItems[id] = preview;
        _sectionHasContent[id] = preview.Count > 0;
        section.Visibility = preview.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RenderCarousel(id);
    }

    private void RefreshCarousels()
    {
        foreach (var id in _sectionItems.Keys.ToArray())
            RenderCarousel(id);
    }

    private void RenderCarousel(string id)
    {
        if (!_carouselWidgets.TryGetValue(id, out var ui) ||
            !_sectionItems.TryGetValue(id, out var items)) return;
        HideHomePreview();
        ui.Row.Children.Clear();
        var cardWidth = PosterMetrics.CardWidth;
        var posterHeight = PosterMetrics.CardHeight;
        ui.Frame.Height = posterHeight + 120;

        foreach (var movie in items)
        {
            var card = new MovieCard
            {
                Movie = movie,
                RecommendationPreview = true,
                Margin = new Thickness(7, 15, 7, 9),
                VerticalAlignment = VerticalAlignment.Top
            };
            card.RecommendationHoverChanged += HomeRecommendationCard_HoverChanged;
            ui.Row.Children.Add(card);
        }

        // A clearly distinct final slide navigates to the same catalog/filter
        // as the header's "Show all" button.
        var endPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center, Spacing = 12
        };
        endPanel.Children.Add(new FontIcon
        {
            Glyph = "\uE8A7", FontSize = 26,
            Foreground = ThemeHelper.AccentTextBrush,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        endPanel.Children.Add(new TextBlock
        {
            Text = Loc.Get("Home.MoreTitles"),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 15
        });
        endPanel.Children.Add(new TextBlock
        {
            Text = Loc.Get("Home.OpenCatalog"),
            FontSize = 12, Opacity = .76, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });
        var allButton = new Button
        {
            Content = endPanel,
            Tag = _carouselLaunches[id],
            Width = cardWidth,
            Height = posterHeight,
            Margin = new Thickness(7, 15, 7, 9),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Background = (Brush)Application.Current.Resources[
                "SubtleFillColorSecondaryBrush"],
            BorderBrush = ThemeHelper.AccentBrush,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        allButton.Click += ShowAll_Click;
        ui.Row.Children.Add(allButton);
        ui.Scroll.ChangeView(0, null, null, true);
        DispatcherQueue.TryEnqueue(() => UpdateCarouselButtons(id));
    }

    private void UpdateCarouselButtons(string id)
    {
        if (!_carouselWidgets.TryGetValue(id, out var ui)) return;
        ui.Previous.IsEnabled = ui.Scroll.HorizontalOffset > 2;
        ui.Next.IsEnabled = ui.Scroll.HorizontalOffset <
            ui.Scroll.ScrollableWidth - 2;
    }

    private void ScrollCarousel(string id, int direction)
    {
        if (!_carouselWidgets.TryGetValue(id, out var ui)) return;
        HideHomePreview();
        var step = Math.Max(PosterMetrics.CardWidth + 32,
            ui.Scroll.ActualWidth - PosterMetrics.CardWidth * .5);
        var next = Math.Clamp(ui.Scroll.HorizontalOffset + direction * step,
            0, ui.Scroll.ScrollableWidth);
        ui.Scroll.ChangeView(next, null, null, false);
    }

    private void CarouselPrevious_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) ScrollCarousel(id, -1);
    }

    private void CarouselNext_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) ScrollCarousel(id, 1);
    }

    private void CarouselScroll_ViewChanged(object sender,
        ScrollViewerViewChangedEventArgs e)
    {
        if (sender is ScrollViewer { Tag: string id })
            UpdateCarouselButtons(id);
        HideHomePreview();
    }

    private void HomeScrollViewer_ViewChanged(object sender,
        ScrollViewerViewChangedEventArgs e) => HideHomePreview();

    private void HomeRecommendationCard_HoverChanged(object sender,
        RecommendationHoverEventArgs e)
    {
        if (sender is not MovieCard card) return;
        if (!e.IsEntering)
        {
            if (ReferenceEquals(_activePreviewCard, card)) HideHomePreview();
            return;
        }
        if (e.Movie == null) return;

        HideHomePreview();
        _activePreviewCard = card;
        _activePreviewId = e.Movie.Id;
        _previewCancellation = new CancellationTokenSource();
        _ = ShowHomePreviewAsync(card, e.Movie, _previewCancellation.Token);
    }

    private void HideHomePreview()
    {
        try { _previewCancellation?.Cancel(); } catch { }
        _previewCancellation = null;
        _activePreviewCard = null;
        _activePreviewId = null;
        HomePreviewPanel.Visibility = Visibility.Collapsed;
    }

    private bool PreviewMatches(MovieCard card, MovieSimple movie, CancellationToken ct)
        => !ct.IsCancellationRequested && ReferenceEquals(_activePreviewCard, card)
           && _activePreviewId == movie.Id && card.Movie?.Id == movie.Id;

    private static SolidColorBrush PreviewBrush(byte r, byte g, byte b)
        => new(Windows.UI.Color.FromArgb(255, r, g, b));

    private void SetHomeRatingPalette()
    {
        var colorful = SettingsService.Instance.DetailPalette == DetailsPalette.Colorful;
        var dark = ActualTheme != ElementTheme.Light;
        var neutral = dark ? PreviewBrush(56, 59, 65) : PreviewBrush(229, 232, 235);
        if (!colorful)
        {
            HomePreviewRBackground.Background = neutral;
            HomePreviewImdbBackground.Background = neutral;
            HomePreviewKpBackground.Background = neutral;
        }
        else
        {
            static SolidColorBrush Mix(Windows.UI.Color value, Windows.UI.Color surface, double amount)
            {
                byte Channel(byte a, byte b) => (byte)Math.Clamp(
                    (int)Math.Round(a * amount + b * (1 - amount)), 0, 255);
                return PreviewBrush(Channel(value.R, surface.R),
                    Channel(value.G, surface.G), Channel(value.B, surface.B));
            }
            var background = dark
                ? Windows.UI.Color.FromArgb(255, 35, 38, 44)
                : Windows.UI.Color.FromArgb(255, 245, 247, 250);
            var accent = SettingsService.Instance.Theme == AppTheme.Manual
                ? ThemeHelper.AccentBrush.Color
                : Windows.UI.Color.FromArgb(255, 34, 145, 123);
            var factor = dark ? .42 : .25;
            HomePreviewRBackground.Background = Mix(accent, background, factor);
            HomePreviewImdbBackground.Background = Mix(
                Windows.UI.Color.FromArgb(255, 246, 198, 36), background, factor);
            HomePreviewKpBackground.Background = Mix(
                Windows.UI.Color.FromArgb(255, 253, 127, 48), background, factor);
        }
        var ink = dark ? PreviewBrush(245, 247, 249) : PreviewBrush(37, 40, 45);
        HomePreviewR.Foreground = ink;
        HomePreviewImdb.Foreground = ink;
        HomePreviewKp.Foreground = ink;
    }

    private void RenderHomePreviewGenres(IReadOnlyList<MovieGenre>? genres)
    {
        HomePreviewGenres.Inlines.Clear();
        if (genres == null) return;
        var colorful = SettingsService.Instance.DetailPalette == DetailsPalette.Colorful;
        var dark = ActualTheme != ElementTheme.Light;
        for (var i = 0; i < Math.Min(5, genres.Count); i++)
        {
            var name = genres[i].Name;
            var lower = name.ToLowerInvariant();
            var color = lower.Contains("комед") || lower.Contains("приключ") ||
                        lower.Contains("comedy") || lower.Contains("adventure")
                ? (dark ? PreviewBrush(125, 218, 159) : PreviewBrush(32, 123, 76))
                : lower.Contains("ужас") || lower.Contains("боев") ||
                  lower.Contains("horror") || lower.Contains("action")
                ? (dark ? PreviewBrush(249, 152, 170) : PreviewBrush(151, 48, 82))
                : lower.Contains("драм") || lower.Contains("fantasy") ||
                  lower.Contains("фэнтез")
                ? (dark ? PreviewBrush(202, 172, 253) : PreviewBrush(126, 76, 157))
                : (dark ? PreviewBrush(133, 193, 253) : PreviewBrush(50, 111, 181));
            HomePreviewGenres.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text = (i > 0 ? "  ·  " : "") + name,
                Foreground = colorful ? color
                    : dark ? PreviewBrush(188, 196, 206) : PreviewBrush(90, 96, 105),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
        }
    }

    private void PositionHomePreview(MovieCard card)
    {
        var width = HomePreviewLayer.ActualWidth;
        var height = HomePreviewLayer.ActualHeight;
        if (width < 220 || height < 230)
        {
            HomePreviewPanel.Visibility = Visibility.Collapsed;
            return;
        }
        HomePreviewPanel.Width = Math.Min(334, width - 28);
        HomePreviewPanel.MaxHeight = height - 24;
        HomePreviewDescription.MaxLines = height < 560 ? 3 : 5;
        var spot = card.TransformToVisual(HomePreviewLayer)
            .TransformPoint(new Windows.Foundation.Point(0, 0));
        HomePreviewPanel.Measure(new Windows.Foundation.Size(
            HomePreviewPanel.Width, HomePreviewPanel.MaxHeight));
        var popupHeight = Math.Min(HomePreviewPanel.DesiredSize.Height,
            HomePreviewPanel.MaxHeight);
        const double gap = 14;
        var toRight = spot.X + card.ActualWidth + gap;
        var toLeft = spot.X - HomePreviewPanel.Width - gap;
        var x = toRight + HomePreviewPanel.Width < width - 12
            ? toRight : toLeft >= 12 ? toLeft
            : Math.Max(12, (width - HomePreviewPanel.Width) / 2);
        var y = Math.Clamp(spot.Y - 7, 12, Math.Max(12, height - popupHeight - 12));
        Canvas.SetLeft(HomePreviewPanel, x);
        Canvas.SetTop(HomePreviewPanel, y);
    }

    private void ApplyHomePreviewDetails(MovieDetailed details)
    {
        HomePreviewTitle.Text = details.Name;
        HomePreviewOriginal.Text = details.OriginalName ?? "";
        HomePreviewRBackground.Visibility = details.SiteRating?.Value != null
            ? Visibility.Visible : Visibility.Collapsed;
        HomePreviewImdbBackground.Visibility = details.ImdbRating?.Value != null
            ? Visibility.Visible : Visibility.Collapsed;
        HomePreviewKpBackground.Visibility = details.KpRating?.Value != null
            ? Visibility.Visible : Visibility.Collapsed;
        HomePreviewR.Text = "R " + details.SiteRating?.Value?.ToString("0.0");
        HomePreviewImdb.Text = "IMDb " + details.ImdbRating?.Value?.ToString("0.0");
        HomePreviewKp.Text = "КП " + details.KpRating?.Value?.ToString("0.0");
        HomePreviewDescription.Text = details.Description ?? "";
        RenderHomePreviewGenres(details.Genres);
        SetHomeRatingPalette();
    }

    private async Task ShowHomePreviewAsync(MovieCard card, MovieSimple movie,
        CancellationToken token)
    {
        try
        {
            await Task.Delay(170, token);
            if (!PreviewMatches(card, movie, token)) return;
            HomePreviewHeading.Text = Loc.Get("Details.AboutMovie");
            HomePreviewTitle.Text = movie.Name ?? "";
            HomePreviewOriginal.Text = movie.Details ?? "";
            HomePreviewRBackground.Visibility = movie.Rating != null
                ? Visibility.Visible : Visibility.Collapsed;
            HomePreviewR.Text = "R " + movie.Rating?.ToString("0.0");
            HomePreviewImdbBackground.Visibility = Visibility.Collapsed;
            HomePreviewKpBackground.Visibility = Visibility.Collapsed;
            HomePreviewDescription.Text = "";
            RenderHomePreviewGenres(null);
            SetHomeRatingPalette();
            HomePreviewPanel.Visibility = Visibility.Visible;
            PositionHomePreview(card);

            if (_previewCache.TryGetValue(movie.Id, out var cached))
            {
                ApplyHomePreviewDetails(cached);
                PositionHomePreview(card);
                return;
            }
            await Task.Delay(260, token);
            if (!PreviewMatches(card, movie, token)) return;
            var detailed = await RezkaService.Instance.Client.GetDetailsAsync(movie.Id, token);
            if (!PreviewMatches(card, movie, token)) return;
            if (_previewCache.Count >= 20)
                _previewCache.Remove(_previewCache.Keys.First());
            _previewCache[movie.Id] = detailed;
            ApplyHomePreviewDetails(detailed);
            PositionHomePreview(card);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Info("[Home] Poster preview not available: " + ex.Message);
        }
    }

    /// <summary>
    /// Orders + hides home blocks per Settings → Home screen.
    /// Content presence comes from _sectionHasContent (bind methods).
    /// </summary>
    private void ApplySectionLayout()
    {
        var map = new Dictionary<string, UIElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["Hero"] = HeroCard,
            ["Continue"] = ContinueSection,
            ["Tracked"] = TrackedSection,
            ["Films"] = FilmsSection,
            ["Series"] = SeriesSection,
            ["Cartoons"] = CartoonsSection,
            ["Popular"] = PopularSection,
            ["Watching"] = WatchingSection,
            ["Soon"] = SoonSection,
        };

        var pos = 0;
        foreach (var s in SettingsService.Instance.HomeSections)
        {
            if (!map.TryGetValue(s.Id, out var el)) continue;
            el.Visibility = s.Visible && _sectionHasContent.GetValueOrDefault(s.Id)
                ? Visibility.Visible
                : Visibility.Collapsed;
            var idx = ContentPanel.Children.IndexOf(el);
            if (idx >= 0 && idx != pos)
            {
                ContentPanel.Children.Move((uint)idx, (uint)pos);
            }

            pos++;
        }
    }

    /// <summary>
    /// Local "continue watching" row: top-N positions from the app itself,
    /// no server requests. Refreshed on every navigation to Home.
    /// </summary>
    private void RenderContinue()
    {
        ContinueRow.Children.Clear();

        var items = PositionService.Instance.GetAll()
            .Take(HomeContinueCount)
            .Select(ContinueWatchingPage.FromLocal)
            .ToList();

        foreach (var item in items)
        {
            var card = new ContinueCard
            {
                Data = item,
                Margin = new Thickness(0, 0, 18, 26),
                IsHitTestVisible = true,
                PlayDirectly = SettingsService.Instance.PlayFromHomeDirectly,
            };
            card.DeleteRequested += OnHomeContinueDeleteRequested;
            card.WatchedToggled += OnHomeContinueWatchedToggled;
            card.PlayRequested += OnHomeContinuePlayRequested;
            ContinueRow.Children.Add(card);
        }

        _sectionHasContent["Continue"] = items.Count > 0;
        ApplySectionLayout();
    }

    private void OnHomeContinueDeleteRequested(object? sender, ContinueItem item)
    {
        PositionService.Instance.Remove(item.Id);
        RenderContinue();
    }

    private async void OnHomeContinueWatchedToggled(object? sender, ContinueItem item)
    {
        // Home row is local-only: just flip the offline key and re-read.
        if (await ContinueWatchingPage.ToggleWatchedAsync(item, Content.XamlRoot) != null)
        {
            RenderContinue();
        }
    }

    private void GoToDetails(ContinueItem item)
    {
        Nav.Go<DetailsPage>(new MovieSimple(Id: item.Id, Name: item.Title, Poster: item.Poster));
    }

    /// <summary>
    /// Direct resume from Home: resolves the saved voice/season/episode
    /// and opens the player without the details page. Falls back to
    /// details on any failure.
    /// </summary>
    private async void OnHomeContinuePlayRequested(object? sender, ContinueItem item)
    {
        if (sender is ContinueCard card) card.IsEnabled = false;
        try
        {
            if (item.MovieKey == null || item.TranslatorKey == null)
            {
                GoToDetails(item);
                return;
            }

            var client = RezkaService.Instance.Client;
            var details = await client.GetDetailsAsync(item.Id);
            var voice = details.VoiceActings?.FirstOrDefault(v => v.TranslatorId == item.TranslatorKey)
                ?? details.VoiceActings?.FirstOrDefault(v => v.IsSelected)
                ?? details.VoiceActings?.FirstOrDefault();
            if (voice == null || !details.IsAvailable || details.IsComingSoon)
            {
                GoToDetails(item);
                return;
            }

            // Сезоны зависят от озвучки: HTML отдаёт набор по умолчанию, а
            // сохранённая позиция могла быть в другой озвучке (у неё свой
            // набор сезонов), поэтому для сериалов запрашиваем get_episodes.
            var numeric = ContinueWatchingPage.ExtractNumericId(details.Id);
            IReadOnlyList<MovieSeason>? seasons = details.Seasons;
            var likelySeries = details.Seasons is { Count: > 0 } || voice.Url != null;
            if (numeric != null && likelySeries)
            {
                try
                {
                    seasons = await client.GetSeriesSeasonsAsync(numeric, voice, details.Favs);
                }
                catch (RezkaException)
                {
                    seasons = details.Seasons;
                }
            }
            else if (numeric == null && seasons == null)
            {
                GoToDetails(item);
                return;
            }

            var season = seasons?.FirstOrDefault(s => s.SeasonId == item.SeasonKey)
                ?? seasons?.FirstOrDefault(s => s.IsSelected)
                ?? seasons?.FirstOrDefault();
            var episode = season?.Episodes.FirstOrDefault(e => e.EpisodeId == item.EpisodeKey)
                ?? season?.Episodes.FirstOrDefault(e => e.IsSelected)
                ?? season?.Episodes.FirstOrDefault();

            var playerWindow = new PlayerWindow();
            playerWindow.ShowPlayer(new PlayerLaunch(details, voice, seasons, season, episode));
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
            GoToDetails(item);
        }
        finally
        {
            if (sender is ContinueCard card2) card2.IsEnabled = true;
        }
    }

    private void ContinueAll_Click(object sender, RoutedEventArgs e)
    {
        Nav.Go<ContinueWatchingPage>();
    }

    /// <summary>
    /// Tracked series with released episodes (from Updates store).
    /// Delete consumes the entry; eye toggle is a no-op here (no watch key).
    /// </summary>
    private void RenderTracked()
    {
        TrackedRow.Children.Clear();

        var items = TrackedSeriesService.GetUpdates()
            .Take(HomeContinueCount)
            .Select(u => new ContinueItem(
                Id: u.PagePath,
                Title: u.Title,
                Poster: u.Poster,
                Subtitle: $"{u.Text} · {u.Date:dd.MM.yyyy}",
                PositionSeconds: 0,
                DurationSeconds: 0,
                UpdatedAt: u.Date))
            .ToList();

        foreach (var item in items)
        {
            var card = new ContinueCard
            {
                Data = item,
                Margin = new Thickness(0, 0, 18, 26),
                IsHitTestVisible = true,
            };
            card.DeleteRequested += OnTrackedDeleteRequested;
            TrackedRow.Children.Add(card);
        }

        _sectionHasContent["Tracked"] = items.Count > 0;
        ApplySectionLayout();
    }

    private void OnTrackedDeleteRequested(object? sender, ContinueItem item)
    {
        var stored = TrackedSeriesService.GetUpdates()
            .FirstOrDefault(u => u.PagePath == item.Id && u.Title == item.Title && u.Date == item.UpdatedAt);
        if (stored != null)
        {
            TrackedSeriesService.RemoveUpdate(stored);
        }

        RenderTracked();
    }

    private void TrackedAll_Click(object sender, RoutedEventArgs e)
    {
        Nav.Go<UpdatesPage>();
    }

    private void HeroWatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_heroItems.Count > 0)
        {
            Nav.Go<DetailsPage>(_heroItems[_heroIndex]);
        }
    }

    private void HeroPrevButton_Click(object sender, RoutedEventArgs e)
    {
        ShowHero(_heroIndex - 1);
        RestartHeroTimer();
    }

    private void HeroNextButton_Click(object sender, RoutedEventArgs e)
    {
        ShowHero(_heroIndex + 1);
        RestartHeroTimer();
    }

    private void HeroDot_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is int index && index != _heroIndex)
        {
            ShowHero(index);
            RestartHeroTimer();
        }
    }

    private void HeroCard_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        HeroClipRect.Rect = new Windows.Foundation.Rect(0, 0, Math.Max(0, e.NewSize.Width), Math.Max(0, e.NewSize.Height));
    }

    private void ShowAll_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is CatalogLaunch launch)
        {
            Nav.Go<CatalogPage>(launch);
        }
    }

    private static async Task<IReadOnlyList<MovieSimple>> WrapList(Task<IReadOnlyList<MovieSimple>> task)
    {
        try
        {
            return await task;
        }
        catch (OperationCanceledException)
        {
            throw; // уход со страницы не должен выглядеть как «пустая главная»
        }
        catch (RezkaException ex) when (ex.Kind is RezkaError.Network or RezkaError.MirrorBanned
            or RezkaError.AccessDenied or RezkaError.LoginRequired)
        {
            // 403 и «нужен вход» нельзя превращать в пустой список: пользователь
            // получал пустую главную без сообщения и без кнопки смены зеркала.
            throw;
        }
        catch
        {
            return [];
        }
    }

    private static async Task<(string Title, IReadOnlyList<MovieSimple> Items)> WrapTitle(
        Task<(string Title, IReadOnlyList<MovieSimple> Items)> task)
    {
        try
        {
            return await task;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RezkaException ex) when (ex.Kind is RezkaError.Network or RezkaError.MirrorBanned
            or RezkaError.AccessDenied or RezkaError.LoginRequired)
        {
            throw;
        }
        catch
        {
            return ("", []);
        }
    }

    private void ShowError(string message, bool showMirrorButton = false)
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Visible;
        ErrorText.Text = message;
        MirrorButton.Visibility = showMirrorButton ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e) => Load();

    private async void MirrorButton_Click(object sender, RoutedEventArgs e)
    {
        MirrorButton.IsEnabled = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var applied = await MirrorService.PickAndApplyAsync(Content.XamlRoot, DispatcherQueue, cts.Token);
            if (applied != null) Load();
        }
        catch
        {
        }
        finally
        {
            MirrorButton.IsEnabled = true;
        }
    }
}
