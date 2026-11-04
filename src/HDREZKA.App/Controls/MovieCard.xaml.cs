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

    public static readonly DependencyProperty RecommendationPreviewProperty =
        DependencyProperty.Register(nameof(RecommendationPreview), typeof(bool),
            typeof(MovieCard), new PropertyMetadata(false));

    public bool RecommendationPreview
    {
        get => (bool)GetValue(RecommendationPreviewProperty);
        set => SetValue(RecommendationPreviewProperty, value);
    }

    // The page owns the preview surface so it cannot be clipped by the
    // recommendation ListView or the poster's Viewbox.
    public event EventHandler<RecommendationHoverEventArgs>? RecommendationHoverChanged;

    private readonly ScaleTransform _hoverScale = new() { ScaleX = 1, ScaleY = 1 };
    private bool _hovering;

    public MovieCard()
    {
        InitializeComponent();
        CardButton.RenderTransform = _hoverScale;
        CardButton.RenderTransformOrigin = new Windows.Foundation.Point(.5, .5);
        Loaded += (_, _) =>
        {
            ApplyPosterSize();
            SettingsService.Instance.PosterSizeChanged += OnPosterSizeChanged;
        };
        Unloaded += (_, _) =>
        {
            SettingsService.Instance.PosterSizeChanged -= OnPosterSizeChanged;
            if (RecommendationPreview)
                RecommendationHoverChanged?.Invoke(this,
                    new RecommendationHoverEventArgs(Movie, false));
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

        RatingBadge.Visibility = Movie.Rating.HasValue ? Visibility.Visible : Visibility.Collapsed;
        if (Movie.Rating is { } score)
        {
            RatingText.Text = "R " + score.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            // Smooth red -> orange -> green on the 0–10 scale.
            var t = Math.Clamp(score, 0f, 10f) / 10f;
            var start = t < .5f
                ? (r: 218.0, g: 64.0, b: 70.0, endR: 237.0, endG: 148.0, endB: 48.0, w: t * 2)
                : (r: 237.0, g: 148.0, b: 48.0, endR: 27.0, endG: 155.0, endB: 100.0, w: (t - .5f) * 2);
            byte Mix(double a, double b, double weight) =>
                (byte)Math.Clamp(Math.Round(a + (b - a) * weight), 0, 255);
            RatingBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(239,
                Mix(start.r, start.endR, start.w),
                Mix(start.g, start.endG, start.w),
                Mix(start.b, start.endB, start.w)));
            // Midscale orange is bright: use dark ink for legibility.
            RatingText.Foreground = new SolidColorBrush(
                score >= 4.4f && score < 7f
                    ? Windows.UI.Color.FromArgb(255, 31, 34, 38)
                    : Microsoft.UI.Colors.White);
        }

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

        // The HDREZKA score sits above the season badge where one exists;
        // regular movie posters keep it anchored neatly at the lower edge.
        RatingBadge.Margin = InfoBadge.Visibility == Visibility.Visible
            ? new Thickness(0, 0, 6, 35)
            : new Thickness(0, 0, 6, 6);
    }

    private void CardButton_Click(object sender, RoutedEventArgs e)
    {
        if (Movie == null) return;
        Nav.Go<DetailsPage>(Movie);
    }

    private void CardButton_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _hovering = true;
        AnimateScale(1.045f);
        AnimateOpacity(HoverOverlay, 1);
        if (RecommendationPreview)
            RecommendationHoverChanged?.Invoke(this,
                new RecommendationHoverEventArgs(Movie, true));
    }

    private void CardButton_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hovering = false;
        AnimateScale(1.0f);
        AnimateOpacity(HoverOverlay, 0);
        if (RecommendationPreview)
            RecommendationHoverChanged?.Invoke(this,
                new RecommendationHoverEventArgs(Movie, false));
    }

    private void CardButton_PointerPressed(object sender, PointerRoutedEventArgs e) => AnimateScale(0.97f);

    private void CardButton_PointerReleased(object sender, PointerRoutedEventArgs e) => AnimateScale(_hovering ? 1.045f : 1.0f);

    private void AnimateScale(float to)
    {
        var storyboard = new Storyboard();
        foreach (var property in new[] { "ScaleX", "ScaleY" })
        {
            var animation = new DoubleAnimation
            {
                To = to,
                Duration = TimeSpan.FromMilliseconds(210),
                EnableDependentAnimation = true,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animation, _hoverScale);
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


public sealed class RecommendationHoverEventArgs : EventArgs
{
    public MovieSimple? Movie { get; }
    public bool IsEntering { get; }

    public RecommendationHoverEventArgs(MovieSimple? movie, bool isEntering)
    {
        Movie = movie;
        IsEntering = isEntering;
    }
}
