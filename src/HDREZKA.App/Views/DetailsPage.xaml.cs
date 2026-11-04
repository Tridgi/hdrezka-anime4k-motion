using HDREZKA.App.Services;
using HDREZKA.App.Controls;
using HDREZKA.Core.Api;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;

namespace HDREZKA.App.Views;

public sealed record PlayerLaunch(
    MovieDetailed Details,
    MovieVoiceActing Voice,
    IReadOnlyList<MovieSeason>? Seasons,
    MovieSeason? Season,
    MovieEpisode? Episode,
    string? LocalFile = null);

public sealed partial class DetailsPage : Page
{
    private MovieSimple? _movie;
    private MovieDetailed? _details;
    private IReadOnlyList<MovieSeason>? _seasons;
    private MovieVoiceActing? _voice;
    private MovieSeason? _season;
    private MovieEpisode? _episode;
    private int _commentsPage = 1;
    private bool _commentsLoaded;
    private bool _commentsLoading;
    private bool _commentSending;
    private readonly List<CommentItem> _loadedComments = [];
    private bool _scheduleExpanded;
    private string? _replyTo;
    private bool _mirrorRecovered;
    private int? _bookmarkedCategoryId;
    private int _seasonsGeneration;
    private CancellationTokenSource? _loadCts;
    private IReadOnlyList<MovieSimple> _watchAlsoItems = [];
    private int _watchAlsoOffset;
    private int _watchAlsoPageSize = 3;
    private double _lastWatchAlsoWidth = -1;
    private int _lastWatchAlsoOffset = -1;
    private bool _watchAlsoRefreshQueued;
    private MovieCard? _hoverRecommendationCard;
    private string? _hoverRecommendationId;
    private CancellationTokenSource? _hoverRecommendationCts;
    private readonly Dictionary<string, MovieDetailed> _recommendationPreviewCache =
        new(StringComparer.OrdinalIgnoreCase);

