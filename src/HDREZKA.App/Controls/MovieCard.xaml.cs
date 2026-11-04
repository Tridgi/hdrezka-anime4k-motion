using HDREZKA.App.Services;
using HDREZKA.App.Views;
using HDREZKA.Core.Api;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace HDREZKA.App.Controls;

public sealed partial class MovieCard : UserControl
{
    public static readonly DependencyProperty MovieProperty = DependencyProperty.Register(
        nameof(Movie), typeof(MovieSimple), typeof(MovieCard), new PropertyMetadata(null, OnMovieChanged));

    public MovieSimple? Movie
    {
        get => (MovieSimple?)GetValue(MovieProperty);
        set => SetValue(MovieProperty, value);
    }

    // Enabled only for DetailsPage recommendations, not catalog MovieCards.
    public static readonly DependencyProperty RecommendationPreviewProperty =
        DependencyProperty.Register(nameof(RecommendationPreview), typeof(bool),
            typeof(MovieCard), new PropertyMetadata(false, (d, _) =>
            {
                if (d is MovieCard card) card.SetupRecommendationPreview();
            }));

    public bool RecommendationPreview
    {
        get => (bool)GetValue(RecommendationPreviewProperty);
        set => SetValue(RecommendationPreviewProperty, value);
    }

    private ToolTip? _previewTip;
    private TextBlock? _previewTitle;
    private TextBlock? _previewSubtitle;
    private TextBlock? _previewRating;
    private TextBlock? _previewDescription;
    private TextBlock? _previewGenres;
    private TextBlock? _previewDirector;
    private TextBlock? _previewCast;
    private string? _previewMovieId;
    private bool _previewLoaded;
    private CancellationTokenSource? _previewCancellation;
    private static readonly object PreviewGate = new();
    private static readonly Dictionary<string, MovieDetailed> PreviewCache = new(StringComparer.OrdinalIgnoreCase);

