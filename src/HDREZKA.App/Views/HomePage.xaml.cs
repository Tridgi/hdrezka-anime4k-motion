using HDREZKA.App.Controls;
using HDREZKA.App.Services;
using HDREZKA.Core.Api;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private readonly DispatcherTimer _heroTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly Dictionary<string, string> _heroDescriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _heroBackdrops = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _heroDescriptionCts;
    private CancellationTokenSource? _homeHoverCts;
    private MovieCard? _hoverCard;
    private string? _hoverMovieId;
    private readonly Dictionary<string, MovieDetailed> _hoverCache =
        new(StringComparer.OrdinalIgnoreCase);

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
        // Both the header and the final "See all" tile open the same listing.
        FilmsEndButton.Tag = FilmsAllButton.Tag;
        SeriesEndButton.Tag = SeriesAllButton.Tag;
        CartoonsEndButton.Tag = CartoonsAllButton.Tag;
        PopularEndButton.Tag = PopularAllButton.Tag;
        WatchingEndButton.Tag = WatchingAllButton.Tag;
        SoonEndButton.Tag = SoonAllButton.Tag;
        HomePreviewHeader.Text = Loc.Get("Details.AboutMovie");
        SettingsService.Instance.PosterSizeChanged += UpdateRailEndTileSizes;

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
        UpdateRailEndTileSizes();
        SettingsService.Instance.DetailPaletteChanged += OnHomePaletteChanged;
        SettingsService.Instance.PosterSizeChanged += OnPosterSizeChanged;
        TrackedSeriesService.UpdatesChanged += OnTrackedUpdatesChanged;
        RenderContinue();
        RenderTracked();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _heroTimer.Stop();
        HideHomePreview();
        try { _heroDescriptionCts?.Cancel(); } catch { }
        SettingsService.Instance.DetailPaletteChanged -= OnHomePaletteChanged;
        try { _loadCts?.Cancel(); } catch { }
        SettingsService.Instance.PosterSizeChanged -= OnPosterSizeChanged;
        TrackedSeriesService.UpdatesChanged -= OnTrackedUpdatesChanged;
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnTrackedUpdatesChanged()
    {
        DispatcherQueue.TryEnqueue(RenderTracked);
    }

    private void OnPosterSizeChanged() => DispatcherQueue.TryEnqueue(ApplyHeroMetrics);

    private void ApplyHeroMetrics()
    {
        HeroCard.Height = PosterMetrics.HeroCardHeight;
        HeroPosterBorder.Width = PosterMetrics.HeroWidth;
        HeroPosterBorder.Height = PosterMetrics.HeroHeight;
        HeroPosterClip.Rect = new Windows.Foundation.Rect(0, 0, PosterMetrics.HeroWidth, PosterMetrics.HeroHeight);
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
        HomePreviewHeader.Text = Loc.Get("Details.AboutMovie");
        var endLabel = Loc.Get("Home.MoreTitles");
        FilmsEndText.Text = endLabel;
        SeriesEndText.Text = endLabel;
        CartoonsEndText.Text = endLabel;
        PopularEndText.Text = endLabel;
        WatchingEndText.Text = endLabel;
        SoonEndText.Text = endLabel;
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
            BindSection("Films", FilmsSection, FilmsGrid, films.Result.Items);
            BindSection("Series", SeriesSection, SeriesGrid, series.Result.Items);
            BindSection("Cartoons", CartoonsSection, CartoonsGrid, cartoons.Result.Items);
            BindSection("Popular", PopularSection, PopularGrid, popular.Result.Items);
            BindSection("Watching", WatchingSection, WatchingGrid, watching.Result.Items);
            BindSection("Soon", SoonSection, SoonGrid, soon.Result.Items);
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
            var dot = new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                Tag = i,
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
        HeroDescription.Text = _heroDescriptions.GetValueOrDefault(movie.Id) ?? "";
        _ = LoadHeroDescriptionAsync(movie);


        if (!string.IsNullOrEmpty(movie.Poster))
        {
            // Через общий кэш: раньше герой каждый раз качал постер заново.
            PosterCache.SetSource(HeroPoster, movie.Poster);
            PosterCache.SetSource(HeroBackdrop, _heroBackdrops.GetValueOrDefault(movie.Id) ?? movie.Poster);
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
            if (HeroDots.Children[i] is Microsoft.UI.Xaml.Shapes.Ellipse dot)
            {
                // All indicators are circles; active state is conveyed by
                // accent, a gentle outline and opacity, not an oval.
                dot.Fill = i == _heroIndex
                    ? ThemeHelper.AccentTextBrush
                    : new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(110, 255, 255, 255));
                dot.Width = 9;
                dot.Height = 9;
                dot.Opacity = i == _heroIndex ? 1 : .72;
            }
        }
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

    private async Task LoadHeroDescriptionAsync(MovieSimple movie)
    {
        if (_heroDescriptions.TryGetValue(movie.Id, out var existing))
        {
            HeroDescription.Text = existing;
            return;
        }
        try { _heroDescriptionCts?.Cancel(); } catch { }
        var cts = new CancellationTokenSource();
        _heroDescriptionCts = cts;
        try
        {
            var details = await RezkaService.Instance.Client.GetDetailsAsync(movie.Id, cts.Token);
            if (cts.IsCancellationRequested) return;
            var synopsis = details.Description?.Trim() ?? "";
            _heroDescriptions[movie.Id] = synopsis;
            if (!string.IsNullOrWhiteSpace(details.BigPoster))
                _heroBackdrops[movie.Id] = details.BigPoster;
            if (_heroItems.Count > _heroIndex &&
                _heroItems[_heroIndex].Id == movie.Id)
            {
                HeroDescription.Text = synopsis;
                if (_heroBackdrops.TryGetValue(movie.Id, out var backdrop))
                    PosterCache.SetSource(HeroBackdrop, backdrop);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Description enriches the banner, but must never block navigation.
            Log.Info("[Home] Hero description unavailable: " + ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_heroDescriptionCts, cts))
                _heroDescriptionCts = null;
            cts.Dispose();
        }
    }

    private void UpdateRailEndTileSizes()
    {
        foreach (var button in new[]
        {
            FilmsEndButton, SeriesEndButton, CartoonsEndButton,
            PopularEndButton, WatchingEndButton, SoonEndButton
        })
        {
            button.Width = PosterMetrics.CardWidth;
            button.Height = PosterMetrics.CardHeight;
        }
        DispatcherQueue.TryEnqueue(UpdateRailButtons);
    }

    private (ScrollViewer Scroll, Button Previous, Button Next)? GetRail(string name) =>
        name switch
        {
            "Films" => (FilmsScroll, FilmsPrev, FilmsNext),
            "Series" => (SeriesScroll, SeriesPrev, SeriesNext),
            "Cartoons" => (CartoonsScroll, CartoonsPrev, CartoonsNext),
            "Popular" => (PopularScroll, PopularPrev, PopularNext),
            "Watching" => (WatchingScroll, WatchingPrev, WatchingNext),
            "Soon" => (SoonScroll, SoonPrev, SoonNext),
            _ => null
        };

    private void HomeRailNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name } button ||
            GetRail(name) is not { } rail) return;
        var direction = ReferenceEquals(button, rail.Next) ? 1 : -1;
        var step = Math.Max(PosterMetrics.CardWidth + 16,
            rail.Scroll.ViewportWidth * .82);
        var target = Math.Clamp(rail.Scroll.HorizontalOffset + direction * step,
            0, Math.Max(0, rail.Scroll.ScrollableWidth));
        rail.Scroll.ChangeView(target, null, null, false);
    }

    private void HomeRail_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        HideHomePreview();
        UpdateRailButtons();
    }

    private void HomeVerticalScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        => HideHomePreview();

    private void UpdateRailButtons()
    {
        foreach (var name in new[]
                 { "Films", "Series", "Cartoons", "Popular", "Watching", "Soon" })
        {
            if (GetRail(name) is not { } rail) continue;
            rail.Previous.IsEnabled = rail.Scroll.HorizontalOffset > 2;
            rail.Next.IsEnabled = rail.Scroll.HorizontalOffset <
                rail.Scroll.ScrollableWidth - 2;
        }
    }

    private void OnHomePaletteChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        if (_hoverMovieId != null &&
            _hoverCache.TryGetValue(_hoverMovieId, out var details))
            FillHomePreview(details);
        StyleHomePreviewBadges();
    });

    private void HomeMovie_HoverChanged(object sender, RecommendationHoverEventArgs e)
    {
        if (sender is not MovieCard card) return;
        if (!e.IsEntering)
        {
            if (ReferenceEquals(card, _hoverCard))
                HideHomePreview();
            return;
        }
        if (e.Movie == null) return;
        HideHomePreview();
        _hoverCard = card;
        _hoverMovieId = e.Movie.Id;
        _homeHoverCts = new CancellationTokenSource();
        _ = ShowHomePreviewAsync(card, e.Movie, _homeHoverCts.Token);
    }

    private void HideHomePreview()
    {
        try { _homeHoverCts?.Cancel(); } catch { }
        _homeHoverCts = null;
        _hoverCard = null;
        _hoverMovieId = null;
        HomePreviewCard.Visibility = Visibility.Collapsed;
    }

    private bool HomeHoverCurrent(MovieCard card, MovieSimple movie, CancellationToken ct) =>
        !ct.IsCancellationRequested &&
        ReferenceEquals(_hoverCard, card) &&
        _hoverMovieId == movie.Id && card.Movie?.Id == movie.Id;

    private void PositionHomePreview(MovieCard card)
    {
        var width = HomePreviewLayer.ActualWidth;
        var height = HomePreviewLayer.ActualHeight;
        if (width < 215 || height < 235)
        {
            HomePreviewCard.Visibility = Visibility.Collapsed;
            return;
        }
        var overlay = HomePreviewCard;
        overlay.Width = Math.Min(330, width - 28);
        overlay.MaxHeight = height - 24;
        HomePreviewDescription.MaxLines = height < 580 ? 3 : 5;
        HomePreviewActors.MaxLines = height < 580 ? 1 : 3;
        var origin = card.TransformToVisual(HomePreviewLayer)
            .TransformPoint(new Windows.Foundation.Point(0, 0));
        overlay.Measure(new Windows.Foundation.Size(overlay.Width, overlay.MaxHeight));
        var measuredHeight = Math.Min(overlay.DesiredSize.Height, overlay.MaxHeight);
        const double space = 14;
        var right = origin.X + card.ActualWidth + space;
        var left = origin.X - overlay.Width - space;
        var x = right + overlay.Width < width - 12
            ? right : left >= 12 ? left
            : Math.Max(12, (width - overlay.Width) / 2);
        var y = Math.Clamp(origin.Y, 12, Math.Max(12, height - measuredHeight - 12));
        Canvas.SetLeft(overlay, x);
        Canvas.SetTop(overlay, y);
    }

    private void StyleHomePreviewBadges()
    {
        var dark = ActualTheme != ElementTheme.Light;
        var colorful = SettingsService.Instance.DetailPalette == DetailsPalette.Colorful;
        var muted = dark ? Windows.UI.Color.FromArgb(255, 54, 57, 63)
                         : Windows.UI.Color.FromArgb(255, 231, 234, 237);
        Windows.UI.Color Tone(byte r, byte g, byte b) =>
            Windows.UI.Color.FromArgb(255, r, g, b);
        var tones = new[]
        {
            colorful ? (dark ? Tone(42, 69, 66) : Tone(210, 238, 232)) : muted,
            colorful ? (dark ? Tone(91, 75, 28) : Tone(252, 242, 194)) : muted,
            colorful ? (dark ? Tone(94, 58, 37) : Tone(250, 222, 202)) : muted,
        };
        var badges = new[] { HomePreviewRezka, HomePreviewImdb, HomePreviewKp };
        var values = new[] { HomePreviewRezkaText, HomePreviewImdbText, HomePreviewKpText };
        for (var i = 0; i < badges.Length; i++)
        {
            badges[i].Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(tones[i]);
            values[i].Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(dark
                ? Tone(241, 243, 246) : Tone(35, 39, 45));
        }
    }

    private void RenderHomePreviewGenres(IReadOnlyList<MovieGenre>? genres)
    {
        HomePreviewGenreRows.Children.Clear();
        if (genres is not { Count: > 0 }) return;
        var palette = GenreColors.Palette(
            genres.Select(g => g.Name).ToArray(),
            ActualTheme == ElementTheme.Light,
            SettingsService.Instance.DetailPalette == DetailsPalette.Colorful);
        StackPanel? row = null;
        for (var i = 0; i < Math.Min(4, genres.Count); i++)
        {
            if (i % 2 == 0)
            {
                row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                HomePreviewGenreRows.Children.Add(row);
            }
            row!.Children.Add(new TextBlock
            {
                Text = genres[i].Name, FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = palette[i].Fg,
                MaxWidth = 144, TextTrimming = TextTrimming.CharacterEllipsis
            });
        }
    }

    private void FillHomePreview(MovieDetailed details)
    {
        HomePreviewTitle.Text = details.Name;
        HomePreviewSubtitle.Text = details.OriginalName ?? "";
        var r = details.SiteRating?.Value;
        var imdb = details.ImdbRating?.Value;
        var kp = details.KpRating?.Value;
        HomePreviewRezka.Visibility = r != null ? Visibility.Visible : Visibility.Collapsed;
        HomePreviewImdb.Visibility = imdb != null ? Visibility.Visible : Visibility.Collapsed;
        HomePreviewKp.Visibility = kp != null ? Visibility.Visible : Visibility.Collapsed;
        HomePreviewRezkaText.Text = r != null ? "R " + r.Value.ToString("0.0") : "";
        HomePreviewImdbText.Text = imdb != null ? "IMDb " + imdb.Value.ToString("0.0") : "";
        HomePreviewKpText.Text = kp != null ? "КП " + kp.Value.ToString("0.0") : "";
        HomePreviewDescription.Text = details.Description ?? "";
        RenderHomePreviewGenres(details.Genres);
        HomePreviewDirector.Text = details.Producers is { Count: > 0 }
            ? Loc.Get("Details.Director") + ": " +
              string.Join(", ", details.Producers.Take(3).Select(x => x.Name)) : "";
        HomePreviewActors.Text = details.Actors is { Count: > 0 }
            ? Loc.Get("Details.Actors") + ": " +
              string.Join(", ", details.Actors.Take(4).Select(x => x.Name)) : "";
        StyleHomePreviewBadges();
        if (r is { } score)
        {
            HomePreviewRezka.Background = RezkaScoreColors.Gradient(score);
            HomePreviewRezkaText.Foreground =
                new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
        }
    }

    private async Task ShowHomePreviewAsync(MovieCard card, MovieSimple movie, CancellationToken ct)
    {
        try
        {
            // Fast local frame before waiting for slow website detail requests.
            await Task.Delay(160, ct);
            if (!HomeHoverCurrent(card, movie, ct)) return;
            HomePreviewHeader.Text = Loc.Get("Details.AboutMovie");
            HomePreviewTitle.Text = movie.Name ?? "";
            HomePreviewSubtitle.Text = movie.Details ?? "";
            HomePreviewRezka.Visibility = movie.Rating.HasValue
                ? Visibility.Visible : Visibility.Collapsed;
            HomePreviewImdb.Visibility = Visibility.Collapsed;
            HomePreviewKp.Visibility = Visibility.Collapsed;
            HomePreviewRezkaText.Text = movie.Rating.HasValue
                ? "R " + movie.Rating.Value.ToString("0.0") : "";
            HomePreviewDescription.Text = "";
            HomePreviewDirector.Text = "";
            HomePreviewActors.Text = "";
            HomePreviewGenreRows.Children.Clear();
            StyleHomePreviewBadges();
            HomePreviewCard.Visibility = Visibility.Visible;
            PositionHomePreview(card);

            if (_hoverCache.TryGetValue(movie.Id, out var cached))
            {
                FillHomePreview(cached);
                card.SetRatingFromDetails(cached.SiteRating?.Value);
                PositionHomePreview(card);
                return;
            }
            await Task.Delay(220, ct);
            if (!HomeHoverCurrent(card, movie, ct)) return;
            var details = await RezkaService.Instance.Client.GetDetailsAsync(movie.Id, ct);
            if (!HomeHoverCurrent(card, movie, ct)) return;
            if (_hoverCache.Count >= 24)
                _hoverCache.Remove(_hoverCache.Keys.First());
            _hoverCache[movie.Id] = details;
            FillHomePreview(details);
            card.SetRatingFromDetails(details.SiteRating?.Value);
            PositionHomePreview(card);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Info("[Home] Hover preview unavailable: " + ex.Message);
        }
    }

    private void BindSection(string id, StackPanel section, ItemsControl grid, IReadOnlyList<MovieSimple> items)
    {
        var preview = items.Take(SectionPreviewCount).ToList();
        grid.ItemsSource = preview;
        var show = preview.Count > 0;
        _sectionHasContent[id] = show;
        section.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        DispatcherQueue.TryEnqueue(UpdateRailButtons);
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
        var narrow = e.NewSize.Width < 700;
        HeroPosterBorder.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        HeroInnerGrid.Padding = narrow ? new Thickness(60, 18, 60, 30)
            : new Thickness(70, 20, 70, 34);
        HeroTitle.FontSize = e.NewSize.Width < 530 ? 22 : narrow ? 27 : 31;
        HeroDescription.MaxLines = narrow ? 3 : 4;
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
