using HDREZKA.App.Services;
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
    private string? _replyTo;
    private bool _mirrorRecovered;
    private int? _bookmarkedCategoryId;
    private int _seasonsGeneration;
    private CancellationTokenSource? _loadCts;

    public DetailsPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            try { Log.Info("[Details] Loaded"); } catch { }
            UpdateAdaptiveLayout();
        };
        ActualThemeChanged += (_, _) => DispatcherQueue.TryEnqueue(RefreshPresentation);
        SizeChanged += (_, _) => UpdateAdaptiveLayout();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ApplyLocalization();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        SettingsService.Instance.PosterSizeChanged += OnPosterSizeChanged;
        SettingsService.Instance.DetailPaletteChanged += OnDetailPaletteChanged;
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
        // Ушли со страницы: отменяем незавершённые запросы, чтобы не держать
        // семафор клиента и не обновлять уже невидимый UI.
        try { _loadCts?.Cancel(); } catch { }
        try { _loadCts?.Dispose(); } catch { }
        _loadCts = null;
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        SettingsService.Instance.PosterSizeChanged -= OnPosterSizeChanged;
        SettingsService.Instance.DetailPaletteChanged -= OnDetailPaletteChanged;
    }

    private void OnPosterSizeChanged() => DispatcherQueue.TryEnqueue(ApplyPosterMetrics);

    private void OnDetailPaletteChanged() => DispatcherQueue.TryEnqueue(RefreshPresentation);

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
        EpisodesList.MaxWidth = Math.Min(480, Math.Max(168, width - (width < 510 ? 72 : 110)));
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
        WatchText.Text = Loc.Get("Common.Watch");
        UpdateBookmarkLabel();
        WatchedText.Text = Loc.Get("Continue.ToggleWatched");
        TrackText.Text = Loc.Get("Details.Track");
        TrailerText.Text = Loc.Get("Common.Trailer");
        DownloadText.Text = Loc.Get("Downloads.Download");
        SeasonDownloadText.Text = Loc.Get("Downloads.Season");
        if (_details != null) RenderSchedule(_details);
        VoicesHeader.Text = Loc.Get("Common.VoiceActing");
        SeasonsHeader.Text = Loc.Get("Common.Season");
        WatchAlsoHeader.Text = Loc.Get("Details.WatchAlso");
        RetryButton.Content = Loc.Get("Common.Retry");
        CommentInput.PlaceholderText = Loc.Get("Comments.Placeholder");
        CommentSendButton.Content = Loc.Get("Common.Send");
        CommentsMoreButton.Content = Loc.Get("Common.LoadMore");
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
            Render(details);

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
        VoiceBox.SelectionChanged -= VoiceBox_SelectionChanged;
        VoiceBox.Items.Clear();
        if (details.VoiceActings != null)
        {
            foreach (var voice in details.VoiceActings)
            {
                var display = new TextBlock
                {
                    Text = voice.Name + (voice.IsPremium ? " ★" : ""),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 420
                };
                ToolTipService.SetToolTip(display, voice.Name);
                VoiceBox.Items.Add(new ComboBoxItem { Content = display, Tag = voice });
            }
        }

        var voiceIndex = details.VoiceActings?.ToList()
            .FindIndex(voice => voice.TranslatorId == _voice?.TranslatorId) ?? -1;
        VoiceBox.SelectedIndex = voiceIndex;
        VoiceBox.SelectionChanged += VoiceBox_SelectionChanged;

        AgeRatingBorder.Visibility = string.IsNullOrWhiteSpace(details.AgeRestriction)
            ? Visibility.Collapsed : Visibility.Visible;
        AgeRatingText.Text = details.AgeRestriction ?? "";
        UpdateAgeBadgeStyle();

        RenderSeasons();
        UpdateSeasonDownloadVisibility();

        WatchButton.IsEnabled = details.IsAvailable && !details.IsComingSoon && _voice != null;
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

        CommentsSection.Visibility = details.CommentsCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        CommentsToggleText.Text = $"{Loc.Get("Comments.Show")} ({details.CommentsCount})";

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

    private void RenderWatchAlso(MovieDetailed details)
    {
        // Virtualized horizontal GridView (like catalog rows), NOT a manual
        // StackPanel of cards — that pattern once correlated with a native
        // crash on this page. Breadcrumbs will tell if it ever returns.
        var items = details.WatchAlso;
        if (items == null || items.Count == 0)
        {
            WatchAlsoPanel.Visibility = Visibility.Collapsed;
            WatchAlsoGrid.ItemsSource = null;
            return;
        }

        WatchAlsoPanel.Visibility = Visibility.Visible;
        WatchAlsoGrid.ItemsSource = items.ToList();
    }

    private void UpdateVoicesRatingButton(MovieDetailed details)
    {
        VoicesRatingButton.Visibility = details.VoiceActingRatings is { Count: > 0 }
            ? Visibility.Visible
            : Visibility.Collapsed;
        ToolTipService.SetToolTip(VoicesRatingButton, Loc.Get("Details.Ratings"));
    }

    private void RenderSchedule(MovieDetailed details)
    {
        ScheduleList.Children.Clear();
        var groups = details.Schedule;
        if (groups == null || groups.Count == 0 || groups.All(g => g.Items.Count == 0))
        {
            SchedulePanel.Visibility = Visibility.Collapsed;
            return;
        }

        SchedulePanel.Visibility = Visibility.Visible;
        ScheduleHeader.Text = Loc.Get("Details.Schedule");

        var next = Parsers.FindNextRelease(groups, DateTime.Today);
        if (next != null)
        {
            var days = (next.Value.Date - DateTime.Today).Days;
            ScheduleCountdownText.Text = Loc.Get("Details.NextRelease", next.Value.Date.ToString("d MMMM yyyy"))
                + " (" + Loc.Get("Details.DaysLeft", days) + ")";
            ScheduleCountdownBadge.Visibility = Visibility.Visible;
        }
        else
        {
            ScheduleCountdownBadge.Visibility = Visibility.Collapsed;
        }

        var first = groups.First(g => g.Items.Count > 0);
        ScheduleGroupText.Text = first.Name;
        foreach (var item in first.Items.Take(5))
        {
            ScheduleList.Children.Add(BuildScheduleRow(item));
        }

        var total = groups.Sum(g => g.Items.Count);
        if (total > 5)
        {
            ScheduleMoreButton.Content = Loc.Get("Common.ShowAll") + $" ({total})";
            ScheduleMoreButton.Visibility = Visibility.Visible;
        }
        else
        {
            ScheduleMoreButton.Visibility = Visibility.Collapsed;
        }
    }

    private Grid BuildScheduleRow(SeriesScheduleItem item)
    {
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Spacing = 2 };
        left.Children.Add(new TextBlock
        {
            Text = item.RussianName.Length > 0 ? item.RussianName : item.Title,
            TextWrapping = TextWrapping.Wrap,
            Style = Application.Current.Resources["PrimaryText"] as Style,
        });
        if (!string.IsNullOrEmpty(item.OriginalName))
        {
            left.Children.Add(new TextBlock
            {
                Text = item.OriginalName,
                FontSize = 12,
                Style = Application.Current.Resources["SecondaryText"] as Style,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        grid.Children.Add(left);

        var right = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(new TextBlock
        {
            Text = item.ReleaseDate,
            HorizontalAlignment = HorizontalAlignment.Right,
            Style = Application.Current.Resources["SecondaryText"] as Style,
        });
        var title = new TextBlock
        {
            Text = item.Title,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            Style = Application.Current.Resources["SecondaryText"] as Style,
        };
        right.Children.Add(title);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private async void ScheduleMoreButton_Click(object sender, RoutedEventArgs e)
    {
        var groups = _details?.Schedule;
        if (groups == null || groups.Count == 0) return;

        var list = new StackPanel { Spacing = 12, MinWidth = 320 };
        foreach (var group in groups)
        {
            if (group.Items.Count == 0) continue;
            list.Children.Add(new TextBlock { Text = group.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            foreach (var item in group.Items)
            {
                list.Children.Add(BuildScheduleRow(item));
            }
        }

        var scroll = new ScrollViewer { Content = list, MaxHeight = 420 };
        var dialog = new ContentDialog
        {
            Title = Loc.Get("Details.Schedule"),
            Content = scroll,
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot,
        };
        await dialog.ShowAsync();
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
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
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

    private void AddMetaRow(string label, FrameworkElement value)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MetaLabelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
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
    private StackPanel WrapChips(IEnumerable<(string Name, MetadataTone Tone)> items)
    {
        var stack = new StackPanel { Spacing = 6 };
        var pageWidth = ActualWidth > 0 ? ActualWidth : 1100;
        var leftRight = pageWidth < 510 ? 24 : pageWidth < 850 ? 40 : 56;
        var available = pageWidth - leftRight - MetaLabelWidth - 32;
        if (!_isNarrow)
            available -= PosterPanel.Width + HeaderGrid.ColumnSpacing;
        available = Math.Max(130, available);

        StackPanel line = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        double used = 0;
        foreach (var (name, tone) in items)
        {
            var estimated = Math.Min(230, 22 + name.Length * 7.2);
            if (line.Children.Count > 0 && used + 6 + estimated > available)
            {
                stack.Children.Add(line);
                line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                used = 0;
            }
            var badge = Chip(name, tone);
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
        if (n.Contains("япон") || n.Contains("japan") || n.Contains("японі")) return "🇯🇵";
        if (n.Contains("сша") || n.Contains("usa") || n.Contains("united states") ||
            n.Contains("сполучені штати")) return "🇺🇸";
        if (n.Contains("росси") || n.Contains("russia") || n.Contains("росі")) return "🇷🇺";
        if (n.Contains("южная корея") || n.Contains("south korea") || n.Contains("південна корея")) return "🇰🇷";
        if (n.Contains("китай") || n.Contains("china") || n.Contains("кита")) return "🇨🇳";
        if (n.Contains("великобрит") || n.Contains("united kingdom") || n.Contains("британ")) return "🇬🇧";
        if (n.Contains("франц") || n.Contains("france")) return "🇫🇷";
        if (n.Contains("герман") || n.Contains("germany") || n.Contains("німеч")) return "🇩🇪";
        if (n.Contains("укра") || n.Contains("ukrain")) return "🇺🇦";
        return "🌐";
    }

    private void BuildMeta(MovieDetailed details)
    {
        MetaPanel.Children.Clear();
        if (details.Countries is { Count: > 0 })
            AddMetaRow(Loc.Get("Details.Country"),
                WrapChips(details.Countries.Select(c => (CountryFlag(c.Name) + " " + c.Name, MetadataTone.Country))));

        if (details.Genres is { Count: > 0 })
        {
            var tones = new[] { MetadataTone.GenreBlue, MetadataTone.GenreRose,
                MetadataTone.GenreGreen, MetadataTone.GenrePurple };
            AddMetaRow(Loc.Get("Details.Genre"), WrapChips(details.Genres.Select((g, i) =>
                (g.Name, tones[i % tones.Length]))));
        }

        if (details.Duration is { } mins)
        {
            var minutes = int.TryParse(mins.ToString(), out var parsed) ? parsed : 0;
            var tone = minutes is > 0 and <= 30 ? MetadataTone.TimeShort
                : minutes is > 30 and <= 90 ? MetadataTone.TimeMedium : MetadataTone.TimeLong;
            AddMetaRow(Loc.Get("Details.Duration"),
                WrapChips([( "⏱ " + mins + " " + Loc.Get("Common.Minutes"), tone)]));
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
    }

    private void AddRating(string label, MovieRating? rating)
    {
        if (rating?.Value == null) return;
        var border = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 7, 9, 7),
            Background = BadgeBrushes(MetadataTone.Rating).Bg,
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
            Foreground = BadgeBrushes(MetadataTone.Rating).Fg
        });
        if (rating.Votes != null)
            stack.Children.Add(new TextBlock
            {
                Text = "(" + rating.Votes + ")",
                FontSize = 11,
                Foreground = BadgeBrushes(MetadataTone.Rating).Fg,
                Opacity = .8,
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

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new TextBlock
        {
            Text = Loc.Get("Details.YourRating") + ":",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            Style = Application.Current.Resources["SecondaryText"] as Style,
        });

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

    private void UpdateWatchedButton()
    {
        var key = WatchedKey();
        WatchedButton.IsEnabled = key != null;
        WatchedButton.IsChecked = key != null &&
            PositionService.Instance.IsWatched(key.Value.MovieId, key.Value.TranslatorId, key.Value.SeasonId, key.Value.EpisodeId);
        // Selection-dependent download buttons refresh together.
        UpdateDownloadVisibility();
    }

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

    private void WatchedButton_Click(object sender, RoutedEventArgs e)
    {
        var key = WatchedKey();
        if (key == null) return;
        var now = PositionService.Instance.ToggleWatched(key.Value.MovieId, key.Value.TranslatorId, key.Value.SeasonId, key.Value.EpisodeId);
        WatchedButton.IsChecked = now;
        RenderEpisodes();

        // Series only: remember for new-episode notifications.
        if (_details != null && _season != null && _episode != null)
        {
            TrackedSeriesService.Touch(_details.Id, _details.Name, _details.Poster);
        }
    }

    private void AddPersonsMeta(string label, IReadOnlyList<PersonSimple> persons)
    {
        // Keep person navigation intact. A capped inline list avoids turning a
        // large anime cast into dozens of rows; 'more' expands in-place.
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        var visible = _peopleExpanded ? persons.Count : Math.Min(6, persons.Count);
        for (var i = 0; i < visible; i++)
        {
            if (i > 0) text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = ", " });
            var person = persons[i];
            var link = new Microsoft.UI.Xaml.Documents.Hyperlink();
            link.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = person.Name });
            link.Click += (_, _) => Nav.Go<PersonPage>(person);
            text.Inlines.Add(link);
        }
        if (visible < persons.Count)
        {
            text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "  ·  " });
            var more = new Microsoft.UI.Xaml.Documents.Hyperlink();
            more.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text = "+" + (persons.Count - visible) + " " + Loc.Get("Details.MorePeople")
            });
            more.Click += (_, _) => { _peopleExpanded = true; if (_details != null) BuildMeta(_details); };
            text.Inlines.Add(more);
        }

        AddMetaRow(label, new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 7, 10, 7),
            Background = BadgeBrushes(MetadataTone.People).Bg,
            Child = text
        });
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
            ToolTipService.SetToolTip(button, episode.Name);
            button.Click += EpisodeButton_Click;
            EpisodesList.Items.Add(button);
        }
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

    private async void CommentsToggle_Click(object sender, RoutedEventArgs e)
    {
        if (CommentsToggle.IsChecked == true)
        {
            CommentsPanel.Visibility = Visibility.Visible;
            CommentsToggleText.Text = $"{Loc.Get("Comments.Hide")} ({_details?.CommentsCount})";
            if (!_commentsLoaded)
            {
                _commentsLoaded = true;
                await LoadCommentsAsync();
            }
        }
        else
        {
            CommentsPanel.Visibility = Visibility.Collapsed;
            CommentsToggleText.Text = $"{Loc.Get("Comments.Show")} ({_details?.CommentsCount})";
        }
    }

    private async Task LoadCommentsAsync()
    {
        if (_details == null) return;
        var numericId = ExtractNumericId(_details.Id);
        if (numericId == null) return;

        try
        {
            var comments = await RezkaService.Instance.Client.GetCommentsAsync(numericId, _commentsPage);
            foreach (var comment in comments)
            {
                CommentsList.Children.Add(BuildComment(comment, 0));
            }

            CommentsMoreButton.Visibility = comments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (RezkaException ex)
        {
            await ShowErrorAsync(RezkaService.Instance.ErrorText(ex));
        }
    }

    private UIElement BuildComment(CommentItem comment, int depth)
    {
        var border = new Border
        {
            Style = Application.Current.Resources["CommentCardBorder"] as Style,
            Margin = new Thickness(depth * 28, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = 900,
        };

        var stack = new StackPanel { Spacing = 4 };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(new TextBlock
        {
            Text = comment.Author,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        if (comment.IsAdmin)
        {
            header.Children.Add(new Border
            {
                Style = Application.Current.Resources["AdminBadgeBorder"] as Style,
                Child = new TextBlock
                {
                    Text = "★",
                    FontSize = 11,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                },
            });
        }

        header.Children.Add(new TextBlock
        {
            Text = comment.Date,
            FontSize = 11,
            Style = Application.Current.Resources["TertiaryText"] as Style,
        });
        stack.Children.Add(header);

        stack.Children.Add(new TextBlock
        {
            Text = comment.Text,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var likeButton = new Button
        {
            Content = $"👍 {comment.Likes}",
            Tag = comment.Id,
            FontSize = 12,
            Padding = new Thickness(8, 3, 8, 3),
        };
        likeButton.Click += async (_, _) =>
        {
            try
            {
                await RezkaService.Instance.Client.ToggleCommentLikeAsync(comment.Id);
                likeButton.Content = $"👍 {comment.Likes + 1}";
                likeButton.IsEnabled = false;
            }
            catch (RezkaException)
            {
                // ignore like failures silently
            }
        };
        actions.Children.Add(likeButton);

        var replyButton = new HyperlinkButton
        {
            Content = Loc.Get("Comments.Reply"),
            FontSize = 12,
            Padding = new Thickness(0),
            Tag = comment.Id,
        };
        replyButton.Click += (_, _) =>
        {
            _replyTo = comment.Id;
            CommentInput.PlaceholderText = $"{Loc.Get("Comments.Reply")}: {comment.Author}";
            CommentInput.Focus(FocusState.Programmatic);
        };
        actions.Children.Add(replyButton);
        stack.Children.Add(actions);

        border.Child = stack;
        return border;
    }

    private async void CommentsMoreButton_Click(object sender, RoutedEventArgs e)
    {
        _commentsPage++;
        await LoadCommentsAsync();
    }

    private async void CommentSendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_details == null || string.IsNullOrWhiteSpace(CommentInput.Text)) return;

        if (!RezkaService.Instance.IsLoggedIn)
        {
            await ShowLoginAsync();
            return;
        }

        var numericId = ExtractNumericId(_details.Id);
        if (numericId == null) return;

        try
        {
            CommentSendButton.IsEnabled = false;
            await RezkaService.Instance.Client.SendCommentAsync(numericId, CommentInput.Text.Trim(), _replyTo, _details.Adb);
            CommentInput.Text = "";
            _replyTo = null;
            CommentInput.PlaceholderText = Loc.Get("Comments.Placeholder");
            CommentsList.Children.Clear();
            _commentsPage = 1;
            await LoadCommentsAsync();
        }
        catch (RezkaException ex)
        {
            await ShowErrorAsync(RezkaService.Instance.ErrorText(ex));
        }
        finally
        {
            CommentSendButton.IsEnabled = true;
        }
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_movie != null) _ = LoadAsync(_movie);
    }

    internal static string? ExtractNumericId(string cleanPath) => Parsers.NumericId(cleanPath);
}
