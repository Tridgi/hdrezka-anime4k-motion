using HDREZKA.App.Services;
using HDREZKA.Core.Api;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HDREZKA.App.Views;

public sealed partial class UpdatesPage : Page
{
    private bool _settingsInitialized;
    private CancellationTokenSource? _checkCancellation;
    private sealed record UpdateRow(
        string Title,
        string Text,
        string Date,
        string? Poster,
        StoredUpdate Source);

    public UpdatesPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ApplyLocalization();
        _settingsInitialized = false;
        SeriesToggle.IsOn = SettingsService.Instance.SeriesUpdatesEnabled;
        _settingsInitialized = true;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        Render();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        _settingsInitialized = false;
        _checkCancellation?.Cancel();
    }

    private void OnLanguageChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ApplyLocalization();
            Render();
        });
    }

    private void ApplyLocalization()
    {
        TitleText.Text = Loc.Get("Updates.Title");
        ClearButton.Content = Loc.Get("Updates.Clear");
        EmptyText.Text = Loc.Get("Updates.Empty");
        SeriesHeader.Text = Loc.Get("Settings.SeriesUpdates");
        SeriesHint.Text = Loc.Get("Settings.SeriesUpdatesHint");
        CheckSeriesButton.Content = Loc.Get("Settings.CheckNow");
    }

    private void SeriesToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_settingsInitialized) return;
        SettingsService.Instance.SeriesUpdatesEnabled = SeriesToggle.IsOn;
        SettingsService.Instance.Save();
    }

    private async void CheckSeriesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_checkCancellation != null) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        _checkCancellation = cts;
        CheckSeriesButton.IsEnabled = false;
        SeriesStatusText.Text = Loc.Get("Settings.SeriesChecking");
        try
        {
            TrackedSeriesService.SeedFromHistory();
            var updates = await TrackedSeriesService.CheckForUpdatesAsync(cts.Token);
            if (cts.IsCancellationRequested) return;
            if (updates.Count == 0)
            {
                SeriesStatusText.Text = Loc.Get("Settings.SeriesUpToDate");
            }
            else
            {
                var first = updates.Take(3).Select(u => $"{u.Title}: {u.Text}");
                SeriesStatusText.Text = Loc.Get("Settings.SeriesFound", updates.Count)
                    + "\n" + string.Join("\n", first);
                TrackedSeriesService.NotifyUpdates(updates);
                Render();
            }
        }
        catch (OperationCanceledException)
        {
            // Closing the page or checking timeout is not a fatal error.
        }
        catch (Exception ex)
        {
            Log.Error("[Updates] Manual check failed", ex);
            SeriesStatusText.Text = Loc.Get("Error.Network");
        }
        finally
        {
            if (ReferenceEquals(_checkCancellation, cts))
                _checkCancellation = null;
            CheckSeriesButton.IsEnabled = true;
        }
    }

    private void Render()
    {
        var rows = TrackedSeriesService.GetUpdates()
            .Select(u => new UpdateRow(
                u.Title,
                u.Text,
                u.Date.ToString("dd.MM.yyyy HH:mm"),
                u.Poster,
                u))
            .ToList();

        List.ItemsSource = rows;
        List.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyPanel.Visibility = rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        ClearButton.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void List_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not UpdateRow row) return;
        // Consumed: open details, drop the entry (badge updates via event).
        TrackedSeriesService.RemoveUpdate(row.Source);
        Render();
        Nav.Go<DetailsPage>(new MovieSimple(Id: row.Source.PagePath, Name: row.Source.Title, Poster: row.Source.Poster));
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        TrackedSeriesService.ClearUpdates();
        Render();
    }
}
