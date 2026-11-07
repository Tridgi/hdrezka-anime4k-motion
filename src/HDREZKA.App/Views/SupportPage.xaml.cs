using HDREZKA.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HDREZKA.App.Views;

public sealed partial class SupportPage : Page
{
    private static readonly Uri BoostyUrl = new("https://boosty.to/tridgi");
    private bool? _qrBelowContent;

    public SupportPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ApplyLocalization();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        try
        {
            // Reuse the QR asset from the earlier support page.
            BoostyQrImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "boosty-qr.png")));
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
            BoostyQrCard.Visibility = Visibility.Collapsed;
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        base.OnNavigatedFrom(e);
    }

    private void OnLanguageChanged() => DispatcherQueue.TryEnqueue(ApplyLocalization);

    private void ApplyLocalization()
    {
        TitleText.Text = Loc.Get("Settings.Donate");
        SupportDescriptionText.Text = Loc.Get("Settings.DonateHint");
        BoostyTitleText.Text = "Boosty";
        BoostyDescriptionText.Text = Loc.Get("Support.BoostyDetails");
        BoostyButtonText.Text = Loc.Get("Settings.DonateBoosty");
        BoostyQrCaption.Text = Loc.Get("Support.ScanQr");
    }

    private void BoostyContentLayout_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Move the QR beneath the text/button before either column gets narrow.
        var below = e.NewSize.Width < 540;
        if (_qrBelowContent == below) return;
        _qrBelowContent = below;
        Grid.SetRow(BoostyQrCard, below ? 1 : 0);
        Grid.SetColumn(BoostyQrCard, below ? 0 : 1);
    }

    private void BoostyQrImage_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        BoostyQrCard.Visibility = Visibility.Collapsed;
    }

    private async void BoostyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(BoostyUrl);
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }
    }
}