    public MovieCard()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ApplyPosterSize();
            SettingsService.Instance.PosterSizeChanged += OnPosterSizeChanged;
        };
        Unloaded += (_, _) =>
        {
            SettingsService.Instance.PosterSizeChanged -= OnPosterSizeChanged;
            _previewCancellation?.Cancel();
        };
    }

    private double _cardWidthOverride;

    /// <summary>
    /// Details recommendations may scale to their grid slot without changing
    /// the poster size used by catalog cards elsewhere in the application.
    /// </summary>
    public void SetCardWidthOverride(double width)
    {
        _cardWidthOverride = double.IsFinite(width) && width > 0 ? width : 0;
        ApplyPosterSize();
    }

    private void OnPosterSizeChanged() => DispatcherQueue.TryEnqueue(ApplyPosterSize);

    private void ApplyPosterSize()
    {
        var w = _cardWidthOverride > 0 ? _cardWidthOverride : PosterMetrics.CardWidth;
        var h = _cardWidthOverride > 0 ? Math.Round(w * 1.5) : PosterMetrics.CardHeight;
        OuterGrid.Width = w + 4;
        PosterGrid.Width = w;
        PosterGrid.Height = h;
    }

    private static void OnMovieChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MovieCard card)
        {
            card.Update();
        }
    }

    private void Update()
    {
        if (Movie == null) return;

        TitleText.Text = Movie.Name ?? "";
        DetailsText.Text = Movie.Details ?? "";

        PosterCache.SetSource(PosterImage, Movie.Poster);

        RatingText.Text = Movie.Rating?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "";
        (RatingText.Parent as FrameworkElement)!.Visibility = Movie.Rating != null ? Visibility.Visible : Visibility.Collapsed;

        if (Movie.Info == MediaInfoType.Series && Movie.Season is { } s && Movie.Episode is { } e)
        {
            InfoBadge.Visibility = Visibility.Visible;
            InfoText.Text = Loc.Get("Info.Series", s, e);
        }
        else if (Movie.Info == MediaInfoType.Completed)
        {
            InfoBadge.Visibility = Visibility.Visible;
            InfoText.Text = Loc.Get("Info.Completed");
        }
        else if (Movie.Info == MediaInfoType.Wait)
        {
            InfoBadge.Visibility = Visibility.Visible;
            InfoText.Text = Loc.Get("Info.Wait");
        }
        else
        {
            InfoBadge.Visibility = Visibility.Collapsed;
        }
        if (RecommendationPreview) SetupRecommendationPreview();
    }

    private void CardButton_Click(object sender, RoutedEventArgs e)
    {
        if (Movie == null) return;
        Nav.Go<DetailsPage>(Movie);
    }

    private void CardButton_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AnimateScale(RecommendationPreview ? 1.025f : 1.05f);
        AnimateOpacity(HoverOverlay, 1);
        if (RecommendationPreview) _ = LoadRecommendationPreviewAsync();
    }

    private void CardButton_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimateScale(1.0f);
        AnimateOpacity(HoverOverlay, 0);
        _previewCancellation?.Cancel();
    }

    private void CardButton_PointerPressed(object sender, PointerRoutedEventArgs e) => AnimateScale(0.97f);

    private void CardButton_PointerReleased(object sender, PointerRoutedEventArgs e) => AnimateScale(1.0f);

    /// <summary>
    /// A compact native tooltip, displayed beside a recommendation poster.
    /// The first frame uses catalog data; full synopsis and cast are fetched
    /// only after a sustained hover and are not needed for normal scrolling.
    /// </summary>
    private void SetupRecommendationPreview()
    {
        if (!RecommendationPreview || Movie is not { } movie) return;

        if (_previewTip == null)
        {
            var isLight = ActualTheme == ElementTheme.Light;
            var background = new SolidColorBrush(isLight
                ? Windows.UI.Color.FromArgb(255, 250, 250, 252)
                : Windows.UI.Color.FromArgb(255, 35, 38, 43));
            var stroke = new SolidColorBrush(isLight
                ? Windows.UI.Color.FromArgb(255, 212, 215, 222)
                : Windows.UI.Color.FromArgb(255, 74, 79, 87));
            var muted = new SolidColorBrush(isLight
                ? Windows.UI.Color.FromArgb(255, 75, 80, 90)
                : Windows.UI.Color.FromArgb(255, 190, 195, 204));
            var primary = new SolidColorBrush(isLight
                ? Windows.UI.Color.FromArgb(255, 30, 33, 39)
                : Windows.UI.Color.FromArgb(255, 247, 248, 250));

            _previewTitle = new TextBlock
            {
                FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap, MaxLines = 2,
                Foreground = primary
            };
            _previewSubtitle = new TextBlock
            {
                FontSize = 12, Foreground = muted, TextWrapping = TextWrapping.Wrap,
                MaxLines = 2
            };
            _previewRating = new TextBlock
            {
                FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = ThemeHelper.AccentTextBrush, TextWrapping = TextWrapping.Wrap
            };
            _previewDescription = new TextBlock
            {
                FontSize = 12, Foreground = primary,
                TextWrapping = TextWrapping.Wrap, MaxLines = 5,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _previewGenres = new TextBlock
            {
                FontSize = 12, Foreground = muted, TextWrapping = TextWrapping.Wrap,
                MaxLines = 2
            };
            _previewDirector = new TextBlock
            {
                FontSize = 12, Foreground = muted, TextWrapping = TextWrapping.Wrap,
                MaxLines = 2
            };
            _previewCast = new TextBlock
            {
                FontSize = 12, Foreground = muted, TextWrapping = TextWrapping.Wrap,
                MaxLines = 3
            };
            var content = new StackPanel { Spacing = 7 };
            content.Children.Add(_previewTitle);
            content.Children.Add(_previewSubtitle);
            content.Children.Add(_previewRating);
            content.Children.Add(_previewDescription);
            content.Children.Add(_previewGenres);
            content.Children.Add(_previewDirector);
            content.Children.Add(_previewCast);
            _previewTip = new ToolTip
            {
                Content = new Border
                {
                    Width = 325,
                    CornerRadius = new CornerRadius(12),
                    Background = background,
                    BorderBrush = stroke,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(14),
                    Child = content
                }
            };
            ToolTipService.SetToolTip(CardButton, _previewTip);
        }

        if (_previewMovieId == movie.Id) return;
        _previewCancellation?.Cancel();
        _previewMovieId = movie.Id;
        _previewLoaded = false;
        _previewTitle!.Text = movie.Name ?? "";
        _previewSubtitle!.Text = movie.Details ?? "";
        _previewRating!.Text = movie.Rating is { } rating
            ? "★ " + rating.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture)
            : "";
        _previewDescription!.Text = "";
        _previewGenres!.Text = "";
        _previewDirector!.Text = "";
        _previewCast!.Text = "";
    }

    private async Task LoadRecommendationPreviewAsync()
    {
        if (!RecommendationPreview || Movie is not { } movie || _previewLoaded)
            return;
        _previewCancellation?.Cancel();
        var cts = new CancellationTokenSource();
        _previewCancellation = cts;
        try
        {
            // Most pointer transits across a row should trigger no network I/O.
            await Task.Delay(550, cts.Token);
            MovieDetailed details;
            lock (PreviewGate)
            {
                PreviewCache.TryGetValue(movie.Id, out details!);
            }
            if (details == null)
            {
                details = await RezkaService.Instance.Client.GetDetailsAsync(movie.Id, cts.Token);
                lock (PreviewGate)
                {
                    if (PreviewCache.Count >= 18)
                        PreviewCache.Remove(PreviewCache.Keys.First());
                    PreviewCache[movie.Id] = details;
                }
            }
            if (cts.IsCancellationRequested || Movie?.Id != movie.Id ||
                _previewMovieId != movie.Id) return;
            _previewLoaded = true;

            _previewTitle!.Text = details.Name;
            _previewSubtitle!.Text = details.OriginalName ?? movie.Details ?? "";
            var ratings = new List<string>();
            if (details.SiteRating?.Value is { } rezka)
                ratings.Add("★ " + rezka.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture));
            if (details.ImdbRating?.Value is { } imdb)
                ratings.Add("IMDb " + imdb.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture));
            if (details.KpRating?.Value is { } kp)
                ratings.Add("КП " + kp.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture));
            _previewRating!.Text = string.Join("  ·  ", ratings);
            _previewDescription!.Text = details.Description ?? "";
            _previewGenres!.Text = details.Genres is { Count: > 0 }
                ? Loc.Get("Details.Genre") + ": " + string.Join(", ", details.Genres.Select(x => x.Name))
                : "";
            _previewDirector!.Text = details.Producers is { Count: > 0 }
                ? Loc.Get("Details.Director") + ": " +
                    string.Join(", ", details.Producers.Take(3).Select(x => x.Name))
                : "";
            _previewCast!.Text = details.Actors is { Count: > 0 }
                ? Loc.Get("Details.Actors") + ": " +
                    string.Join(", ", details.Actors.Take(5).Select(x => x.Name))
                : "";
        }
        catch (OperationCanceledException)
        {
            // Rapid hovers and page navigation do not fetch full metadata.
        }
        catch (Exception ex)
        {
            Log.Info("[Recommendation] Hover preview unavailable: " + ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_previewCancellation, cts))
                _previewCancellation = null;
            cts.Dispose();
        }
    }

    private void AnimateScale(float to)
    {
        var transform = new ScaleTransform
        {
            CenterX = OuterGrid.Width / 2,
            CenterY = PosterGrid.Height / 2,
        };
        CardButton.RenderTransform = transform;
        var storyboard = new Storyboard();
        foreach (var property in new[] { "ScaleX", "ScaleY" })
        {
            var animation = new DoubleAnimation
            {
                To = to,
                Duration = TimeSpan.FromMilliseconds(140),
                EnableDependentAnimation = true,
            };
            Storyboard.SetTarget(animation, transform);
            Storyboard.SetTargetProperty(animation, property);
            storyboard.Children.Add(animation);
        }

        storyboard.Begin();
    }

    private static void AnimateOpacity(UIElement element, double to)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(140),
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard { Children = { animation } };
        storyboard.Begin();
    }
}
