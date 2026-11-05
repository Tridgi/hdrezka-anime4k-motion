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
    private void UpdateRatingBadge()
    {
        // Only ratings available in the listing are drawn; a hover must not
        // silently add a badge to an otherwise identical poster.
        var score = Movie?.Rating;
        RatingBadge.Visibility = score.HasValue
            ? Visibility.Visible : Visibility.Collapsed;
        if (!score.HasValue) return;
        RatingText.Text = "R " + score.Value.ToString("0.0",
            System.Globalization.CultureInfo.InvariantCulture);
        RatingBadge.Background = RezkaScoreColors.Gradient(score.Value);
        RatingText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
    }


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

        UpdateRatingBadge();

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
        Canvas.SetZIndex(CardButton, 20);
        AnimateScale(1.055f);
        AnimateOpacity(HoverOverlay, 1);
        if (RecommendationPreview)
            RecommendationHoverChanged?.Invoke(this,
                new RecommendationHoverEventArgs(Movie, true));
    }

    private void CardButton_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hovering = false;
        Canvas.SetZIndex(CardButton, 0);
        AnimateScale(1.0f);
        AnimateOpacity(HoverOverlay, 0);
        if (RecommendationPreview)
            RecommendationHoverChanged?.Invoke(this,
                new RecommendationHoverEventArgs(Movie, false));
    }

    private void CardButton_PointerPressed(object sender, PointerRoutedEventArgs e) => AnimateScale(0.97f);

    private void CardButton_PointerReleased(object sender, PointerRoutedEventArgs e) => AnimateScale(_hovering ? 1.055f : 1.0f);

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
