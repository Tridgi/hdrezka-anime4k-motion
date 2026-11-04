using HDREZKA.App.Services;
using HDREZKA.Core.Api;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace HDREZKA.App.Views;

public sealed partial class PersonPage : Page
{
    private PersonSimple? _person;
    private PersonDetailed? _details;
    private bool _mirrorRecovered;

    private bool _narrowHero;

    public PersonPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ApplyLocalization();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

        if (e.Parameter is PersonSimple person && _person?.Id != person.Id)
        {
            _person = person;
            _ = LoadAsync(person);
        }
        else if (e.Parameter is PersonDetailed details && _details?.Id != details.Id)
        {
            _person = new PersonSimple(details.Id, details.Name, details.Photo);
            LoadDetails(details);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        ApplyLocalization();
        if (_details != null) Render(_details);
    });

    private void ApplyLocalization()
    {
        RetryButton.Content = Loc.Get("Common.Retry");
        MirrorButton.Content = Loc.Get("Settings.AutoMirror");
        FilmographyHeader.Text = Loc.Get("Person.Filmography");
    }

    internal static string RoleName(string roleId) => roleId switch
    {
        "akter" => Loc.Get("Person.RoleActor"),
        "aktrisa" => Loc.Get("Person.RoleActress"),
        "rezhisser" => Loc.Get("Person.RoleDirector"),
        "prodyuser" => Loc.Get("Person.RoleProducer"),
        "scenarist" => Loc.Get("Person.RoleWriter"),
        "operator" => Loc.Get("Person.RoleCameraman"),
        "montazher" => Loc.Get("Person.RoleEditor"),
        "hudozhnik" => Loc.Get("Person.RoleArtist"),
        "kompozitor" => Loc.Get("Person.RoleComposer"),
        _ => roleId,
    };

    private async Task LoadAsync(PersonSimple person)
    {
        MainPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;
        ErrorPanel.Visibility = Visibility.Collapsed;

        try
        {
            var details = await RezkaService.Instance.GetPersonAsync(person.Id);
            LoadDetails(details);
        }
        catch (RezkaException ex)
        {
            App.TryLog(new Exception($"[Person] failed id={person.Id}: {ex.Kind} {ex.Message}"));

            if (!_mirrorRecovered && ex.Kind is RezkaError.Network or RezkaError.AccessDenied or RezkaError.MirrorBanned)
            {
                _mirrorRecovered = true;
                if (await MirrorRecovery.TryRecoverAsync())
                {
                    DispatcherQueue.TryEnqueue(() => _ = LoadAsync(person));
                    return;
                }
            }

            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            ErrorText.Text = RezkaService.Instance.ErrorText(ex);
            MirrorButton.Content = Loc.Get("Settings.AutoMirror");
            MirrorButton.Visibility = ex.Kind is RezkaError.Network or RezkaError.AccessDenied or RezkaError.MirrorBanned
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void LoadDetails(PersonDetailed details)
    {
        _details = details;
        DispatcherQueue.TryEnqueue(() =>
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
            MainPanel.Visibility = Visibility.Visible;
            Render(details);
        });
    }

    private void PersonHeroGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < 560;
        if (narrow == _narrowHero) return;
        _narrowHero = narrow;
        PersonPhotoColumn.Width = narrow
            ? new GridLength(1, GridUnitType.Star) : new GridLength(200);
        PersonInfoColumn.Width = narrow
            ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(PersonBioPanel, narrow ? 0 : 1);
        Grid.SetRow(PersonBioPanel, narrow ? 1 : 0);
        var portraitWidth = narrow ? 152 : 200;
        PersonPhotoFrame.Width = PersonPoster.Width = portraitWidth;
        PersonPhotoFrame.Height = PersonPoster.Height = portraitWidth * 1.5;
    }

    private void Render(PersonDetailed details)
    {
        NameText.Text = details.Name;
        OrigText.Text = details.OriginalName ?? "";
        PosterCache.SetSource(PersonPoster, details.Photo);
        FilmographyHeader.Text = Loc.Get("Person.Filmography");

        MetaPanel.Children.Clear();
        AddMetaRow(Loc.Get("Person.Career"), details.Career);
        AddMetaRow(Loc.Get("Person.Born"), details.BirthDate);
        AddMetaRow(Loc.Get("Person.BirthPlace"), details.BirthPlace);
        AddMetaRow(Loc.Get("Person.Died"), details.DeathDate);
        AddMetaRow(Loc.Get("Person.DeathPlace"), details.DeathPlace);
        AddMetaRow(Loc.Get("Person.Height"), details.Height);

        FilmographyPanel.Children.Clear();
        var groups = details.Filmography?
            .Where(g => g.Movies.Count > 0).ToArray() ?? [];
        FilmographyCountText.Text = groups.Sum(g => g.Movies.Count) +
            " " + Loc.Get("Person.Works");

        var isFirst = true;
        foreach (var group in groups)
        {
            var list = new GridView
            {
                ItemsPanel = (ItemsPanelTemplate)Resources["PersonFilmsPanel"],
                ItemTemplate = (DataTemplate)Resources["PersonFilmTemplate"],
                ItemContainerStyle = (Style)Resources["PersonFilmItemStyle"],
                Style = (Style)Resources["PersonFilmsGridStyle"],
                SelectionMode = ListViewSelectionMode.None,
                IsItemClickEnabled = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ItemsSource = group.Movies.ToList(),
            };
            var heading = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            heading.Children.Add(new TextBlock
            {
                Text = RoleName(group.RoleId),
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            heading.Children.Add(new Border
            {
                Padding = new Thickness(8, 4, 8, 4),
                CornerRadius = new CornerRadius(8),
                Background = (Brush)Application.Current.Resources[
                    "SubtleFillColorSecondaryBrush"],
                Child = new TextBlock { Text = group.Movies.Count.ToString(),
                    FontSize = 12 }
            });
            var expander = new Expander
            {
                Header = heading,
                Content = list,
                IsExpanded = isFirst,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            FilmographyPanel.Children.Add(expander);
            isFirst = false;
        }
        if (groups.Length == 0)
            FilmographyPanel.Children.Add(new TextBlock
            {
                Text = Loc.Get("Common.Empty"),
                Foreground = (Brush)Application.Current.Resources[
                    "TextFillColorSecondaryBrush"]
            });
    }

    private void AddMetaRow(string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 4 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
        grid.ColumnDefinitions.Add(new ColumnDefinition
            { Width = new GridLength(1, GridUnitType.Star) });

        var caption = new TextBlock
        {
            Text = label, FontSize = 12,
            Foreground = (Brush)Application.Current.Resources[
                "TextFillColorSecondaryBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        var valueText = new TextBlock
        {
            Text = value, FontSize = 13, TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(caption);
        grid.Children.Add(valueText);
        MetaPanel.Children.Add(grid);
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_person != null) _ = LoadAsync(_person);
    }

    private async void MirrorButton_Click(object sender, RoutedEventArgs e)
    {
        MirrorButton.IsEnabled = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var applied = await MirrorService.PickAndApplyAsync(Content.XamlRoot, DispatcherQueue, cts.Token);
            if (applied != null && _person != null) await LoadAsync(_person);
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
        }
        finally
        {
            MirrorButton.IsEnabled = true;
        }
    }
}
