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

    public MovieCard()
    {
        InitializeComponent();
        CardButton.RenderTransformOrigin = new Windows.Foundation.Point(.5, .36);
        CardButton.RenderTransform = _cardScale;
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
    private bool _hovered;
    private readonly ScaleTransform _cardScale = new() { ScaleX = 1, ScaleY = 1 };

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

        RatingText.Text = Movie.Rating is { } score
            ? "R " + score.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
            : "";
        RatingBadge.Visibility = Movie.Rating != null
            ? Visibility.Visible : Visibility.Collapsed;
        if (Movie.Rating is { } rating)
            SetRatingColor(rating);

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

        RatingBadge.Margin = InfoBadge.Visibility == Visibility.Visible
            ? new Thickness(0, 0, 6, 36)
            : new Thickness(0, 0, 6, 6);
    }

    private void SetRatingColor(float value)
    {
        // 0 = red; 5 = warm orange; 10 = green. This is the
        // HDREZKA rating, not KP/IMDb.
        var clamped = Math.Clamp(value, 0f, 10f);
        var t = clamped < 5 ? clamped / 5f : (clamped - 5) / 5f;
        (byte R, byte G, byte B) from = clamped < 5
            ? ((byte)195, (byte)59, (byte)68)
            : ((byte)232, (byte)150, (byte)52);
        (byte R, byte G, byte B) to = clamped < 5
            ? ((byte)232, (byte)150, (byte)52)
            : ((byte)37, (byte)158, (byte)103);
        static byte Interpolate(byte a, byte b, float amount) =>
            (byte)Math.Clamp((int)Math.Round(a + (b - a) * amount), 0, 255);
        RatingBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255,
            Interpolate(from.R, to.R, t),
            Interpolate(from.G, to.G, t),
            Interpolate(from.B, to.B, t)));
        RatingText.Foreground = new SolidColorBrush(
            value >= 4.5f && value < 7f
                ? Windows.UI.Color.FromArgb(255, 29, 34, 35)
                : Microsoft.UI.Colors.White);
    }

    private void CardButton_Click(object sender, RoutedEventArgs e)
    {
        if (Movie == null) return;
        Nav.Go<DetailsPage>(Movie);
    }

    private void CardButton_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _hovered = true;
        AnimateScale(1.045f);
        AnimateOpacity(HoverOverlay, 1);
        if (RecommendationPreview)
            RecommendationHoverChanged?.Invoke(this,
                new RecommendationHoverEventArgs(Movie, true));
    }

    private void CardButton_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hovered = false;
        AnimateScale(1.0f);
        AnimateOpacity(HoverOverlay, 0);
        if (RecommendationPreview)
            RecommendationHoverChanged?.Invoke(this,
                new RecommendationHoverEventArgs(Movie, false));
    }

    private void CardButton_PointerPressed(object sender, PointerRoutedEventArgs e) => AnimateScale(0.97f);

    private void CardButton_PointerReleased(object sender, PointerRoutedEventArgs e)
        => AnimateScale(_hovered ? 1.045f : 1.0f);

    private void AnimateScale(float to)
    {
        // Reuse one GPU-friendly transform; repeated pointer events must
        // not replace it or create clipping/flicker at viewport boundaries.
        var storyboard = new Storyboard();
        foreach (var property in new[] { "ScaleX", "ScaleY" })
        {
            var animation = new DoubleAnimation
            {
                To = to,
                Duration = TimeSpan.FromMilliseconds(230),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(animation, _cardScale);
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