    public DetailsPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            try { Log.Info("[Details] Loaded"); } catch { }
            UpdateAdaptiveLayout();
        };
        ActualThemeChanged += (_, _) => DispatcherQueue.TryEnqueue(RefreshPresentation);
        SizeChanged += (_, _) => { HideRecommendationPreview(); UpdateAdaptiveLayout(); };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ApplyLocalization();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        SettingsService.Instance.PosterSizeChanged += OnPosterSizeChanged;
        SettingsService.Instance.DetailPaletteChanged += OnDetailPaletteChanged;
        ThemeHelper.AccentChanged += OnAccentChanged;
        ApplyPosterMetrics();

        if (e.Parameter is MovieSimple movie && _movie?.Id != movie.Id)
        {
            _movie = movie;
            _ = LoadAsync(movie);
        }
        else if (e.Parameter is MovieDetailed details && _details?.Id != details.Id)
        {
            _movie = new MovieSimple(Id: details.Id, Name: details.Name, Poster: details.Poster);
            _ = LoadDetailsAsync(details);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        HideRecommendationPreview();
        // Ушли со страницы: отменяем незавершённые запросы, чтобы не держать
        // семафор клиента и не обновлять уже невидимый UI.
        try { _loadCts?.Cancel(); } catch { }
        try { _loadCts?.Dispose(); } catch { }
        _loadCts = null;
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        SettingsService.Instance.PosterSizeChanged -= OnPosterSizeChanged;
        SettingsService.Instance.DetailPaletteChanged -= OnDetailPaletteChanged;
        ThemeHelper.AccentChanged -= OnAccentChanged;
    }

    private void OnPosterSizeChanged() => DispatcherQueue.TryEnqueue(ApplyPosterMetrics);

    private void OnDetailPaletteChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        RefreshPresentation();
        if (RecommendationPreviewOverlay.Visibility == Visibility.Visible)
        {
            if (_hoverRecommendationId != null &&
                _recommendationPreviewCache.TryGetValue(_hoverRecommendationId, out var details))
                UpdateRecommendationPreview(details);
            StyleRecommendationPreview();
        }
    });
    private void OnAccentChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        RefreshPresentation();
        BuildVoiceChoices();
        UpdateAccentActions();
    });

    private bool _isNarrow;
    private bool _stackSelectors;
    private bool _tightPadding;
    private bool _smallPoster;
    private int _lastLayoutBucket = -1;
    private bool _descriptionExpanded;
    private bool _peopleExpanded;

    // Breakpoints are based on available page width, never monitor pixels.
    // 740 DIP: poster/info stack; 670 DIP: season and voice stack; 510 DIP:
    // reduce margins and put secondary buttons on separate rows.
    private void UpdateAdaptiveLayout()
    {
        var width = ActualWidth;
        if (width <= 0) return;
        var narrow = width < 740;
        var stackSelectors = width < 670;
        var tightPadding = width < 510;
        var smallPoster = width < 1050;
        var bucket = (int)(width / 120);
        if (narrow == _isNarrow && stackSelectors == _stackSelectors &&
            tightPadding == _tightPadding && smallPoster == _smallPoster &&
            bucket == _lastLayoutBucket) return;

        _isNarrow = narrow;
        _stackSelectors = stackSelectors;
        _tightPadding = tightPadding;
        _smallPoster = smallPoster;
        _lastLayoutBucket = bucket;

        MainPanel.Padding = width < 510
            ? new Thickness(12, 14, 12, 20)
            : width < 850 ? new Thickness(20, 16, 20, 24)
            : new Thickness(28, 16, 28, 24);

        HeaderGrid.ColumnSpacing = narrow ? 0 : 24;
        Grid.SetColumnSpan(PosterPanel, narrow ? 2 : 1);
        Grid.SetRow(InfoPanel, narrow ? 1 : 0);
        Grid.SetColumn(InfoPanel, narrow ? 0 : 1);
        Grid.SetColumnSpan(InfoPanel, narrow ? 2 : 1);

        ActionsRow.Orientation = width < 510
            ? Orientation.Vertical : Orientation.Horizontal;
        YourRatingLabelColumn.Width = new GridLength(MetaLabelWidth);
        UpdateEpisodeListWidth();
        ApplyPosterMetrics();
        ArrangeSelectionControls();

        if (_details != null) BuildMeta(_details);
    }

    private void ArrangeSelectionControls()
    {
        var hasSeasons = _seasons is { Count: > 0 };
        if (!hasSeasons)
        {
            SeasonColumn.Width = new GridLength(0);
            ChoiceGrid.ColumnSpacing = 0;
            Grid.SetRow(VoicesPanel, 0);
            Grid.SetColumn(VoicesPanel, 0);
            Grid.SetColumnSpan(VoicesPanel, 2);
        }
        else if (_stackSelectors)
        {
            SeasonColumn.Width = new GridLength(1, GridUnitType.Star);
            ChoiceGrid.ColumnSpacing = 0;
            Grid.SetRow(SeasonsPanel, 0);
            Grid.SetColumn(SeasonsPanel, 0);
            Grid.SetColumnSpan(SeasonsPanel, 2);
            Grid.SetRow(VoicesPanel, 1);
            Grid.SetColumn(VoicesPanel, 0);
            Grid.SetColumnSpan(VoicesPanel, 2);
        }
        else
        {
            SeasonColumn.Width = new GridLength(180);
            ChoiceGrid.ColumnSpacing = 16;
            Grid.SetRow(SeasonsPanel, 0);
            Grid.SetColumn(SeasonsPanel, 0);
            Grid.SetColumnSpan(SeasonsPanel, 1);
            Grid.SetRow(VoicesPanel, 0);
            Grid.SetColumn(VoicesPanel, 1);
            Grid.SetColumnSpan(VoicesPanel, 1);
        }
    }

    private void PlaybackChoiceBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateEpisodeListWidth();

    private void UpdateEpisodeListWidth()
    {
        // ItemsWrapGrid must receive a *finite* measure width. Bound it to the
        // playback card's real content width; this naturally fits 7, 25 or
        // 50 episodes across as many rows as necessary at each window size.
        var width = PlaybackChoiceBorder.ActualWidth;
        if (width > 0)
            EpisodesList.Width = Math.Max(46, width - 32);
    }

    private void ApplyPosterMetrics()
    {
        // User poster size remains the source of truth; cap details width so
        // controls never steal half the viewport at large poster settings.
        var max = _isNarrow || (ActualWidth > 0 && ActualWidth < 1050) ? 210 : 250;
        var width = Math.Clamp(PosterMetrics.DetailsWidth, 164, max);
        PosterPanel.Width = width;
        PosterImage.Width = width;
        PosterImage.Height = Math.Round(width * 1.5);
    }

    private void OnLanguageChanged() => DispatcherQueue.TryEnqueue(ApplyLocalization);

    private void ApplyLocalization()
    {
        PreviewHeader.Text = Loc.Get("Details.AboutMovie");
        PreviewGenresHeader.Text = Loc.Get("Details.Genre");
        WatchText.Text = Loc.Get("Common.Watch");
        UpdateBookmarkLabel();
        YourRatingHeader.Text = Loc.Get("Details.YourRating");
        EpisodeRightClickHint.Text = Loc.Get("Details.EpisodeRightClick");
        ToolTipService.SetToolTip(InlineWatchButton, Loc.Get("Common.Watch"));
        TrackText.Text = Loc.Get("Details.Track");
        TrailerText.Text = Loc.Get("Common.Trailer");
        DownloadText.Text = Loc.Get("Downloads.Download");
        SeasonDownloadText.Text = Loc.Get("Downloads.Season");
        if (_details != null) RenderSchedule(_details);
        VoicesHeader.Text = Loc.Get("Common.VoiceActing");
        SeasonsHeader.Text = Loc.Get("Common.Season");
        WatchAlsoHeader.Text = Loc.Get("Details.WatchAlso");
        ToolTipService.SetToolTip(WatchAlsoPreviousButton, Loc.Get("Details.PreviousRecommendations"));
        ToolTipService.SetToolTip(WatchAlsoNextButton, Loc.Get("Details.NextRecommendations"));
        CommentsToggleText.Text = Loc.Get(CommentsToggle.IsChecked == true
            ? "Details.HideComments" : "Details.ViewComments") +
            $" ({_details?.CommentsCount ?? 0})";
        RetryButton.Content = Loc.Get("Common.Retry");
        CommentInput.PlaceholderText = Loc.Get("Comments.Placeholder");
        // The send button now has an icon and a localized TextBlock child.
        CommentsMoreButton.Content = Loc.Get("Common.LoadMore");
        CommentsHeader.Text = Loc.Get("Common.Comments");
        CommentsSortHint.Text = Loc.Get("Comments.SortLoadedOnly");
        CommentComposerHeader.Text = Loc.Get("Comments.Write");
        CommentCancelReplyButton.Content = Loc.Get("Comments.CancelReply");
        CommentSendText.Text = Loc.Get("Common.Send");
        CommentOpenSiteText.Text = Loc.Get("Comments.OpenOnSite");
        if (CommentsSortBox.Items.Count >= 4)
        {
            ((ComboBoxItem)CommentsSortBox.Items[0]).Content = Loc.Get("Comments.SortSite");
            ((ComboBoxItem)CommentsSortBox.Items[1]).Content = Loc.Get("Comments.SortPopular");
            ((ComboBoxItem)CommentsSortBox.Items[2]).Content = Loc.Get("Comments.SortOldest");
            ((ComboBoxItem)CommentsSortBox.Items[3]).Content = Loc.Get("Comments.SortAuthor");
        }
        if (CommentsSortBox.SelectedIndex < 0) CommentsSortBox.SelectedIndex = 0;
        UpdateCommentComposer();
        EpisodesHeader.Text = Loc.Get("Details.Episodes");
        DescriptionExpandButton.Content = Loc.Get(_descriptionExpanded ? "Details.ShowLess" : "Details.ShowMore");
        if (_details != null) BuildMeta(_details);
    }

    private async Task LoadAsync(MovieSimple movie)
    {
        MainPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;
        ErrorPanel.Visibility = Visibility.Collapsed;

        try { _loadCts?.Cancel(); } catch { }
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        try
        {
            var details = await RezkaService.Instance.Client.GetDetailsAsync(movie.Id, token);
            await LoadDetailsAsync(details);
        }
        catch (OperationCanceledException)
        {
            // Пользователь ушёл со страницы.
        }
        catch (RezkaException ex)
        {
            // 403/бан/сеть: одна попытка молча сменить зеркало и перезагрузить.
            if (!_mirrorRecovered && ex.Kind is RezkaError.Network or RezkaError.AccessDenied or RezkaError.MirrorBanned)
            {
                _mirrorRecovered = true;
                if (await MirrorRecovery.TryRecoverAsync(movie.Id))
                {
                    await LoadAsync(movie);
                    return;
                }
            }

            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            ErrorText.Text = RezkaService.Instance.ErrorText(ex);
        }
    }

    private Task LoadDetailsAsync(MovieDetailed details)
    {
        _details = details;
        DispatcherQueue.TryEnqueue(() =>
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
            MainPanel.Visibility = Visibility.Visible;
            try
            {
                Render(details);
            }
            catch (Exception ex)
            {
                // Keep a usable error screen instead of crashing the app when
                // a movie contains unexpected metadata or UI elements.
                Log.Error("[Details] Render failed for " + details.Id, ex);
                MainPanel.Visibility = Visibility.Collapsed;
                ErrorPanel.Visibility = Visibility.Visible;
                ErrorText.Text = Loc.Get("Common.Error") + ": " + ex.Message;
                return;
            }

            // HTML отдаёт сезоны только для озвучки по умолчанию: тихо сверяем
            // с get_episodes и восстанавливаем последнюю озвучку/сезон из истории
            // (у разных озвучек разный набор сезонов, например 1 или 2).
            if (details.Seasons is { Count: > 0 } && _voice != null)
            {
                _ = RestoreOrSyncSeasonsAsync();
            }
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// Открывает тайтл в той озвучке/сезоне/серии, где пользователь остановился,
    /// и синхронизирует список сезонов через get_episodes.
    /// </summary>
    private async Task RestoreOrSyncSeasonsAsync()
    {
        if (_details == null) return;

        var numericId = ExtractNumericId(_details.Id);
        var last = PositionService.Instance.GetLatestFor(numericId ?? _details.Id, _details.Id);

        var savedVoice = last == null
            ? null
            : _details.VoiceActings?.FirstOrDefault(v => v.TranslatorId == last.TranslatorId);

        if (savedVoice != null)
        {
            _voice = savedVoice;
            MarkSelectedVoice(savedVoice);
            await RefreshSeasonsAsync(savedVoice, resetSelection: true, _loadCts?.Token ?? default);
        }
        else
        {
            await RefreshSeasonsAsync(_voice, resetSelection: false, _loadCts?.Token ?? default);
        }

        if (last?.SeasonId == null || _seasons == null || _seasons.Count == 0) return;

        var season = _seasons.FirstOrDefault(s => s.SeasonId == last.SeasonId);
        if (season == null) return;

        _season = season;
        _episode = season.Episodes.FirstOrDefault(ep => ep.EpisodeId == last.EpisodeId)
            ?? season.Episodes.FirstOrDefault();

        RenderSeasons();
        UpdateWatchedButton();
        UpdateSeasonDownloadVisibility();
    }

    private void MarkSelectedVoice(MovieVoiceActing voice)
    {
        VoiceBox.SelectionChanged -= VoiceBox_SelectionChanged;
        var item = VoiceBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(candidate => candidate.Tag is MovieVoiceActing v &&
                v.TranslatorId == voice.TranslatorId);
        if (item != null) VoiceBox.SelectedItem = item;
        VoiceBox.SelectionChanged += VoiceBox_SelectionChanged;
    }

    private void Render(MovieDetailed details)
    {
        TitleText.Text = details.Name;
        OrigTitleText.Text = details.OriginalName ?? "";
        PosterCache.SetSource(PosterImage, details.Poster);

        _descriptionExpanded = false;
        _peopleExpanded = false;
        DescriptionText.Text = details.Description ?? "";
        DescriptionText.MaxLines = 3;
        DescriptionText.TextTrimming = TextTrimming.CharacterEllipsis;
        DescriptionPanel.Visibility = string.IsNullOrWhiteSpace(DescriptionText.Text)
            ? Visibility.Collapsed : Visibility.Visible;
        DescriptionExpandButton.Visibility = DescriptionText.Text.Length >
            (ActualWidth < 440 ? 85 : ActualWidth < 740 ? 135 : 180)
            ? Visibility.Visible : Visibility.Collapsed;
        DescriptionExpandButton.Content = Loc.Get("Details.ShowMore");
        RefreshPresentation();
        BuildRateRow();
        UpdateAccentActions();

        // Preserve translator IDs and selected instance: the episode endpoint
        // needs the MovieVoiceActing object, not a localized display string.
        _seasons = details.Seasons;
        _voice = details.VoiceActings?.FirstOrDefault(v => v.IsSelected)
            ?? details.VoiceActings?.FirstOrDefault();
        _season = _seasons?.FirstOrDefault(s => s.IsSelected) ?? _seasons?.FirstOrDefault();
        _episode = _season?.Episodes.FirstOrDefault(ep => ep.IsSelected)
            ?? _season?.Episodes.FirstOrDefault();

        VoicesPanel.Visibility = details.VoiceActings is { Count: > 0 }
            ? Visibility.Visible : Visibility.Collapsed;
        BuildVoiceChoices();

        AgeRatingBorder.Visibility = string.IsNullOrWhiteSpace(details.AgeRestriction)
            ? Visibility.Collapsed : Visibility.Visible;
        AgeRatingText.Text = details.AgeRestriction ?? "";
        UpdateAgeBadgeStyle();

        RenderSeasons();
        UpdateSeasonDownloadVisibility();

        WatchButton.IsEnabled = details.IsAvailable && !details.IsComingSoon && _voice != null;
        InlineWatchButton.IsEnabled = WatchButton.IsEnabled;
        if (details.IsComingSoon)
        {
            WatchText.Text = Loc.Get("Details.ComingSoon");
        }
        else if (!details.IsAvailable)
        {
            WatchText.Text = Loc.Get("Details.Unavailable");
        }
        else if (WatchText.Text == Loc.Get("Details.ComingSoon") || WatchText.Text == Loc.Get("Details.Unavailable"))
        {
            WatchText.Text = Loc.Get("Common.Watch");
        }

        _scheduleExpanded = false;
        _loadedComments.Clear();
        _commentsPage = 1;
        _commentsLoaded = false;
        _replyTo = null;
        CommentsList.Children.Clear();
        CommentsPanel.Visibility = Visibility.Collapsed;
        CommentsCard.Visibility = Visibility.Collapsed;
        CommentsToggle.IsChecked = false;
        CommentsSection.Visibility = Visibility.Visible;
        UpdateCommentComposer();
        CommentsToggleText.Text = $"{Loc.Get("Details.ViewComments")} ({details.CommentsCount})";

        UpdateWatchedButton();
        UpdateTrackButton();
        _bookmarkedCategoryId = null;
        UpdateBookmarkLabel();
        UpdateVoicesRatingButton(details);
        RenderSchedule(details);
        RenderWatchAlso(details);
        Log.Info("[Details] Rendered " + details.Id);
    }

    private void DescriptionExpandButton_Click(object sender, RoutedEventArgs e)
    {
        _descriptionExpanded = !_descriptionExpanded;
        DescriptionText.MaxLines = _descriptionExpanded ? 0 : 3;
        DescriptionText.TextTrimming = _descriptionExpanded
            ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        DescriptionExpandButton.Content = Loc.Get(_descriptionExpanded
            ? "Details.ShowLess" : "Details.ShowMore");
    }

    private static string NormalizeVoiceName(string name) =>
        new(name.Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant).ToArray());

    /// <summary>
    /// Rebuilding the ComboBox never fires voice change requests. The selected
    /// translator is restored by immutable TranslatorId, not display text.
    /// Ratings are supplemental and may not exist for every translation.
    /// </summary>
    private void BuildVoiceChoices()
    {
        if (_details?.VoiceActings == null) return;
        VoiceBox.SelectionChanged -= VoiceBox_SelectionChanged;
        VoiceBox.Items.Clear();

        var ratingByName = _details.VoiceActingRatings?
            .Where(v => float.IsFinite(v.Percent))
            .GroupBy(v => NormalizeVoiceName(v.Name))
            .ToDictionary(g => g.Key, g => g.First().Percent);

        foreach (var voice in _details.VoiceActings)
        {
            var line = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var name = new TextBlock
            {
                Text = voice.Name + (voice.IsPremium ? " ★" : ""),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 215,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(name, voice.Name);
            line.Children.Add(name);

            if (ratingByName != null &&
                ratingByName.TryGetValue(NormalizeVoiceName(voice.Name), out var percent))
            {
                line.Children.Add(new TextBlock
                {
                    Text = percent.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + " %",
                    Foreground = ThemeHelper.AccentTextBrush,
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.NoWrap
                });
            }
            var item = new ComboBoxItem
            {
                Content = line,
                Tag = voice,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            ToolTipService.SetToolTip(item, voice.Name);
            VoiceBox.Items.Add(item);
        }
        var selected = VoiceBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is MovieVoiceActing voice &&
                                     voice.TranslatorId == _voice?.TranslatorId);
        VoiceBox.SelectedItem = selected;
        VoiceBox.SelectionChanged += VoiceBox_SelectionChanged;
    }

    private void UpdateAccentActions()
    {
        var accent = ThemeHelper.AccentBrush.Color;
        WatchGlowOuter.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Windows.UI.Color.FromArgb(255, accent.R, accent.G, accent.B));
        WatchGlowInner.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Windows.UI.Color.FromArgb(255, accent.R, accent.G, accent.B));
        WatchGlowOuter.Opacity = 0.14;
        WatchGlowInner.Opacity = 0.23;

        if (SettingsService.Instance.Theme == AppTheme.Manual)
        {
            WatchButton.Background = ThemeHelper.AccentBrush;
            WatchButton.Foreground = ThemeHelper.AccentForegroundBrush;
            InlineWatchButton.Background = ThemeHelper.AccentBrush;
            InlineWatchButton.Foreground = ThemeHelper.AccentForegroundBrush;
        }
        else
        {
            WatchButton.ClearValue(Button.BackgroundProperty);
            WatchButton.ClearValue(Button.ForegroundProperty);
            InlineWatchButton.ClearValue(Button.BackgroundProperty);
            InlineWatchButton.ClearValue(Button.ForegroundProperty);
        }
    }

    /// <summary>
    /// Keep the recommendations in a WinUI-managed ListView. In particular,
    /// never change Grid.Children/ColumnDefinitions synchronously in SizeChanged:
    /// that reenters native WinUI layout and can terminate the process.
    /// </summary>
    // One overlay lives above the scroll viewer. MovieCards only emit pointer
    // events: they cannot create popups clipped by their own ListView.
    private void RecommendationCard_HoverChanged(object sender,
        RecommendationHoverEventArgs e)
    {
        if (sender is not MovieCard card) return;
        if (!e.IsEntering)
        {
            if (ReferenceEquals(card, _hoverRecommendationCard))
                HideRecommendationPreview();
            return;
        }
        if (e.Movie == null) return;

        HideRecommendationPreview();
        _hoverRecommendationCard = card;
        _hoverRecommendationId = e.Movie.Id;
        var cts = new CancellationTokenSource();
        _hoverRecommendationCts = cts;
        _ = ShowRecommendationPreviewAsync(card, e.Movie, cts.Token);
    }

    private void DetailsScrollViewer_ViewChanged(object sender,
        ScrollViewerViewChangedEventArgs e) => HideRecommendationPreview();

    private void HideRecommendationPreview()
    {
        try { _hoverRecommendationCts?.Cancel(); } catch { }
        _hoverRecommendationCts = null;
        _hoverRecommendationId = null;
        _hoverRecommendationCard = null;
        RecommendationPreviewOverlay.Visibility = Visibility.Collapsed;
    }

    private bool PreviewStillCurrent(MovieCard card, MovieSimple movie,
        CancellationToken ct) =>
        !ct.IsCancellationRequested &&
        ReferenceEquals(_hoverRecommendationCard, card) &&
        _hoverRecommendationId == movie.Id && card.Movie?.Id == movie.Id;

    private void PositionRecommendationPreview(MovieCard card)
    {
        var viewportWidth = PreviewLayer.ActualWidth;
        var viewportHeight = PreviewLayer.ActualHeight;
        if (viewportWidth < 200 || viewportHeight < 220)
        {
            RecommendationPreviewOverlay.Visibility = Visibility.Collapsed;
            return;
        }
        var overlay = RecommendationPreviewOverlay;
        overlay.Width = Math.Min(334, viewportWidth - 28);
        overlay.MaxHeight = viewportHeight - 22;
        PreviewDescription.MaxLines = viewportHeight < 560 ? 2 : 5;
        PreviewCast.MaxLines = viewportHeight < 560 ? 1 : 3;

        var relative = card.TransformToVisual(PreviewLayer)
            .TransformPoint(new Windows.Foundation.Point(0, 0));
        overlay.Measure(new Windows.Foundation.Size(overlay.Width,
            overlay.MaxHeight));
        var height = Math.Min(overlay.DesiredSize.Height, overlay.MaxHeight);
        const double gap = 14;
        var rightX = relative.X + card.ActualWidth + gap;
        var leftX = relative.X - overlay.Width - gap;
        var x = rightX + overlay.Width <= viewportWidth - 12
            ? rightX : leftX >= 12 ? leftX
            : Math.Max(12, (viewportWidth - overlay.Width) / 2);
        var y = Math.Max(12, Math.Min(relative.Y - 4,
            viewportHeight - height - 12));

        Canvas.SetLeft(overlay, x);
        Canvas.SetTop(overlay, y);
    }

    private void StyleRecommendationPreview()
    {
        // Exactly the same neutral/colorful semantic palette as movie metadata.
        var (rBg, rFg) = BadgeBrushes(MetadataTone.Rating);
        var (iBg, iFg) = BadgeBrushes(MetadataTone.TimeMedium);
        var (kBg, kFg) = BadgeBrushes(MetadataTone.TimeLong);
        PreviewRezkaBadge.Background = rBg;
        PreviewImdbBadge.Background = iBg;
        PreviewKpBadge.Background = kBg;
        PreviewRezkaValue.Foreground = rFg;
        PreviewImdbValue.Foreground = iFg;
        PreviewKpValue.Foreground = kFg;
    }

    private void RenderPreviewGenres(IReadOnlyList<MovieGenre>? genres)
    {
        PreviewGenreChips.Children.Clear();
        if (genres is not { Count: > 0 })
        {
            PreviewGenresSection.Visibility = Visibility.Collapsed;
            return;
        }

        PreviewGenresSection.Visibility = Visibility.Visible;
        var rows = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        // Text-only semantics. In neutral mode all genres are muted; in
        // colorful mode colors match the existing metadata palette.
        var palette = GenreColors.Palette(
            genres.Select(x => x.Name).ToArray(), IsLightTheme, IsColorful);
        for (var index = 0; index < Math.Min(4, genres.Count); index++)
        {
            if (index > 0 && index % 2 == 0)
            {
                PreviewGenreChips.Children.Add(rows);
                rows = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
            }
            var fg = palette[index].Fg;
            rows.Children.Add(new TextBlock
            {
                Text = genres[index].Name,
                Foreground = fg,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                MaxWidth = 150,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        if (rows.Children.Count > 0)
            PreviewGenreChips.Children.Add(rows);
    }

    private void UpdateRecommendationPreview(MovieDetailed details)
    {
        PreviewTitle.Text = details.Name;
        PreviewSubtitle.Text = details.OriginalName ?? "";
        var r = details.SiteRating?.Value;
        var imdb = details.ImdbRating?.Value;
        var kp = details.KpRating?.Value;
        PreviewRezkaBadge.Visibility = r != null ? Visibility.Visible : Visibility.Collapsed;
        PreviewImdbBadge.Visibility = imdb != null ? Visibility.Visible : Visibility.Collapsed;
        PreviewKpBadge.Visibility = kp != null ? Visibility.Visible : Visibility.Collapsed;
        PreviewRezkaValue.Text = r?.ToString("0.0",
            System.Globalization.CultureInfo.CurrentCulture) ?? "";
        PreviewImdbValue.Text = imdb?.ToString("0.0",
            System.Globalization.CultureInfo.CurrentCulture) ?? "";
        PreviewKpValue.Text = kp?.ToString("0.0",
            System.Globalization.CultureInfo.CurrentCulture) ?? "";
        PreviewDescription.Text = details.Description ?? "";
        RenderPreviewGenres(details.Genres);
        PreviewDirector.Text = details.Producers is { Count: > 0 }
            ? Loc.Get("Details.Director") + ": " +
                string.Join(", ", details.Producers.Take(3).Select(x => x.Name))
            : "";
        PreviewCast.Text = details.Actors is { Count: > 0 }
            ? Loc.Get("Details.Actors") + ": " +
                string.Join(", ", details.Actors.Take(5).Select(x => x.Name))
            : "";
        StyleRecommendationPreview();
        if (r is { } value)
        {
            PreviewRezkaBadge.Background = RezkaScoreColors.Gradient(value);
            PreviewRezkaValue.Foreground = Brush(255, 255, 255);
        }
    }

    private async Task ShowRecommendationPreviewAsync(MovieCard card,
        MovieSimple movie, CancellationToken ct)
    {
        try
        {
            // Local catalog data are drawn first, independently of network.
            await Task.Delay(145, ct);
            if (!PreviewStillCurrent(card, movie, ct)) return;
            PreviewTitle.Text = movie.Name ?? "";
            PreviewSubtitle.Text = movie.Details ?? "";
            PreviewRezkaBadge.Visibility = movie.Rating != null
                ? Visibility.Visible : Visibility.Collapsed;
            PreviewImdbBadge.Visibility = Visibility.Collapsed;
            PreviewKpBadge.Visibility = Visibility.Collapsed;
            PreviewRezkaValue.Text = movie.Rating?.ToString("0.0",
                System.Globalization.CultureInfo.CurrentCulture) ?? "";
            PreviewDescription.Text = "";
            RenderPreviewGenres(null);
            PreviewDirector.Text = "";
            PreviewCast.Text = "";
            StyleRecommendationPreview();
            if (movie.Rating is { } localScore)
            {
                PreviewRezkaBadge.Background = RezkaScoreColors.Gradient(localScore);
                PreviewRezkaValue.Foreground = Brush(255, 255, 255);
            }
            RecommendationPreviewOverlay.Visibility = Visibility.Visible;
            PositionRecommendationPreview(card);

            if (_recommendationPreviewCache.TryGetValue(movie.Id, out var cached))
            {
                UpdateRecommendationPreview(cached);
                card.SetRatingFromDetails(cached.SiteRating?.Value);
                PositionRecommendationPreview(card);
                return;
            }

            await Task.Delay(250, ct);
            if (!PreviewStillCurrent(card, movie, ct)) return;
            var details = await RezkaService.Instance.Client.GetDetailsAsync(
                movie.Id, ct);
            if (!PreviewStillCurrent(card, movie, ct)) return;

            if (_recommendationPreviewCache.Count >= 20)
                _recommendationPreviewCache.Remove(
                    _recommendationPreviewCache.Keys.First());
            _recommendationPreviewCache[movie.Id] = details;
            UpdateRecommendationPreview(details);
            card.SetRatingFromDetails(details.SiteRating?.Value);
            PositionRecommendationPreview(card);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Hover data are optional; never take down a movie page for them.
            Log.Info("[Details] Recommendation preview: " + ex.Message);
        }
    }

    private void RenderWatchAlso(MovieDetailed details)
    {
        _watchAlsoItems = details.WatchAlso ?? [];
        _watchAlsoOffset = 0;
        _lastWatchAlsoWidth = -1;
        _lastWatchAlsoOffset = -1;
        WatchAlsoGrid.ItemsSource = null;
        WatchAlsoPanel.Visibility = _watchAlsoItems.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
        if (_watchAlsoItems.Count > 0) QueueWatchAlsoLayout();
    }

    private void WatchAlsoPanel_SizeChanged(object sender, SizeChangedEventArgs e)
        => QueueWatchAlsoLayout();

    /// <summary>SizeChanged fires during measure; update list data on the next UI tick.</summary>
    private void QueueWatchAlsoLayout()
    {
        if (_watchAlsoRefreshQueued) return;
        _watchAlsoRefreshQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            _watchAlsoRefreshQueued = false;
            try { UpdateWatchAlsoCards(); }
            catch (Exception ex)
            {
                Log.Error("[Details] Failed to build recommendations; hiding section", ex);
                WatchAlsoPanel.Visibility = Visibility.Collapsed;
            }
        }))
        {
            _watchAlsoRefreshQueued = false;
        }
    }

    private void UpdateWatchAlsoCards(bool force = false)
    {
        if (_watchAlsoItems.Count == 0 || WatchAlsoPanel.Visibility != Visibility.Visible)
            return;

        // Reserve room for the left/right arrows and their column gaps.
        // On a typical desktop width five titles are visible; with nine
        // recommendations the second page naturally contains four.
        var available = WatchAlsoPanel.ActualWidth - 36;
        if (!double.IsFinite(available) || available < 130) return;
        var contentWidth = Math.Max(70, available - 84);
        var pageSize = contentWidth >= 780 ? 5
            : contentWidth >= 625 ? 4
            : contentWidth >= 475 ? 3
            : contentWidth >= 320 ? 2 : 1;
        if (pageSize != _watchAlsoPageSize)
        {
            _watchAlsoOffset = (_watchAlsoOffset / pageSize) * pageSize;
            _watchAlsoPageSize = pageSize;
        }
        _watchAlsoOffset = Math.Clamp(_watchAlsoOffset, 0,
            ((_watchAlsoItems.Count - 1) / pageSize) * pageSize);

        if (!force && Math.Abs(available - _lastWatchAlsoWidth) < 2 &&
            _lastWatchAlsoOffset == _watchAlsoOffset)
            return;
        _lastWatchAlsoWidth = available;
        _lastWatchAlsoOffset = _watchAlsoOffset;

        var visible = _watchAlsoItems.Skip(_watchAlsoOffset).Take(pageSize).ToArray();
        // 24 DIP between posters; scale recommendation cards only, not
        // cards in the main catalog. Avoid the oversized 230-DIP posters.
        var slotWidth = (contentWidth - 24 * pageSize) / pageSize;
        var cardWidth = Math.Clamp(slotWidth, 90, 158);

        // Apply width to ListView containers, not to manually constructed
        // MovieCard controls. Viewbox scales the existing MovieCard template.
        // No in-layout visual-tree destruction or unbounded items.
        var itemStyle = new Style(typeof(ListViewItem));
        itemStyle.Setters.Add(new Setter(FrameworkElement.WidthProperty, cardWidth));
        itemStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty,
            new Thickness(0, 0, 24, 0)));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        itemStyle.Setters.Add(new Setter(Control.MinWidthProperty, 0d));
        itemStyle.Setters.Add(new Setter(Control.MinHeightProperty, 0d));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,
            HorizontalAlignment.Stretch));
        itemStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty,
            VerticalAlignment.Top));
        itemStyle.Setters.Add(new Setter(Control.IsTabStopProperty, false));

        WatchAlsoGrid.ItemContainerStyle = itemStyle;
        WatchAlsoGrid.Width = Math.Min(contentWidth, (cardWidth + 24) * visible.Length);
        WatchAlsoGrid.ItemsSource = visible;
        WatchAlsoPreviousButton.IsEnabled = _watchAlsoOffset > 0;
        WatchAlsoNextButton.IsEnabled = _watchAlsoOffset + pageSize < _watchAlsoItems.Count;
        WatchAlsoPageText.Text =
            $"{_watchAlsoOffset / pageSize + 1} / {(_watchAlsoItems.Count + pageSize - 1) / pageSize}";
    }

    private void WatchAlsoPreviousButton_Click(object sender, RoutedEventArgs e)
    {
        _watchAlsoOffset = Math.Max(0, _watchAlsoOffset - _watchAlsoPageSize);
        QueueWatchAlsoLayout();
    }

    private void WatchAlsoNextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_watchAlsoOffset + _watchAlsoPageSize >= _watchAlsoItems.Count) return;
        _watchAlsoOffset += _watchAlsoPageSize;
        QueueWatchAlsoLayout();
    }

    private void UpdateVoicesRatingButton(MovieDetailed details)
    {
        VoicesRatingButton.Visibility = details.VoiceActingRatings is { Count: > 0 }
            ? Visibility.Visible
            : Visibility.Collapsed;
        ToolTipService.SetToolTip(VoicesRatingButton, Loc.Get("Details.Ratings"));
    }

    // The title page keeps the original concise five-line schedule.
    // The full release timetable is opened in the app-level modal instead.
    private void RenderSchedule(MovieDetailed details)
    {
        ScheduleList.Children.Clear();
        var groups = details.Schedule;
        if (groups == null || groups.Count == 0 || groups.All(g => g.Items.Count == 0))
        {
            ScheduleCard.Visibility = Visibility.Collapsed;
            return;
        }

        ScheduleCard.Visibility = Visibility.Visible;
        ScheduleHeader.Text = Loc.Get("Details.Schedule");

        var next = Parsers.FindNextRelease(groups, DateTime.Today);
        if (next != null)
        {
            var days = (next.Value.Date - DateTime.Today).Days;
            ScheduleCountdownText.Text = Loc.Get("Details.NextRelease",
                next.Value.Date.ToString("d MMMM yyyy")) +
                " (" + Loc.Get("Details.DaysLeft", days) + ")";
            ScheduleCountdownBadge.Visibility = Visibility.Visible;
        }
        else
        {
            ScheduleCountdownBadge.Visibility = Visibility.Collapsed;
        }

        var first = groups.First(g => g.Items.Count > 0);
        ScheduleGroupText.Text = first.Name;
        foreach (var item in first.Items.Take(5))
            ScheduleList.Children.Add(BuildCompactScheduleRow(item));

        var count = groups.Sum(g => g.Items.Count);
        ScheduleMoreButton.Visibility = count > 5 || groups.Count(g => g.Items.Count > 0) > 1
            ? Visibility.Visible : Visibility.Collapsed;
        ScheduleMoreButton.Content = Loc.Get("Details.ScheduleShowAll") + $" ({count})";
    }

    private UIElement BuildCompactScheduleRow(SeriesScheduleItem item)
    {
        var row = new Grid
        {
            ColumnSpacing = 10,
            Padding = new Thickness(0, 7, 0, 7)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition
            { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Spacing = 3 };
        left.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(item.RussianName)
                ? item.Title : item.RussianName,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(item.OriginalName))
            left.Children.Add(new TextBlock
            {
                Text = item.OriginalName, FontSize = 11,
                Foreground = Brush(IsLightTheme ? (byte)100 : (byte)173,
                    IsLightTheme ? (byte)105 : (byte)177,
                    IsLightTheme ? (byte)112 : (byte)184),
                TextWrapping = TextWrapping.Wrap
            });
        row.Children.Add(left);
        var right = new StackPanel
        {
            Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right
        };
        right.Children.Add(new TextBlock
        {
            Text = item.ReleaseDate, FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = Brush(IsLightTheme ? (byte)105 : (byte)196,
                IsLightTheme ? (byte)110 : (byte)200,
                IsLightTheme ? (byte)116 : (byte)206)
        });
        right.Children.Add(new TextBlock
        {
            Text = item.Title, FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = Brush(IsLightTheme ? (byte)124 : (byte)150,
                IsLightTheme ? (byte)129 : (byte)155,
                IsLightTheme ? (byte)135 : (byte)165),
            MaxWidth = 190, TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(right, 1);
        row.Children.Add(right);
        return row;
    }

    private async void ScheduleMoreButton_Click(object sender, RoutedEventArgs e)
    {
        var groups = _details?.Schedule?.Where(x => x.Items.Count > 0).ToList();
        if (groups == null || groups.Count == 0 || App.MainWindow is not { } window)
            return;

        var foreground = Brush(IsLightTheme ? (byte)35 : (byte)242,
            IsLightTheme ? (byte)39 : (byte)243,
            IsLightTheme ? (byte)45 : (byte)247);
        var muted = Brush(IsLightTheme ? (byte)102 : (byte)180,
            IsLightTheme ? (byte)110 : (byte)188,
            IsLightTheme ? (byte)121 : (byte)198);
        var tile = Brush(IsLightTheme ? (byte)236 : (byte)44,
            IsLightTheme ? (byte)240 : (byte)47,
            IsLightTheme ? (byte)244 : (byte)54);
        var surface = Brush(IsLightTheme ? (byte)248 : (byte)37,
            IsLightTheme ? (byte)249 : (byte)40,
            IsLightTheme ? (byte)252 : (byte)46);
        var timeline = new StackPanel { Spacing = 14 };
        var total = groups.Sum(x => x.Items.Count);
        timeline.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16, 14, 16, 14),
            Background = tile,
            Child = new StackPanel
            {
                Spacing = 5,
                Children =
                {
                    new TextBlock
                    {
                        Text = _details?.Name ?? "",
                        FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = foreground, TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = Loc.Get("Details.ScheduleEntries") + ": " + total,
                        FontSize = 12, Foreground = muted
                    }
                }
            }
        });

        var next = Parsers.FindNextRelease(groups, DateTime.Today);
        if (next != null)
            timeline.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(13, 9, 13, 9),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = Brush(IsLightTheme ? (byte)210 : (byte)29,
                    IsLightTheme ? (byte)237 : (byte)70,
                    IsLightTheme ? (byte)230 : (byte)63),
                Child = new TextBlock
                {
                    Text = Loc.Get("Details.NextRelease",
                        next.Value.Date.ToString("d MMMM yyyy")),
                    Foreground = foreground, FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                }
            });

        foreach (var group in groups)
        {
            var groupCard = new StackPanel { Spacing = 8 };
            groupCard.Children.Add(new TextBlock
            {
                Text = group.Name,
                FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = foreground,
                Margin = new Thickness(0, 5, 0, 4),
                TextWrapping = TextWrapping.Wrap
            });
            for (var i = 0; i < group.Items.Count; i++)
            {
                var item = group.Items[i];
                var row = new Grid { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition
                    { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var number = new Border
                {
                    Width = 32, Height = 32,
                    CornerRadius = new CornerRadius(11),
                    Background = Brush(IsLightTheme ? (byte)221 : (byte)52,
                        IsLightTheme ? (byte)234 : (byte)84,
                        IsLightTheme ? (byte)231 : (byte)79),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = (i + 1).ToString(),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = foreground
                    }
                };
                row.Children.Add(number);
                var names = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
                names.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(item.RussianName)
                        ? item.Title : item.RussianName,
                    FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = foreground, TextWrapping = TextWrapping.Wrap
                });
                if (!string.IsNullOrWhiteSpace(item.OriginalName))
                    names.Children.Add(new TextBlock
                    {
                        Text = item.OriginalName,
                        FontSize = 11, Foreground = muted, TextWrapping = TextWrapping.Wrap
                    });
                Grid.SetColumn(names, 1);
                row.Children.Add(names);
                var when = new TextBlock
                {
                    Text = item.ReleaseDate, FontSize = 12,
                    Foreground = muted, TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 145, VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    TextAlignment = TextAlignment.Right
                };
                Grid.SetColumn(when, 2);
                row.Children.Add(when);
                groupCard.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(11),
                    Padding = new Thickness(12, 11, 12, 11),
                    Background = surface,
                    Child = row
                });
            }
            timeline.Children.Add(groupCard);
        }

        var scroll = new ScrollViewer
        {
            Content = timeline,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollMode = ScrollMode.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        await window.ShowCenteredModalAsync(Loc.Get("Details.Schedule"), scroll, 740);
    }

    private async void VoicesRatingButton_Click(object sender, RoutedEventArgs e)
    {
        var ratings = _details?.VoiceActingRatings;
        if (ratings == null || ratings.Count == 0 || App.MainWindow is not { } window)
            return;

        // The scroll view stretches to the fixed-width modal in MainWindow.
        // 22 DIP right inset keeps percentages clear of the scrollbar.
        var list = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(0, 0, 22, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var sorted = ratings.Where(r => float.IsFinite(r.Percent))
            .OrderByDescending(r => r.Percent).ToArray();

        foreach (var (rating, index) in sorted.Select((r, i) => (r, i)))
        {
            var row = new StackPanel
            {
                Spacing = 5,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var header = new Grid { ColumnSpacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });

            var name = new TextBlock
            {
                Text = rating.Name,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 2,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = index == 0
                    ? Microsoft.UI.Text.FontWeights.SemiBold
                    : Microsoft.UI.Text.FontWeights.Normal
            };
            ToolTipService.SetToolTip(name, rating.Name);
            Grid.SetColumn(name, 0);
            header.Children.Add(name);

            var percentage = new TextBlock
            {
                Text = rating.Percent.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + " %",
                TextAlignment = TextAlignment.Right,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            Grid.SetColumn(percentage, 1);
            header.Children.Add(percentage);

            row.Children.Add(header);
            row.Children.Add(new ProgressBar
            {
                Height = 4,
                Minimum = 0,
                Maximum = 100,
                Value = Math.Clamp(rating.Percent, 0, 100),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsHitTestVisible = false
            });
            list.Children.Add(row);
        }

        var scroll = new ScrollViewer
        {
            Content = list,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden
        };
        await window.ShowCenteredModalAsync(Loc.Get("Details.Ratings"), scroll);
    }

    private int MetaLabelWidth => _isNarrow ? 88 : 110;

    private enum MetadataTone
    {
        Neutral, Country, GenreBlue, GenreGreen, GenrePurple, GenreRose,
        TimeShort, TimeMedium, TimeLong, People, Rating, Age
    }

    private bool IsColorful => SettingsService.Instance.DetailPalette == DetailsPalette.Colorful;
    private bool IsLightTheme => ActualTheme == ElementTheme.Light;

    private Microsoft.UI.Xaml.Media.SolidColorBrush Brush(byte r, byte g, byte b) =>
        new(Windows.UI.Color.FromArgb(255, r, g, b));

    /// <summary>
    /// One semantic palette for both light and dark themes. Colorful mode
    /// touches only badges, not system accent/backgrounds or action buttons.
    /// </summary>
    private (Microsoft.UI.Xaml.Media.SolidColorBrush Bg, Microsoft.UI.Xaml.Media.SolidColorBrush Fg)
        BadgeBrushes(MetadataTone tone)
    {
        if (IsColorful && SettingsService.Instance.Theme == AppTheme.Manual &&
            tone is MetadataTone.People or MetadataTone.Rating)
        {
            // The manually selected pair gives a coherent tint to the page's
            // major badges while leaving genre and age semantics intact.
            var accent = tone == MetadataTone.Rating
                ? ThemeHelper.AccentBrush.Color : ThemeHelper.CompanionBrush.Color;
            var surface = IsLightTheme
                ? Windows.UI.Color.FromArgb(255, 249, 249, 251)
                : Windows.UI.Color.FromArgb(255, 37, 38, 41);
            var blend = IsLightTheme ? 0.16 : 0.28;
            byte Mix(byte x, byte y) => (byte)Math.Clamp(Math.Round(x * blend + y * (1 - blend)), 0, 255);
            return (Brush(Mix(accent.R, surface.R), Mix(accent.G, surface.G), Mix(accent.B, surface.B)),
                IsLightTheme ? Brush(35, 38, 44) : Brush(242, 244, 248));
        }
        if (!IsColorful)
            return IsLightTheme
                ? (Brush(238, 239, 241), Brush(42, 43, 45))
                : (Brush(51, 53, 56), Brush(242, 242, 242));

        var color = tone switch
        {
            MetadataTone.Country => IsLightTheme ? (222, 236, 251, 28, 58, 88) : (40, 67, 92, 223, 239, 255),
            MetadataTone.GenreBlue => IsLightTheme ? (222, 236, 251, 25, 67, 100) : (38, 62, 88, 218, 237, 255),
            MetadataTone.GenreGreen => IsLightTheme ? (220, 245, 229, 25, 85, 49) : (36, 76, 58, 220, 255, 231),
            MetadataTone.GenrePurple => IsLightTheme ? (237, 225, 251, 81, 42, 118) : (72, 50, 90, 238, 226, 255),
            MetadataTone.GenreRose => IsLightTheme ? (251, 227, 231, 126, 38, 64) : (86, 49, 63, 255, 226, 232),
            MetadataTone.TimeShort => IsLightTheme ? (220, 245, 229, 25, 85, 49) : (36, 76, 58, 220, 255, 231),
            MetadataTone.TimeMedium => IsLightTheme ? (252, 240, 209, 109, 66, 15) : (80, 66, 43, 255, 239, 201),
            MetadataTone.TimeLong => IsLightTheme ? (253, 224, 221, 132, 43, 38) : (89, 48, 45, 255, 225, 221),
            MetadataTone.Age => IsLightTheme ? (253, 224, 221, 132, 43, 38) : (89, 48, 45, 255, 225, 221),
            MetadataTone.People => IsLightTheme ? (229, 232, 242, 48, 56, 93) : (52, 58, 76, 226, 233, 250),
            MetadataTone.Rating => IsLightTheme ? (251, 239, 216, 97, 66, 21) : (79, 66, 45, 255, 240, 214),
            _ => IsLightTheme ? (235, 238, 244, 36, 49, 78) : (50, 57, 70, 235, 241, 251)
        };
        return (Brush((byte)color.Item1, (byte)color.Item2, (byte)color.Item3),
                Brush((byte)color.Item4, (byte)color.Item5, (byte)color.Item6));
    }

    private Border Chip(string text, MetadataTone tone)
    {
        var (background, foreground) = BadgeBrushes(tone);
        var badge = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 6, 10, 6),
            Background = background,
            BorderThickness = new Thickness(0),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = foreground,
                MaxWidth = 210,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        ToolTipService.SetToolTip(badge, text);
        return badge;
    }

    /// <summary>
    /// Low-saturation flag-color gradients keep country labels readable even
    /// against dark backgrounds. The flag icon is retained for recognition.
    /// </summary>
    private void ApplyCountryTint(Border badge, string name)
    {
        if (!IsColorful) return;
        var flag = CountryFlag(name);
        var dark = !IsLightTheme;
        (Windows.UI.Color A, Windows.UI.Color B, Windows.UI.Color C) colors = flag switch
        {
            "🇬🇷" => dark
                ? (Windows.UI.Color.FromArgb(255, 35, 66, 106), Windows.UI.Color.FromArgb(255, 76, 86, 102),
                    Windows.UI.Color.FromArgb(255, 35, 66, 106))
                : (Windows.UI.Color.FromArgb(255, 182, 210, 248), Windows.UI.Color.FromArgb(255, 247, 249, 253),
                    Windows.UI.Color.FromArgb(255, 182, 210, 248)),
            "🇨🇾" => dark
                ? (Windows.UI.Color.FromArgb(255, 66, 69, 72), Windows.UI.Color.FromArgb(255, 117, 80, 52),
                    Windows.UI.Color.FromArgb(255, 46, 88, 66))
                : (Windows.UI.Color.FromArgb(255, 247, 248, 251), Windows.UI.Color.FromArgb(255, 243, 208, 165),
                    Windows.UI.Color.FromArgb(255, 207, 235, 214)),
            // Japan is white and red, NEVER black and red in dark mode.
            "🇯🇵" => dark
                ? (Windows.UI.Color.FromArgb(255, 247, 247, 249), Windows.UI.Color.FromArgb(255, 242, 177, 187),
                    Windows.UI.Color.FromArgb(255, 247, 247, 249))
                : (Windows.UI.Color.FromArgb(255, 255, 255, 255), Windows.UI.Color.FromArgb(255, 248, 200, 208),
                    Windows.UI.Color.FromArgb(255, 255, 255, 255)),
            "🇺🇦" => dark
                ? (Windows.UI.Color.FromArgb(255, 40, 65, 95), Windows.UI.Color.FromArgb(255, 105, 88, 44),
                    Windows.UI.Color.FromArgb(255, 105, 88, 44))
                : (Windows.UI.Color.FromArgb(255, 213, 232, 252), Windows.UI.Color.FromArgb(255, 249, 237, 177),
                    Windows.UI.Color.FromArgb(255, 249, 237, 177)),
            "🇺🇸" or "🇷🇺" or "🇬🇧" or "🇫🇷" => dark
                ? (Windows.UI.Color.FromArgb(255, 44, 58, 97), Windows.UI.Color.FromArgb(255, 75, 64, 81),
                    Windows.UI.Color.FromArgb(255, 112, 52, 65))
                : (Windows.UI.Color.FromArgb(255, 212, 231, 252), Windows.UI.Color.FromArgb(255, 245, 246, 248),
                    Windows.UI.Color.FromArgb(255, 250, 217, 222)),
            "🇩🇪" => dark
                ? (Windows.UI.Color.FromArgb(255, 50, 51, 55), Windows.UI.Color.FromArgb(255, 99, 52, 58),
                    Windows.UI.Color.FromArgb(255, 103, 83, 49))
                : (Windows.UI.Color.FromArgb(255, 220, 222, 227), Windows.UI.Color.FromArgb(255, 252, 220, 224),
                    Windows.UI.Color.FromArgb(255, 252, 239, 204)),
            "🇨🇳" => dark
                ? (Windows.UI.Color.FromArgb(255, 91, 48, 51), Windows.UI.Color.FromArgb(255, 119, 73, 55),
                    Windows.UI.Color.FromArgb(255, 91, 48, 51))
                : (Windows.UI.Color.FromArgb(255, 250, 220, 222), Windows.UI.Color.FromArgb(255, 252, 232, 193),
                    Windows.UI.Color.FromArgb(255, 250, 220, 222)),
            _ => dark
                ? (Windows.UI.Color.FromArgb(255, 50, 64, 77), Windows.UI.Color.FromArgb(255, 57, 70, 78),
                    Windows.UI.Color.FromArgb(255, 63, 58, 78))
                : (Windows.UI.Color.FromArgb(255, 224, 236, 249), Windows.UI.Color.FromArgb(255, 229, 241, 240),
                    Windows.UI.Color.FromArgb(255, 237, 230, 248))
        };
        var gradient = new Microsoft.UI.Xaml.Media.LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, .5),
            EndPoint = new Windows.Foundation.Point(1, .5),
        };
        // Duplicate stop offsets form three crisp flag-coloured bands.
        foreach (var (offset, color) in new[]
        {
            (0.0, colors.A), (1.0 / 3, colors.A),
            (1.0 / 3, colors.B), (2.0 / 3, colors.B),
            (2.0 / 3, colors.C), (1.0, colors.C)
        })
            gradient.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop
            {
                Offset = offset, Color = color
            });
        badge.Background = gradient;
        if (badge.Child is StackPanel content)
            foreach (var label in content.Children.OfType<TextBlock>())
                label.Foreground = flag == "🇯🇵" ? Brush(33, 37, 44)
                    : dark ? Brush(246, 246, 248) : Brush(33, 37, 44);
    }

    private void AddMetaRow(string label, FrameworkElement value, bool alignLabelTop = false)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MetaLabelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 12,
            VerticalAlignment = alignLabelTop ? VerticalAlignment.Top : VerticalAlignment.Center,
            Margin = alignLabelTop ? new Thickness(0, 7, 0, 0) : new Thickness(0),
            Foreground = IsLightTheme ? Brush(91, 96, 104) : Brush(191, 194, 199)
        });
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        MetaPanel.Children.Add(grid);
    }

    /// <summary>
    /// Flow chips into short rows sized against the available page width.
    /// No external toolkit/WrapPanel, no horizontal overflow for long genres.
    /// </summary>
    private StackPanel WrapChips(
        IEnumerable<(string Name, MetadataTone Tone)> items, bool genres = false)
    {
        var sources = items.ToArray();
        var genreColors = genres
            ? GenreColors.Palette(sources.Select(x => x.Name).ToArray(),
                IsLightTheme, IsColorful) : null;
        var genreIndex = 0;
        var stack = new StackPanel { Spacing = 6 };
        var pageWidth = ActualWidth > 0 ? ActualWidth : 1100;
        var leftRight = pageWidth < 510 ? 24 : pageWidth < 850 ? 40 : 56;
        var available = pageWidth - leftRight - MetaLabelWidth - 32;
        if (!_isNarrow)
            available -= PosterPanel.Width + HeaderGrid.ColumnSpacing;
        available = Math.Max(130, available);

        StackPanel line = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        double used = 0;
        foreach (var (name, tone) in sources)
        {
            var estimated = Math.Min(230, 22 + name.Length * 7.2);
            if (line.Children.Count > 0 && used + 6 + estimated > available)
            {
                stack.Children.Add(line);
                line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                used = 0;
            }
            var badge = tone == MetadataTone.Country
                ? CountryChip(name) : Chip(name, tone);
            if (genreColors != null)
            {
                badge.Background = genreColors[genreIndex].Bg;
                if (badge.Child is TextBlock label)
                    label.Foreground = genreColors[genreIndex].Fg;
                genreIndex++;
            }
            if (tone == MetadataTone.Country) ApplyCountryTint(badge, name);
            badge.MaxWidth = Math.Max(124, Math.Min(220, available));
            line.Children.Add(badge);
            used += (used > 0 ? 6 : 0) + Math.Min(estimated, badge.MaxWidth);
        }
        if (line.Children.Count > 0) stack.Children.Add(line);
        return stack;
    }

    private static string CountryFlag(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("греци") || n.Contains("греці") || n.Contains("greece")) return "🇬🇷";
        if (n.Contains("кипр") || n.Contains("кіпр") || n.Contains("cyprus")) return "🇨🇾";
        if (n.Contains("япон") || n.Contains("japan")) return "🇯🇵";
        if (n.Contains("сша") || n.Contains("usa") || n.Contains("united states")) return "🇺🇸";
        if (n.Contains("росси") || n.Contains("russia") || n.Contains("росі")) return "🇷🇺";
        if (n.Contains("корея") || n.Contains("korea")) return "🇰🇷";
        if (n.Contains("китай") || n.Contains("china")) return "🇨🇳";
        if (n.Contains("великобрит") || n.Contains("united kingdom") || n.Contains("британ")) return "🇬🇧";
        if (n.Contains("франц") || n.Contains("france")) return "🇫🇷";
        if (n.Contains("герман") || n.Contains("germany") || n.Contains("німеч")) return "🇩🇪";
        if (n.Contains("укра") || n.Contains("ukrain")) return "🇺🇦";
        if (n.Contains("итал") || n.Contains("italy") || n.Contains("італ")) return "🇮🇹";
        if (n.Contains("испан") || n.Contains("spain") || n.Contains("іспан")) return "🇪🇸";
        if (n.Contains("польш") || n.Contains("poland") || n.Contains("польщ")) return "🇵🇱";
        return "🌐";
    }

    private Border CountryChip(string country)
    {
        var badge = Chip(country, MetadataTone.Country);
        var label = (TextBlock)badge.Child;
        badge.Child = null;
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 7,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(FlagVisual(CountryFlag(country)));
        content.Children.Add(label);
        badge.Child = content;
        return badge;
    }

    /// <summary>Lightweight native-vector flags: unlike emoji, reliable in WinUI 3.</summary>
    private Border FlagVisual(string flag)
    {
        var canvas = new Canvas { Width = 24, Height = 16, Background = Brush(255, 255, 255) };
        static Windows.UI.Color C(byte r, byte g, byte b) => Windows.UI.Color.FromArgb(255, r, g, b);
        var white = C(255, 255, 255);
        var red = C(205, 39, 53);
        var blue = C(25, 88, 163);
        void Rect(double x, double y, double w, double h, Windows.UI.Color color)
        {
            var rect = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = w, Height = h,
                Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(color)
            };
            Canvas.SetLeft(rect, x); Canvas.SetTop(rect, y); canvas.Children.Add(rect);
        }
        void H(params Windows.UI.Color[] stripes)
        {
            for (var i = 0; i < stripes.Length; i++)
                Rect(0, i * 16.0 / stripes.Length, 24, 16.0 / stripes.Length + .05, stripes[i]);
        }
        void V(params Windows.UI.Color[] stripes)
        {
            for (var i = 0; i < stripes.Length; i++)
                Rect(i * 24.0 / stripes.Length, 0, 24.0 / stripes.Length + .05, 16, stripes[i]);
        }
        switch (flag)
        {
            case "🇬🇷":
                for (var i = 0; i < 9; i++) Rect(0, i * 16.0 / 9, 24, 16.0 / 9 + .05,
                    i % 2 == 0 ? C(13, 94, 175) : white);
                Rect(0, 0, 10.5, 8.9, C(13, 94, 175));
                Rect(4.2, 0, 2.1, 8.9, white);
                Rect(0, 3.4, 10.5, 2.1, white);
                break;
            case "🇨🇾":
                // Cyprus: copper-coloured island over two olive branches.
                var island = new Microsoft.UI.Xaml.Shapes.Polygon
                {
                    Points = new Microsoft.UI.Xaml.Media.PointCollection
                    {
                        new Windows.Foundation.Point(2, 7), new Windows.Foundation.Point(6, 5),
                        new Windows.Foundation.Point(11, 6), new Windows.Foundation.Point(16, 4),
                        new Windows.Foundation.Point(22, 7), new Windows.Foundation.Point(17, 9),
                        new Windows.Foundation.Point(12, 11), new Windows.Foundation.Point(5, 10)
                    },
                    Fill = Brush(194, 123, 61)
                };
                canvas.Children.Add(island);
                Rect(6.5, 12, 5, 1.3, C(51, 133, 69));
                Rect(12.5, 12, 5, 1.3, C(51, 133, 69));
                break;
            case "🇯🇵":
                // Explicit opaque white geometry: the surrounding tinted chip
                // must never show through the Japanese flag.
                Rect(0, 0, 24, 16, white);
                var disc = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 8, Height = 8, Fill = Brush(185, 37, 52) };
                Canvas.SetLeft(disc, 8); Canvas.SetTop(disc, 4); canvas.Children.Add(disc); break;
            case "🇺🇦": H(C(0, 91, 187), C(255, 213, 0)); break;
            case "🇷🇺": H(white, C(0, 57, 166), red); break;
            case "🇩🇪": H(C(22, 22, 22), C(205, 21, 36), C(255, 206, 0)); break;
            case "🇫🇷": V(C(0, 85, 164), white, C(239, 65, 53)); break;
            case "🇮🇹": V(C(0, 146, 70), white, red); break;
            case "🇵🇱": H(white, red); break;
            case "🇪🇸": H(red, C(255, 196, 0), red); break;
            case "🇺🇸":
                for (var i = 0; i < 13; i++) Rect(0, i * 16.0 / 13, 24, 16.0 / 13 + .05, i % 2 == 0 ? red : white);
                Rect(0, 0, 10, 8.7, C(25, 52, 116)); break;
            case "🇬🇧":
                Rect(0, 0, 24, 16, C(1, 33, 106));
                Rect(10, 0, 4, 16, white); Rect(0, 6, 24, 4, white);
                Rect(11, 0, 2, 16, red); Rect(0, 7, 24, 2, red); break;
            case "🇨🇳":
                Rect(0, 0, 24, 16, C(222, 41, 16));
                var star = new TextBlock { Text = "★", FontSize = 8, Foreground = Brush(255, 222, 0) };
                Canvas.SetLeft(star, 2); Canvas.SetTop(star, 0); canvas.Children.Add(star); break;
            case "🇰🇷":
                var symbol = new Microsoft.UI.Xaml.Shapes.Ellipse
                { Width = 9, Height = 9, Fill = Brush(204, 38, 56) };
                Canvas.SetLeft(symbol, 7.5); Canvas.SetTop(symbol, 3.5);
                canvas.Children.Add(symbol); break;
            default:
                var globe = new TextBlock { Text = "🌐", FontSize = 12 };
                Canvas.SetLeft(globe, 4); Canvas.SetTop(globe, 0);
                canvas.Children.Add(globe); break;
        }
        return new Border
        {
            Width = 25, Height = 17, Child = canvas,
            Background = Brush(255, 255, 255),
            BorderBrush = Brush(117, 120, 128),
            BorderThickness = new Thickness(.5), CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
        };
    }

    private void BuildMeta(MovieDetailed details)
    {
        MetaPanel.Children.Clear();
        // The site already supplies ReleaseDate. Display it immediately after
        // country chips without an extra "Date" heading or an extra request.
        var countryDateChips = new List<(string Name, MetadataTone Tone)>();
        if (details.Countries is { Count: > 0 })
            countryDateChips.AddRange(details.Countries.Select(c => (c.Name, MetadataTone.Country)));
        if (!string.IsNullOrWhiteSpace(details.ReleaseDate))
            countryDateChips.Add((details.ReleaseDate.Trim(), MetadataTone.Neutral));
        if (countryDateChips.Count > 0)
            AddMetaRow(details.Countries is { Count: > 0 }
                ? Loc.Get("Details.Country") : "", WrapChips(countryDateChips));

        if (details.Genres is { Count: > 0 })
        {
            AddMetaRow(Loc.Get("Details.Genre"),
                WrapChips(details.Genres.Select(g =>
                    (g.Name, MetadataTone.GenreBlue)), genres: true));
        }

        if (details.Duration is { } mins)
        {
            var minutes = int.TryParse(mins.ToString(), out var parsed) ? parsed : 0;
            var tone = minutes is > 0 and <= 30 ? MetadataTone.TimeShort
                : minutes is > 30 and <= 90 ? MetadataTone.TimeMedium : MetadataTone.TimeLong;
            var (bg, fg) = BadgeBrushes(tone);
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };
            content.Children.Add(new FontIcon
            {
                Glyph = "\uE823", FontSize = 13, Foreground = fg,
                VerticalAlignment = VerticalAlignment.Center
            });
            content.Children.Add(new TextBlock
            {
                Text = mins + " " + Loc.Get("Common.Minutes"),
                FontSize = 12, Foreground = fg, VerticalAlignment = VerticalAlignment.Center
            });
            AddMetaRow(Loc.Get("Details.Duration"), new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 6, 10, 6),
                Background = bg,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = content
            });
        }

        if (details.Producers is { Count: > 0 })
            AddPersonsMeta(Loc.Get("Details.Director"), details.Producers);
        if (details.Actors is { Count: > 0 })
            AddPersonsMeta(Loc.Get("Details.Actors"), details.Actors);
        if (details.Year != null)
            AddMetaRow(Loc.Get("Details.Year"), WrapChips([(details.Year, MetadataTone.Neutral)]));
        if (details.Slogan is { Length: > 0 })
            AddMeta(Loc.Get("Details.Slogan"), details.Slogan);
    }

    private void UpdateAgeBadgeStyle()
    {
        var age = AgeRatingText.Text;
        var prefix = new string(age.TakeWhile(char.IsDigit).ToArray());
        var tone = int.TryParse(prefix, out var number) ? number switch
        {
            <= 6 => MetadataTone.TimeShort,
            <= 12 => MetadataTone.GenreBlue,
            <= 16 => MetadataTone.TimeMedium,
            _ => MetadataTone.Age
        } : MetadataTone.Neutral;
        var (background, foreground) = BadgeBrushes(tone);
        AgeRatingBorder.Background = background;
        AgeRatingText.Foreground = foreground;
    }

    private void RefreshPresentation()
    {
        if (_details is not { } details) return;
        RatingsPanel.Children.Clear();
        AddRating("Rezka", details.SiteRating);
        AddRating("IMDb", details.ImdbRating);
        AddRating("KP", details.KpRating);
        BuildMeta(details);
        UpdateAgeBadgeStyle();
        UserRatingBorder.Background = BadgeBrushes(MetadataTone.Rating).Bg;
    }

    private void AddRating(string label, MovieRating? rating)
    {
        if (rating?.Value == null) return;
        var border = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 7, 9, 7),
            Background = label == "Rezka"
                ? RezkaScoreColors.Gradient(rating.Value.Value)
                : BadgeBrushes(MetadataTone.Rating).Bg,
        };
        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        var logo = label switch
        {
            "IMDb" => "IMDb",
            "KP" => "КП",
            _ => "R"
        };
        var logoBg = label switch
        {
            "IMDb" => Brush(242, 198, 35),
            "KP" => Brush(245, 129, 49),
            _ => Brush(70, 73, 78)
        };
        var logoFg = label == "Rezka" ? Brush(255, 255, 255) : Brush(25, 25, 25);
        stack.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(5),
            Background = logoBg,
            Padding = new Thickness(5, 2, 5, 2),
            Child = new TextBlock
            {
                Text = logo,
                Foreground = logoFg,
                FontSize = label == "IMDb" ? 11 : 12,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        stack.Children.Add(new TextBlock
        {
            Text = rating.Value.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = label == "Rezka" ? Brush(255, 255, 255)
                : BadgeBrushes(MetadataTone.Rating).Fg
        });
        if (rating.Votes != null)
            stack.Children.Add(new TextBlock
            {
                Text = "(" + rating.Votes + ")",
                FontSize = 11,
                Foreground = label == "Rezka" ? Brush(255, 255, 255)
                    : BadgeBrushes(MetadataTone.Rating).Fg,
                Opacity = .85,
                VerticalAlignment = VerticalAlignment.Center
            });

        border.Child = stack;
        RatingsPanel.Children.Add(border);
    }

    private readonly List<Button> _rateButtons = new();
    private TextBlock? _rateThanksText;

    private void BuildRateRow()
    {
        _rateButtons.Clear();
        _rateThanksText = null;
        UserRatingPanel.Children.Clear();
        UserRatingPanel.Visibility = Visibility.Visible;

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };

        for (var i = 1; i <= 10; i++)
        {
            var star = new Button
            {
                Content = new FontIcon { Glyph = "\uE734", FontSize = 15 },
                Tag = i,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(3, 2, 3, 2),
                MinWidth = 0,
                MinHeight = 0,
            };
            star.Click += RateButton_Click;
            ToolTipService.SetToolTip(star, i.ToString());
            _rateButtons.Add(star);
            row.Children.Add(star);
        }

        PaintRateRow(null);

        var wrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        wrap.Children.Add(row);
        _rateThanksText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Style = Application.Current.Resources["SecondaryText"] as Style,
            Visibility = Visibility.Collapsed,
        };
        wrap.Children.Add(_rateThanksText);
        UserRatingPanel.Children.Add(wrap);
    }

    private void PaintRateRow(int? selected)
    {
        var activeStyle = Application.Current.Resources["StarActiveIcon"] as Style;
        var idleStyle = Application.Current.Resources["StarIdleIcon"] as Style;
        for (var i = 0; i < _rateButtons.Count; i++)
        {
            var button = _rateButtons[i];
            button.IsEnabled = selected == null;
            if (button.Content is FontIcon icon)
            {
                icon.Glyph = selected != null && i < selected ? "\uE735" : "\uE734";
                icon.Style = selected != null && i < selected ? activeStyle : idleStyle;
            }
        }
    }

    private async void RateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_details == null) return;
        if ((sender as Button)?.Tag is not int value) return;

        if (!RezkaService.Instance.IsLoggedIn)
        {
            await ShowLoginAsync();
            return;
        }

        var numericId = ExtractNumericId(_details.Id);
        if (numericId == null) return;

        foreach (var b in _rateButtons) b.IsEnabled = false;
        try
        {
            await RezkaService.Instance.Client.RateAsync(numericId, value);
            PaintRateRow(value);
            if (_rateThanksText != null)
            {
                _rateThanksText.Text = Loc.Get("Details.RateThanks");
                _rateThanksText.Visibility = Visibility.Visible;
            }
        }
        catch (RezkaException ex)
        {
            await ShowErrorAsync(RezkaService.Instance.ErrorText(ex));
            PaintRateRow(null);
        }
    }

    private (string MovieId, string TranslatorId, string? SeasonId, string? EpisodeId)? WatchedKey()
    {
        if (_details == null || _voice == null) return null;
        var movieId = ExtractNumericId(_details.Id) ?? _details.Id;
        return (movieId, _voice.TranslatorId, _season?.SeasonId, _episode?.EpisodeId);
    }

    // Episode watch-state now lives on each episode chip's context action;
    // the existing selection-dependent downloads still refresh here.
    private void UpdateWatchedButton() => UpdateDownloadVisibility();

    private void UpdateTrackButton()
    {
        var tracked = _details != null && TrackedSeriesService.IsTracked(_details.Id);
        TrackButton.IsEnabled = _details != null;
        TrackButton.IsChecked = tracked;
    }

    private void TrackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_details == null) return;
        var now = TrackedSeriesService.ToggleTracked(_details.Id, _details.Name, _details.Poster);
        TrackButton.IsChecked = now;
    }

    private async void TrailerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_details == null) return;
        var query = $"{_details.OriginalName ?? _details.Name} {(_details.Year ?? "").Trim()} трейлер".Trim();
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(
                new Uri("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(query)));
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }
    }

    private void AddPersonsMeta(string label, IReadOnlyList<PersonSimple> persons)
    {
        // Each person is an independently clickable pill. This preserves
        // navigation to PersonPage while preventing unbounded wall-of-text.
        var available = ActualWidth > 0 ? ActualWidth : 1024;
        available -= (available < 510 ? 24 : available < 850 ? 40 : 56) + MetaLabelWidth + 32;
        if (!_isNarrow) available -= PosterPanel.Width + HeaderGrid.ColumnSpacing;
        available = Math.Max(145, available);

        var wrap = new StackPanel { Spacing = 6 };
        StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        double used = 0;

        void Append(Button pill, double width)
        {
            width = Math.Min(width, Math.Min(220, available));
            if (row.Children.Count > 0 && used + width + 6 > available)
            {
                wrap.Children.Add(row);
                row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                used = 0;
            }
            pill.MaxWidth = Math.Max(100, width);
            row.Children.Add(pill);
            used += width + (row.Children.Count > 1 ? 6 : 0);
        }

        var isCast = _details?.Actors != null && ReferenceEquals(persons, _details.Actors);
        var visible = _peopleExpanded || !isCast ? persons.Count : Math.Min(6, persons.Count);
        var (bg, fg) = BadgeBrushes(MetadataTone.People);
        for (var i = 0; i < visible; i++)
        {
            var person = persons[i];
            var pill = new Button
            {
                Content = new TextBlock
                {
                    Text = person.Name,
                    MaxWidth = 200,
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = fg
                },
                Background = bg,
                Foreground = fg,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(9, 5, 9, 5),
                MinWidth = 0,
                MinHeight = 28,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            ToolTipService.SetToolTip(pill, person.Name);
            pill.Click += (_, _) => Nav.Go<PersonPage>(person);
            Append(pill, 20 + person.Name.Length * 7);
        }

        if (isCast && persons.Count > 6)
        {
            var caption = _peopleExpanded ? Loc.Get("Details.ShowLess") :
                "+" + (persons.Count - visible) + " " + Loc.Get("Details.MorePeople");
            var more = new Button
            {
                Content = caption,
                Background = bg, Foreground = ThemeHelper.AccentTextBrush,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(9, 5, 9, 5),
                MinWidth = 0, MinHeight = 28,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            more.Click += (_, _) =>
            {
                _peopleExpanded = !_peopleExpanded;
                if (_details != null) BuildMeta(_details);
            };
            Append(more, 25 + caption.Length * 7);
        }
        if (row.Children.Count > 0) wrap.Children.Add(row);
        // For multi-line cast the heading belongs next to the first row.
        AddMetaRow(label, wrap, alignLabelTop: isCast);
    }

    private void AddMeta(string label, string value)
    {
        AddMetaRow(label, WrapChips([(value, MetadataTone.Neutral)]));
    }

    private async void VoiceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VoiceBox.SelectedItem is not ComboBoxItem { Tag: MovieVoiceActing voice } ||
            _details == null || ReferenceEquals(_voice, voice)) return;

        _voice = voice;
        SeasonBox.IsEnabled = false;
        try
        {
            // Each voiceover may expose a different set of seasons. Preserve
            // the original translator-specific refresh and stale-result guard.
            await RefreshSeasonsAsync(voice, resetSelection: true, _loadCts?.Token ?? default);
        }
        finally
        {
            SeasonBox.IsEnabled = true;
        }

        UpdateWatchedButton();
        UpdateSeasonDownloadVisibility();
    }

    /// <summary>
    /// Перезапрашивает список сезонов для конкретной озвучки.
    /// HTML-версия остаётся запасным вариантом (фильм, ошибка сети, пустой ответ).
    /// </summary>
    private async Task RefreshSeasonsAsync(MovieVoiceActing? voice, bool resetSelection, CancellationToken ct = default)
    {
        if (_details == null || voice == null) return;

        var likelySeries = _details.Seasons is { Count: > 0 } || _details.Schedule != null || voice.Url != null;
        var numericId = ExtractNumericId(_details.Id);

        if (!likelySeries || numericId == null)
        {
            if (!resetSelection) return;
            _seasons = _details.Seasons;
            ApplySeasonSelection(reset: true);
            RenderSeasons();
            return;
        }

        var generation = ++_seasonsGeneration;

        IReadOnlyList<MovieSeason>? seasons = null;
        try
        {
            seasons = await RezkaService.Instance.Client.GetSeriesSeasonsAsync(numericId, voice, _details.Favs, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Log.Info($"[Details] seasons translator={voice.TranslatorId} failed: {ex.Message}");
        }

        // Пользователь успел переключить другую озвучку — этот ответ устарел.
        if (generation != _seasonsGeneration) return;

        if (seasons is { Count: > 0 })
        {
            // Стартовая синхронизация: если HTML уже дал тот же набор,
            // не перерисовываем список (иначе слетает выбранная серия).
            if (!resetSelection && SameSeasonSet(seasons, _seasons)) return;
            _seasons = seasons;
        }
        else if (resetSelection)
        {
            _seasons = _details.Seasons;
        }
        else
        {
            return;
        }

        ApplySeasonSelection(resetSelection);
        RenderSeasons();
        UpdateWatchedButton();
        UpdateSeasonDownloadVisibility();
    }

    private static bool SameSeasonSet(IReadOnlyList<MovieSeason>? a, IReadOnlyList<MovieSeason>? b)
    {
        if (a == null || b == null) return false;
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i].SeasonId != b[i].SeasonId) return false;
            if (a[i].Episodes.Count != b[i].Episodes.Count) return false;
        }

        return true;
    }

    /// <summary>Сохраняет текущий сезон/серию, если они есть в новом наборе.</summary>
    private void ApplySeasonSelection(bool reset)
    {
        if (_seasons is not { Count: > 0 })
        {
            _season = null;
            _episode = null;
            return;
        }

        var previousSeasonId = reset ? null : _season?.SeasonId;
        var previousEpisodeId = reset ? null : _episode?.EpisodeId;

        var season = (previousSeasonId == null ? null : _seasons.FirstOrDefault(s => s.SeasonId == previousSeasonId))
            ?? _seasons.FirstOrDefault(s => s.IsSelected)
            ?? _seasons[0];

        var episode = (previousEpisodeId == null ? null : season.Episodes.FirstOrDefault(ep => ep.EpisodeId == previousEpisodeId))
            ?? season.Episodes.FirstOrDefault(ep => ep.IsSelected)
            ?? season.Episodes.FirstOrDefault();

        _season = season;
        _episode = episode;
    }

    private void RenderSeasons()
    {
        var hasSeasons = _seasons is { Count: > 0 };
        SeasonsPanel.Visibility = hasSeasons ? Visibility.Visible : Visibility.Collapsed;
        PlaybackChoiceBorder.Visibility = hasSeasons || VoicesPanel.Visibility == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
        EpisodesPanel.Visibility = hasSeasons && _season?.Episodes.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
        ArrangeSelectionControls();
        if (!hasSeasons)
        {
            EpisodesList.Items.Clear();
            return;
        }

        SeasonBox.SelectionChanged -= SeasonBox_SelectionChanged;
        SeasonBox.Items.Clear();
        foreach (var season in _seasons!)
        {
            SeasonBox.Items.Add(new ComboBoxItem
            {
                Content = $"{Loc.Get("Common.Season")} {season.Name}",
                Tag = season,
            });
        }

        var selectedIdx = 0;
        var i = 0;
        foreach (var season in _seasons)
        {
            if (season.SeasonId == _season?.SeasonId) selectedIdx = i;
            i++;
        }

        SeasonBox.SelectedIndex = selectedIdx;
        SeasonBox.SelectionChanged += SeasonBox_SelectionChanged;
        RenderEpisodes();
    }

    private void SeasonBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SeasonBox.SelectedItem is ComboBoxItem { Tag: MovieSeason season })
        {
            _season = season;
            _episode = season.Episodes.FirstOrDefault();
            RenderEpisodes();
            UpdateWatchedButton();
            UpdateSeasonDownloadVisibility();
        }
    }

    private async void SeasonDownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (!DonorUnlock.IsUnlocked)
        {
            await DownloadDialog.ShowLockedAsync(Content.XamlRoot);
            return;
        }

        if (DownloadService.HasActive)
        {
            await DownloadDialog.ShowActiveAsync(Content.XamlRoot);
            return;
        }

        if (_details == null || _voice == null || _season == null) return;
        var quality = await DownloadDialog.PickQualityAsync(
            Content.XamlRoot, SettingsService.Instance.DefaultQuality);
        if (quality == null) return;
        var jobs = DownloadJobs.Season(_details, _voice, _season, quality).ToList();
        await DownloadDialog.RunAsync(Content.XamlRoot, jobs);
    }

    /// <summary>Season download exists only for unlocked users.</summary>
    private void UpdateSeasonDownloadVisibility()
    {
        SeasonDownloadButton.Visibility = DonorUnlock.IsUnlocked && _season != null
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateDownloadVisibility();
    }

    /// <summary>Single download (film or selected episode) for unlocked users.</summary>
    private void UpdateDownloadVisibility()
    {
        DownloadButton.Visibility = DonorUnlock.IsUnlocked && _details != null && _voice != null
            ? Visibility.Visible
            : Visibility.Collapsed;
        ActionsRow.Visibility = DownloadButton.Visibility;
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (!DonorUnlock.IsUnlocked)
        {
            await DownloadDialog.ShowLockedAsync(Content.XamlRoot);
            return;
        }

        if (_details == null || _voice == null) return;

        if (DownloadService.HasActive)
        {
            await DownloadDialog.ShowActiveAsync(Content.XamlRoot);
            return;
        }

        var quality = await DownloadDialog.PickQualityAsync(
            Content.XamlRoot, SettingsService.Instance.DefaultQuality);
        if (quality == null) return;
        var job = DownloadJobs.Episode(_details, _voice, _season, _episode, quality);
        await DownloadDialog.RunAsync(Content.XamlRoot, [job]);
    }

    private void RenderEpisodes()
    {
        EpisodesList.Items.Clear();
        if (_season == null) return;
        var movieId = _details != null ? ExtractNumericId(_details.Id) ?? _details.Id : null;
        var translator = _voice?.TranslatorId;
        for (var episodeIndex = 0; episodeIndex < _season.Episodes.Count; episodeIndex++)
        {
            var episode = _season.Episodes[episodeIndex];
            var watched = movieId != null && translator != null &&
                PositionService.Instance.IsWatched(movieId, translator, _season.SeasonId, episode.EpisodeId);
            var label = episode.Name;
            // Long descriptive episode titles remain in the tooltip while the
            // chip shows the ordinal, avoiding 50 wide buttons on one row.
            if (label.Length > 4)
                label = (episodeIndex + 1).ToString();
            var button = new ToggleButton
            {
                Content = watched ? "✓" + label : label,
                Tag = episode,
                Width = 40,
                Height = 32,
                Padding = new Thickness(1),
                Margin = new Thickness(3, 2, 3, 2),
                MinWidth = 0,
                MinHeight = 0,
                IsChecked = episode.EpisodeId == _episode?.EpisodeId,
            };
            ToolTipService.SetToolTip(button,
                episode.Name + "\n" + Loc.Get("Details.EpisodeRightClick"));
            button.Click += EpisodeButton_Click;
            button.RightTapped += EpisodeButton_RightTapped;
            EpisodesList.Items.Add(button);
        }
    }

    private void EpisodeButton_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not ToggleButton { Tag: MovieEpisode episode } ||
            _details == null || _voice == null || _season == null) return;

        var movieId = ExtractNumericId(_details.Id) ?? _details.Id;
        var scrollOffset = DetailsScrollViewer.VerticalOffset;
        var nowWatched = PositionService.Instance.ToggleWatched(movieId, _voice.TranslatorId,
            _season.SeasonId, episode.EpisodeId);
        TrackedSeriesService.Touch(_details.Id, _details.Name, _details.Poster);
        // Never clear/rebuild the ItemsControl after a right click: replacing
        // a focused element made the outer ScrollViewer jump to the top.
        if (sender is ToggleButton button)
        {
            var originalLabel = button.Content?.ToString() ?? episode.Name;
            if (originalLabel.StartsWith("✓", StringComparison.Ordinal))
                originalLabel = originalLabel[1..];
            button.Content = nowWatched ? "✓" + originalLabel : originalLabel;
        }
        DispatcherQueue.TryEnqueue(() =>
        {
            if (Math.Abs(DetailsScrollViewer.VerticalOffset - scrollOffset) > 1)
                DetailsScrollViewer.ChangeView(null, scrollOffset, null, true);
        });
    }

    private void EpisodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not MovieEpisode episode) return;
        _episode = episode;
        foreach (var item in EpisodesList.Items.OfType<ToggleButton>())
        {
            item.IsChecked = item == button;
        }

        UpdateWatchedButton();
    }

    private void WatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_details == null || _voice == null) return;

        try
        {
            // Player lives in a separate window: the main window layout
            // (custom title bar, drag rectangles) stays untouched.
            var playerWindow = new PlayerWindow();
            playerWindow.ShowPlayer(new PlayerLaunch(_details, _voice, _seasons, _season, _episode));
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }
    }

    private async void BookmarkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_details == null) return;

        if (!RezkaService.Instance.IsLoggedIn)
        {
            await ShowLoginAsync();
            return;
        }

        var numericId = ExtractNumericId(_details.Id);
        if (numericId == null) return;

        try
        {
            var categories = await RezkaService.Instance.Client.GetBookmarkCategoriesAsync();
            var menu = new MenuFlyout();
            foreach (var category in categories)
            {
                var item = new MenuFlyoutItem
                {
                    Text = $"{category.Name} ({category.Count})",
                    Tag = category.Id,
                };
                if (category.Id == _bookmarkedCategoryId)
                {
                    item.Icon = new FontIcon { Glyph = "\uE73E" };
                }

                item.Click += async (_, _) => await SetBookmarkAsync(numericId, category.Id);
                menu.Items.Add(item);
            }

            if (_bookmarkedCategoryId is int bookmarked)
            {
                menu.Items.Add(new MenuFlyoutSeparator());
                var remove = new MenuFlyoutItem
                {
                    Text = Loc.Get("Bookmarks.Remove"),
                    Tag = bookmarked,
                    Icon = new SymbolIcon(Symbol.Delete),
                };
                remove.Click += async (_, _) => await RemoveBookmarkAsync(numericId, bookmarked);
                menu.Items.Add(remove);
            }

            menu.ShowAt(BookmarkButton, new Windows.Foundation.Point());
        }
        catch (RezkaException ex)
        {
            await ShowErrorAsync(RezkaService.Instance.ErrorText(ex));
        }
    }

    private async Task SetBookmarkAsync(string numericId, int categoryId)
    {
        try
        {
            await RezkaService.Instance.Client.AddToBookmarksAsync(numericId, categoryId);
            _bookmarkedCategoryId = categoryId;
            UpdateBookmarkLabel();
        }
        catch (RezkaException ex)
        {
            await ShowErrorAsync(RezkaService.Instance.ErrorText(ex));
        }
    }

    private async Task RemoveBookmarkAsync(string numericId, int categoryId)
    {
        try
        {
            await RezkaService.Instance.Client.RemoveFromBookmarksAsync([numericId], categoryId);
            _bookmarkedCategoryId = null;
            UpdateBookmarkLabel();
        }
        catch (RezkaException ex)
        {
            await ShowErrorAsync(RezkaService.Instance.ErrorText(ex));
        }
    }

    private void UpdateBookmarkLabel()
    {
        BookmarkText.Text = _bookmarkedCategoryId != null
            ? Loc.Get("Common.RemoveFromBookmarks")
            : Loc.Get("Common.AddToBookmarks");
    }

    private async Task ShowLoginAsync()
    {
        var dialog = new LoginDialog { XamlRoot = Content.XamlRoot };
        await dialog.ShowAsync();
    }

    private async Task ShowErrorAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = Loc.Get("Common.Error"),
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    // ---------- comments ----------

    private void UpdateCommentComposer()
    {
        if (CommentInput == null || CommentSendButton == null) return;
        var characters = CommentInput.Text?.Length ?? 0;
        CommentCharCount.Text = $"{characters} / 2000";
        CommentSendButton.IsEnabled = !_commentSending &&
            !string.IsNullOrWhiteSpace(CommentInput.Text);
        CommentReplyRow.Visibility = _replyTo == null
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private void CommentInput_TextChanged(object sender, TextChangedEventArgs e)
        => UpdateCommentComposer();

    private void CommentEmoji_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string emoji }) return;
        var text = CommentInput.Text ?? "";
        if (text.Length + emoji.Length > 2000) return;
        CommentInput.SelectedText = emoji;
        CommentInput.Focus(FocusState.Programmatic);
        UpdateCommentComposer();
    }

    private void CommentCancelReplyButton_Click(object sender, RoutedEventArgs e)
    {
        _replyTo = null;
        CommentInput.PlaceholderText = Loc.Get("Comments.Placeholder");
        UpdateCommentComposer();
    }

    private void CommentsSortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadedComments.Count > 0) RenderLoadedComments();
    }

    private void RenderLoadedComments()
    {
        var all = _loadedComments.Select((comment, index) => (comment, index));
        var sorted = CommentsSortBox.SelectedIndex switch
        {
            1 => all.OrderByDescending(x => x.comment.Likes).ThenBy(x => x.index),
            2 => all.OrderByDescending(x => x.index),
            3 => all.OrderBy(x => x.comment.Author, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.index),
            _ => all.OrderBy(x => x.index)
        };
        CommentsList.Children.Clear();
        if (_loadedComments.Count == 0 && !_commentsLoading)
        {
            CommentsList.Children.Add(new TextBlock
            {
                Text = Loc.Get("Comments.Empty"),
                FontSize = 13,
                Foreground = Brush(IsLightTheme ? (byte)94 : (byte)180,
                    IsLightTheme ? (byte)98 : (byte)184,
                    IsLightTheme ? (byte)103 : (byte)190),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 14, 0, 14)
            });
            return;
        }
        foreach (var (comment, _) in sorted)
            CommentsList.Children.Add(BuildComment(comment, 0));
    }

    private async void CommentsToggle_Click(object sender, RoutedEventArgs e)
    {
        var expanded = CommentsToggle.IsChecked == true;
        CommentsPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        CommentsCard.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        CommentsToggleText.Text = expanded
            ? $"{Loc.Get("Details.HideComments")} ({_details?.CommentsCount ?? 0})"
            : $"{Loc.Get("Details.ViewComments")} ({_details?.CommentsCount ?? 0})";
        if (expanded && !_commentsLoaded)
            await LoadCommentsAsync();
    }

    private async Task LoadCommentsAsync()
    {
        if (_details == null || _commentsLoading) return;
        var numericId = ExtractNumericId(_details.Id);
        if (numericId == null) return;
        var titleId = _details.Id;
        var page = _commentsPage;
        _commentsLoading = true;
        CommentsMoreButton.IsEnabled = false;
        try
        {
            var comments = await RezkaService.Instance.Client.GetCommentsAsync(numericId, page);
            if (_details?.Id != titleId) return;
            if (page == 1) _loadedComments.Clear();
            _loadedComments.AddRange(comments);
            _commentsLoaded = true;
            CommentsMoreButton.Visibility = comments.Count > 0
                ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (RezkaException ex)
        {
            _commentsPage = Math.Max(1, page - (page > 1 ? 1 : 0));
            CommentStatusText.Text = RezkaService.Instance.ErrorText(ex);
            CommentStatusText.Visibility = Visibility.Visible;
            Log.Error("[Comments] Loading failed", ex);
        }
        catch (Exception ex)
        {
            _commentsPage = Math.Max(1, page - (page > 1 ? 1 : 0));
            CommentStatusText.Text = Loc.Get("Comments.LoadFailed");
            CommentStatusText.Visibility = Visibility.Visible;
            Log.Error("[Comments] Loading failed", ex);
        }
        finally
        {
            _commentsLoading = false;
            CommentsMoreButton.IsEnabled = true;
            if (_details?.Id == titleId) RenderLoadedComments();
        }
    }

    private UIElement BuildComment(CommentItem comment, int depth)
    {
        var fg = Brush(IsLightTheme ? (byte)35 : (byte)240,
            IsLightTheme ? (byte)39 : (byte)242, IsLightTheme ? (byte)44 : (byte)246);
        var subdued = Brush(IsLightTheme ? (byte)105 : (byte)174,
            IsLightTheme ? (byte)110 : (byte)181, IsLightTheme ? (byte)117 : (byte)190);
        var container = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(Math.Min(2, depth) * 18, 0, 0, 0),
            Background = Brush(IsLightTheme ? (byte)243 : (byte)45,
                IsLightTheme ? (byte)244 : (byte)47, IsLightTheme ? (byte)247 : (byte)51),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var layout = new StackPanel { Spacing = 9 };
        var top = new Grid { ColumnSpacing = 10 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var avatar = new Border
        {
            Background = Brush(IsLightTheme ? (byte)212 : (byte)57,
                IsLightTheme ? (byte)226 : (byte)70, IsLightTheme ? (byte)230 : (byte)77),
            CornerRadius = new CornerRadius(18),
            Width = 36, Height = 36,
            Child = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(comment.Author) ? "?" : comment.Author[..1].ToUpperInvariant(),
                FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = fg
            }
        };
        top.Children.Add(avatar);
        var heading = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(new TextBlock
        {
            Text = comment.Author,
            FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = fg, TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (comment.IsAdmin)
            heading.Children.Add(new TextBlock
            {
                Text = "★ " + Loc.Get("Comments.Moderator"), FontSize = 11,
                Foreground = ThemeHelper.AccentTextBrush
            });
        Grid.SetColumn(heading, 1);
        top.Children.Add(heading);
        var dateText = new TextBlock
        {
            Text = comment.Date, FontSize = 11,
            Foreground = subdued, VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 150, TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(dateText, 2);
        top.Children.Add(dateText);
        layout.Children.Add(top);

        layout.Children.Add(new TextBlock
        {
            Text = comment.Text, FontSize = 13, TextWrapping = TextWrapping.Wrap,
            Foreground = fg, IsTextSelectionEnabled = true
        });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        var likeButton = new Button
        {
            Content = (comment.IsLiked ? "♥ " : "♡ ") + comment.Likes,
            FontSize = 12, Padding = new Thickness(10, 5, 10, 5),
            MinWidth = 0, BorderThickness = new Thickness(0),
            IsEnabled = !comment.IsLiked,
            Background = Brush(IsLightTheme ? (byte)227 : (byte)63,
                IsLightTheme ? (byte)230 : (byte)65, IsLightTheme ? (byte)235 : (byte)70)
        };
        likeButton.Click += async (_, _) =>
        {
            try
            {
                await RezkaService.Instance.Client.ToggleCommentLikeAsync(comment.Id);
                likeButton.Content = "♥ " + (comment.Likes + 1);
                likeButton.IsEnabled = false;
            }
            catch (Exception ex)
            {
                Log.Error("[Comments] Like failed", ex);
            }
        };
        actions.Children.Add(likeButton);
        var replyButton = new Button
        {
            Content = "↩ " + Loc.Get("Comments.Reply"),
            FontSize = 12, MinWidth = 0, Padding = new Thickness(10, 5, 10, 5),
            Background = Brush(IsLightTheme ? (byte)227 : (byte)63,
                IsLightTheme ? (byte)230 : (byte)65, IsLightTheme ? (byte)235 : (byte)70),
            BorderThickness = new Thickness(0)
        };
        replyButton.Click += (_, _) =>
        {
            _replyTo = comment.Id;
            CommentReplyText.Text = Loc.Get("Comments.ReplyTo", comment.Author);
            CommentInput.PlaceholderText = Loc.Get("Comments.Placeholder");
            UpdateCommentComposer();
            CommentInput.Focus(FocusState.Programmatic);
            CommentInput.Select(CommentInput.Text.Length, 0);
        };
        actions.Children.Add(replyButton);
        layout.Children.Add(actions);
        // Replies remain attached to their parent when the top-level sort changes.
        if (depth < 2 && comment.Replies.Count > 0)
        {
            foreach (var reply in comment.Replies)
                layout.Children.Add(BuildComment(reply, depth + 1));
        }
        container.Child = layout;
        return container;
    }

    private async void CommentsMoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (_commentsLoading) return;
        _commentsPage++;
        await LoadCommentsAsync();
    }

    private async void CommentSendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_details == null || _commentSending) return;
        var text = CommentInput.Text?.Trim() ?? "";
        if (text.Length == 0 || text.Length > 2000) return;

        if (!RezkaService.Instance.IsLoggedIn)
        {
            await ShowLoginAsync();
            return;
        }
        var numericId = ExtractNumericId(_details.Id);
        if (numericId == null) return;
        var titleId = _details.Id;

        _commentSending = true;
        UpdateCommentComposer();
        CommentStatusText.Visibility = Visibility.Collapsed;
        CommentOpenSiteButton.Visibility = Visibility.Collapsed;
        try
        {
            await RezkaService.Instance.Client.SendCommentAsync(
                numericId, text, _replyTo, _details.Adb);
            if (_details?.Id != titleId) return;
            CommentInput.Text = "";
            _replyTo = null;
            UpdateCommentComposer();
            _commentsLoaded = false;
            _commentsPage = 1;
            await LoadCommentsAsync();
            CommentStatusText.Text = Loc.Get("Comments.Sent");
            CommentStatusText.Visibility = Visibility.Visible;
        }
        catch (RezkaException ex)
        {
            Log.Error("[Comments] Site rejected comment", ex);
            CommentStatusText.Text = Loc.Get("Comments.SendFailed") + "\n" +
                (ex.Kind == RezkaError.Site ? ex.Message :
                 RezkaService.Instance.ErrorText(ex));
            CommentStatusText.Visibility = Visibility.Visible;
            CommentOpenSiteButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Log.Error("[Comments] Sending failed", ex);
            CommentStatusText.Text = Loc.Get("Comments.SendFailed") + "\n" +
                Loc.Get("Comments.RetryBrowser");
            CommentStatusText.Visibility = Visibility.Visible;
            CommentOpenSiteButton.Visibility = Visibility.Visible;
        }
        finally
        {
            _commentSending = false;
            UpdateCommentComposer();
        }
    }

    private async void CommentOpenSiteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_details == null) return;
        try
        {
            var draft = CommentInput.Text;
            if (!string.IsNullOrWhiteSpace(draft))
            {
                var data = new DataPackage();
                data.SetText(draft);
                Clipboard.SetContent(data);
            }

            var baseUri = new Uri(RezkaService.Instance.Client.Options.Mirror);
            if (baseUri.Scheme != Uri.UriSchemeHttps)
            {
                CommentStatusText.Text = Loc.Get("Comments.HttpsRequired");
                CommentStatusText.Visibility = Visibility.Visible;
                return;
            }
            var uri = new Uri(baseUri, _details.Id.TrimStart('/'));
            await Windows.System.Launcher.LaunchUriAsync(uri);
            CommentStatusText.Text = Loc.Get("Comments.DraftCopied");
            CommentStatusText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Log.Error("[Comments] Browser fallback failed", ex);
            CommentStatusText.Text = Loc.Get("Comments.RetryBrowser");
            CommentStatusText.Visibility = Visibility.Visible;
        }
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_movie != null) _ = LoadAsync(_movie);
    }

    internal static string? ExtractNumericId(string cleanPath) => Parsers.NumericId(cleanPath);
}
