using HDREZKA.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HDREZKA.App.Views;

public sealed partial class SupportPage : Page
{
    private static readonly Uri BoostyUrl = new("https://boosty.to/tridgi");

    public SupportPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ApplyLocalization();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
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
