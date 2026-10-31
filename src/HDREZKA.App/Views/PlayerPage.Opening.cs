using HDREZKA.App.Services;
using Microsoft.UI.Xaml;

namespace HDREZKA.App.Views;

public sealed partial class PlayerPage
{
    private void RefreshOpeningSkipLabel()
    {
        var seconds = SettingsService.Instance.OpeningSkipSeconds;
        SkipOpeningText.Text = $"Опенинг +{seconds / 60}:{seconds % 60:00}";
    }

    private void SkipOpeningButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshOpeningSkipLabel();
        SeekBySeconds(SettingsService.Instance.OpeningSkipSeconds);
        ShowControls();
        _hideTimer.Start();
    }
}
