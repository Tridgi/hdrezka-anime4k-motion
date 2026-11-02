using HDREZKA.App.Services;
using HDREZKA.Core.Api;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HDREZKA.App.Views;

public sealed partial class SupportPage : Page
{
    private static readonly Uri BoostyUrl = new("https://boosty.to/tridgi");

    public SupportPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        DonorBox.Text = SettingsService.Instance.DonorCode;
        ApplyLocalization();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        try
        {
            BoostyQrImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "boosty-qr.png")));
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
            BoostyQrImage.Visibility = Visibility.Collapsed;
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        base.OnNavigatedFrom(e);
    }

    private void OnLanguageChanged()
        => DispatcherQueue.TryEnqueue(ApplyLocalization);

    private void ApplyLocalization()
    {
        TitleText.Text = Loc.Get("Settings.Donate");
        SupportDescriptionText.Text = Loc.Get("Settings.DonateHint");
        BoostyTitleText.Text = "Boosty";
        BoostyDescriptionText.Text = Loc.Get("Settings.DonateHint");
        BoostyButton.Content = Loc.Get("Settings.DonateBoosty");
        DonorHeader.Text = Loc.Get("Settings.DonorCode");
        DonorHint.Text = Loc.Get("Settings.DonorHint");
        ApplyDonorButton.Content = Loc.Get("Support.SaveCode");
        UpdateDonorStatus();
    }

    private void BoostyQrImage_ImageFailed(object sender, ExceptionRoutedEventArgs e)
        => BoostyQrImage.Visibility = Visibility.Collapsed;

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

    private void DonorBox_LostFocus(object sender, RoutedEventArgs e)
        => SaveDonorCode();

    private void ApplyDonorButton_Click(object sender, RoutedEventArgs e)
        => SaveDonorCode();

    private void SaveDonorCode()
    {
        var input = DonorBox.Text.Trim();
        if (string.Equals(SettingsService.Instance.DonorCode, input, StringComparison.Ordinal))
        {
            UpdateDonorStatus();
            return;
        }

        var previouslyUnlocked = DonorUnlock.IsUnlocked;
        SettingsService.Instance.DonorCode = input;
        SettingsService.Instance.Save();
        UpdateDonorStatus();
        if (!previouslyUnlocked && DonorUnlock.IsUnlocked)
            _ = ShowDonorThanksAsync();
    }

    private async Task ShowDonorThanksAsync()
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = Loc.Get("Settings.DonorThanksTitle"),
                Content = Loc.Get("Settings.DonorThanksText"),
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot,
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }
    }

    private void UpdateDonorStatus()
    {
        var ok = HDREZKA.Core.Api.DonorCode.Validate(SettingsService.Instance.DonorCode);
        DonorStatusText.Text = string.IsNullOrWhiteSpace(SettingsService.Instance.DonorCode)
            ? "" : Loc.Get(ok ? "Settings.DonorUnlocked" : "Settings.DonorInvalid");
        DonorPerksText.Text = ok ? Loc.Get("Settings.DonorPerks") : "";
        DonorPerksText.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
    }
}