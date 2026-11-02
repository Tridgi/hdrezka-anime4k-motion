using HDREZKA.Core.Api;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HDREZKA.App.Views;

/// <summary>In-app history of published stable and test releases from GitHub.</summary>
internal static class VersionHistoryDialog
{
    public static ContentDialog Create(string releasesJson, XamlRoot xamlRoot, UpdateChannel initialChannel)
    {
        var selector = new ComboBox { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
        selector.Items.Add(new ComboBoxItem { Content = "Стабильные (main)", Tag = UpdateChannel.Stable });
        selector.Items.Add(new ComboBoxItem { Content = "Тестовые сборки", Tag = UpdateChannel.Testing });

        var entries = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
        var scroll = new ScrollViewer
        {
            Content = entries,
            MaxHeight = 440,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        void Fill(UpdateChannel channel)
        {
            entries.Children.Clear();
            var releases = ReleaseCatalog.History(releasesJson, channel).Take(20).ToList();
            if (releases.Count == 0)
            {
                entries.Children.Add(new TextBlock
                {
                    Text = "В этом канале пока нет опубликованных версий.",
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            foreach (var (release, index) in releases.Select((release, index) => (release, index)))
            {
                var details = new StackPanel { Spacing = 8 };
                details.Children.Add(new TextBlock
                {
                    Text = FormatNotes(release.Notes),
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                });
                details.Children.Add(new HyperlinkButton
                {
                    Content = "Открыть релиз на GitHub",
                    NavigateUri = new Uri(release.Url),
                    Padding = new Thickness(0, 2, 0, 2),
                    HorizontalAlignment = HorizontalAlignment.Left
                });
                entries.Children.Add(new Expander
                {
                    Header = new TextBlock
                    {
                        Text = release.Tag + "  ·  " + release.PublishedAt.LocalDateTime.ToString("dd.MM.yyyy"),
                        TextWrapping = TextWrapping.Wrap
                    },
                    Content = details,
                    IsExpanded = index == 0,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                });
            }
        }

        selector.SelectedIndex = initialChannel == UpdateChannel.Testing ? 1 : 0;
        Fill(initialChannel);
        selector.SelectionChanged += (_, _) =>
            Fill(selector.SelectedIndex == 1 ? UpdateChannel.Testing : UpdateChannel.Stable);

        var content = new StackPanel { Spacing = 12, MinWidth = 0, MaxWidth = 540 };
        content.Children.Add(new TextBlock
        {
            Text = "Список изменений берётся из опубликованных GitHub Releases. Проверенные доработки попадают в стабильную историю после выпуска нового релиза.",
            TextWrapping = TextWrapping.Wrap,
            Style = Microsoft.UI.Xaml.Application.Current.Resources["SecondaryText"] as Style
        });
        content.Children.Add(selector);
        content.Children.Add(scroll);

        return new ContentDialog
        {
            Title = "История версий",
            Content = content,
            CloseButtonText = "Закрыть",
            XamlRoot = xamlRoot
        };
    }

    private static string FormatNotes(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return "Для этого релиза описание изменений не опубликовано.";

        var lines = markdown.Replace("\r", "").Split('\n').Select(line =>
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("#")) trimmed = trimmed.TrimStart('#').TrimStart();
            if (trimmed.StartsWith("- ")) trimmed = "• " + trimmed[2..];
            return trimmed.Replace("**", "").Replace("`", "");
        });
        var result = string.Join("\n", lines).Trim();
        return result.Length > 6000 ? result[..6000] + "…" : result;
    }
}
