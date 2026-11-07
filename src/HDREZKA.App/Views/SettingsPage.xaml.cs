using HDREZKA.App.Services;
using HDREZKA.Core.Api;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HDREZKA.App.Views;

public sealed partial class SettingsPage : Page
{
    private bool _initialized;
    private ChannelRelease? _pendingUpdate;
    private CancellationTokenSource? _downloadCancellation;

    public SettingsPage()
    {
        InitializeComponent();
        // Native ListView reorder updates the visual order. Save it only when
        // a drag completes, so checkbox clicks never rewrite the ordering.
        HomeSectionsList.DragItemsCompleted += (_, _) =>
            DispatcherQueue.TryEnqueue(PersistHomeSectionsOrder);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ApplyLocalization();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

        if (_initialized) return;

        var settings = SettingsService.Instance;
        OpeningSkipBox.Value = settings.OpeningSkipSeconds;
        SeekBackBox.Value = settings.SeekBackSeconds;
        SeekForwardBox.Value = settings.SeekForwardSeconds;
        DefaultSpeedBox.Value = settings.DefaultSpeed;
        RememberSpeedCheck.IsChecked = settings.RememberPlayerSpeed;
        SeekThumbnailsCheck.IsChecked = settings.EnableSeekThumbnails;
        MirrorBox.Text = settings.Mirror;

        LanguageBox.Items.Clear();
        LanguageBox.Items.Add(new ComboBoxItem { Content = "Русский", Tag = AppLanguage.Ru });
        LanguageBox.Items.Add(new ComboBoxItem { Content = "English", Tag = AppLanguage.En });
        LanguageBox.Items.Add(new ComboBoxItem { Content = "Українська", Tag = AppLanguage.Uk });
        LanguageBox.SelectedIndex = (int)settings.Language;

        QualityBox.Items.Clear();
        foreach (var quality in new[] { "360p", "480p", "720p", "1080p", "1440p", "2160p" })
        {
            QualityBox.Items.Add(new ComboBoxItem { Content = quality, Tag = quality });
        }

        var qualityIndex = QualityBox.Items.IndexOf(QualityBox.Items.OfType<ComboBoxItem>().FirstOrDefault(q => (string?)q.Tag == settings.DefaultQuality));
        QualityBox.SelectedIndex = qualityIndex >= 0 ? qualityIndex : 3;

        HeadersToggle.IsOn = settings.UseAndroidHeaders;
        ShowPlaybackStatsCheck.IsChecked = settings.ShowPlaybackStats;
        DirectPlayCheck.IsChecked = settings.PlayFromHomeDirectly;
        VersionText.Text = $"HDREZKA Anime4K Motion · {ChannelUpdateService.CurrentDescription}";
        UpdateChannelBox.SelectedIndex = settings.PreferredUpdateChannel == UpdateChannel.Testing ? 1 : 0;
        InstalledBuildText.Text = "Установлено: " + ChannelUpdateService.CurrentDescription;
        UpdateStatusText.Text = "Выбери канал и проверь доступные сборки.";


        _initialized = true;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        DispatcherQueue.TryEnqueue(ApplyLocalization);
    }

    private void SeekBackBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_initialized || !double.IsFinite(args.NewValue)) return;
        SettingsService.Instance.SeekBackSeconds = (int)Math.Clamp(Math.Round(args.NewValue), 1, 120);
        SettingsService.Instance.Save();
    }

    private void SeekForwardBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_initialized || !double.IsFinite(args.NewValue)) return;
        SettingsService.Instance.SeekForwardSeconds = (int)Math.Clamp(Math.Round(args.NewValue), 1, 120);
        SettingsService.Instance.Save();
    }

    private void DefaultSpeedBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_initialized || !double.IsFinite(args.NewValue)) return;
        SettingsService.Instance.DefaultSpeed = Math.Clamp(args.NewValue, 0.5, 3.0);
        SettingsService.Instance.Save();
    }

    private void SeekThumbnailsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        SettingsService.Instance.EnableSeekThumbnails = SeekThumbnailsCheck.IsChecked == true;
        SettingsService.Instance.Save();
    }

    private void RememberSpeedCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        SettingsService.Instance.RememberPlayerSpeed = RememberSpeedCheck.IsChecked == true;
        SettingsService.Instance.Save();
    }

    private void OpeningSkipBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_initialized || !double.IsFinite(args.NewValue)) return;
        SettingsService.Instance.OpeningSkipSeconds = (int)Math.Clamp(Math.Round(args.NewValue), 1, 600);
        SettingsService.Instance.Save();
    }

    private void ApplyLocalization()
    {
        TitleText.Text = Loc.Get("Settings.Title");
        ConnectionHeader.Text = Loc.Get("Settings.GroupConnection");
        InterfaceHeader.Text = Loc.Get("Settings.GroupInterface");
        PlaybackHeader.Text = Loc.Get("Settings.GroupPlayback");
        MirrorHeader.Text = Loc.Get("Settings.Mirror");
        MirrorHint.Text = Loc.Get("Settings.MirrorHint");
        LanguageHeader.Text = Loc.Get("Settings.Language");
        OpenAppearanceText.Text = Loc.Get("Nav.Appearance");
        AppearanceMovedHint.Text = Loc.Get("Settings.AppearanceMoved");
        QualityHeader.Text = Loc.Get("Settings.Quality");
        DirectPlayCheck.Content = Loc.Get("Settings.PlayFromHome");
        HomeHeader.Text = Loc.Get("Settings.HomeSections");
        HomeHint.Text = Loc.Get("Settings.HomeSectionsHint");
        BuildHomeSectionsList();
        HeadersHeader.Text = Loc.Get("Settings.Headers");
        HeadersHint.Text = Loc.Get("Settings.HeadersHint");
        AutoMirrorButton.Content = Loc.Get("Settings.AutoMirror");
        UpdateHeader.Text = Loc.Get("Settings.Updates");
        CheckUpdatesButton.Content = Loc.Get("Settings.CheckUpdates");
        AboutHeader.Text = Loc.Get("Settings.About");
        DisclaimerText.Text = Loc.Get("Settings.Disclaimer");
        LogoutButton.Content = Loc.Get("Common.Logout");
        LogoutButton.Visibility = RezkaService.Instance.IsLoggedIn ? Visibility.Visible : Visibility.Collapsed;

    }

    private async void MirrorBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var mirror = MirrorBox.Text.Trim();
        if (!Uri.TryCreate(mirror, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            MirrorBox.Text = SettingsService.Instance.Mirror;
            return;
        }

        if (!mirror.EndsWith('/')) mirror += '/';
        if (mirror.Equals(SettingsService.Instance.Mirror, StringComparison.OrdinalIgnoreCase)) return;

        // Смена домена = потеря сессии: куки привязаны к домену.
        // Автоподбор об этом предупреждает, ручной ввод — тоже должен.
        if (RezkaService.Instance.IsLoggedIn)
        {
            var dialog = new ContentDialog
            {
                Title = Loc.Get("Mirror.PickTitle"),
                Content = Loc.Get("Mirror.PickLogoutWarn", mirror),
                PrimaryButtonText = Loc.Get("Mirror.Apply"),
                CloseButtonText = Loc.Get("Common.Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                MirrorBox.Text = SettingsService.Instance.Mirror;
                return;
            }
        }

        SettingsService.Instance.Mirror = mirror;
        SettingsService.Instance.Save();
        RezkaService.Instance.ApplySettings();
        MirrorStatusText.Text = "";
    }

    private void OpenAppearanceButton_Click(object sender, RoutedEventArgs e)
        => Nav.Go<AppearancePage>();

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        if (LanguageBox.SelectedItem is ComboBoxItem { Tag: AppLanguage lang })
        {
            SettingsService.Instance.Language = lang;
            SettingsService.Instance.Save();
            LocalizationService.Instance.Apply(lang);
        }
    }

    private void QualityBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        if (QualityBox.SelectedItem is ComboBoxItem { Tag: string quality })
        {
            SettingsService.Instance.DefaultQuality = quality;
            SettingsService.Instance.Save();
        }
    }

    private static string HomeSectionName(string id) => id switch
    {
        "Hero" => Loc.Get("Filter.Hot"),
        "Continue" => Loc.Get("Continue.Home"),
        "Tracked" => Loc.Get("Home.Tracked"),
        "Films" => Loc.Get("Section.Films"),
        "Series" => Loc.Get("Section.Series"),
        "Cartoons" => Loc.Get("Section.Cartoons"),
        "Popular" => Loc.Get("Filter.Popular"),
        "Watching" => Loc.Get("Filter.WatchingNow"),
        "Soon" => Loc.Get("Filter.Soon"),
        _ => id,
    };

    private void BuildHomeSectionsList()
    {
        HomeSectionsList.Items.Clear();
        var sections = SettingsService.Instance.HomeSections;
        // The outer settings ScrollViewer handles scrolling; keep all rows
        // visible, so a second scrollbar does not interfere with drag/drop.
        HomeSectionsList.Height = sections.Count * 52 + 10;

        foreach (var section in sections)
        {
            // The stable ID stays on the item during a reorder. Capturing an
            // integer index would toggle the wrong row after drag-and-drop.
            var row = new Grid
            {
                Tag = section.Id,
                ColumnSpacing = 8,
                MinHeight = 40,
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var grip = new FontIcon
            {
                Glyph = "\uE700", // Three horizontal lines: drag handle.
                FontSize = 15,
                Opacity = 0.55,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(grip, "Перетащите строку, чтобы изменить порядок");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(grip, "Перетащить блок");
            row.Children.Add(grip);

            var check = new CheckBox
            {
                Content = HomeSectionName(section.Id),
                IsChecked = section.Visible,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinHeight = 34,
            };
            var id = section.Id;
            check.Checked += (_, _) => SetHomeSectionVisible(id, true);
            check.Unchecked += (_, _) => SetHomeSectionVisible(id, false);
            Grid.SetColumn(check, 1);
            row.Children.Add(check);

            HomeSectionsList.Items.Add(row);
        }
    }

    private void PersistHomeSectionsOrder()
    {
        var ids = HomeSectionsList.Items.OfType<Grid>()
            .Select(item => item.Tag as string)
            .ToList();
        var sections = SettingsService.Instance.HomeSections;
        if (ids.Count != sections.Count || ids.Any(string.IsNullOrWhiteSpace))
            return;

        var byId = sections.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        if (ids.Any(id => !byId.ContainsKey(id!)) ||
            ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Count)
            return;

        var next = ids.Select(id => byId[id!]).ToList();
        if (sections.Select(s => s.Id).SequenceEqual(next.Select(s => s.Id),
            StringComparer.OrdinalIgnoreCase))
            return;

        sections.Clear();
        sections.AddRange(next);
        SettingsService.Instance.Save();
        Log.Info("[Settings] Home sections reordered: " + string.Join(", ", ids));
    }

    private static void SetHomeSectionVisible(string id, bool visible)
    {
        var sections = SettingsService.Instance.HomeSections;
        var index = sections.FindIndex(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || sections[index].Visible == visible) return;
        sections[index] = sections[index] with { Visible = visible };
        SettingsService.Instance.Save();
    }

    private void HeadersToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        SettingsService.Instance.UseAndroidHeaders = HeadersToggle.IsOn;
        SettingsService.Instance.Save();
        RezkaService.Instance.ApplySettings();
    }

    private void ShowPlaybackStatsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        SettingsService.Instance.ShowPlaybackStats = ShowPlaybackStatsCheck.IsChecked == true;
        SettingsService.Instance.Save();
    }

    private void DirectPlayCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        SettingsService.Instance.PlayFromHomeDirectly = DirectPlayCheck.IsChecked == true;
        SettingsService.Instance.Save();
    }

    private async void AutoMirrorButton_Click(object sender, RoutedEventArgs e)
    {
        AutoMirrorButton.IsEnabled = false;
        MirrorStatusText.Text = Loc.Get("Settings.MirrorChecking");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var before = SettingsService.Instance.Mirror;
            var applied = await MirrorService.PickAndApplyAsync(Content.XamlRoot, DispatcherQueue, cts.Token);
            if (applied == null)
            {
                MirrorStatusText.Text = Loc.Get("Settings.MirrorNoneFound");
            }
            else
            {
                MirrorBox.Text = SettingsService.Instance.Mirror;
                MirrorStatusText.Text = string.Equals(applied, before, StringComparison.OrdinalIgnoreCase)
                    ? Loc.Get("Settings.MirrorSame")
                    : Loc.Get("Settings.MirrorFound", applied);
            }
        }
        catch
        {
            MirrorStatusText.Text = Loc.Get("Settings.MirrorNoneFound");
        }
        finally
        {
            AutoMirrorButton.IsEnabled = true;
        }
    }

    private void CopyBuildButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Use the installed build, not the selected update channel: the two may differ.
            var details = string.Join(Environment.NewLine, new[]
            {
                "HDREZKA Anime4K Motion",
                "Версия: " + UpdateService.CurrentVersion,
                "Тег сборки: " + (ChannelUpdateService.InstalledTag ?? "не указан"),
                "Коммит: " + (ChannelUpdateService.InstalledCommit ?? "не указан"),
                "Выбранный канал обновлений: " +
                    (SettingsService.Instance.PreferredUpdateChannel == UpdateChannel.Testing ? "Тестовый" : "Стабильный"),
                "Репозиторий: " + ReleaseCatalog.Repository
            });
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(details);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            CopiedBuildStatusText.Text = "Сведения о сборке скопированы.";
            CopiedBuildStatusText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
            CopiedBuildStatusText.Text = "Не удалось скопировать сведения о сборке.";
            CopiedBuildStatusText.Visibility = Visibility.Visible;
        }
    }

    private async void VersionHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        VersionHistoryButton.IsEnabled = false;
        VersionHistoryButton.Content = "Загрузка истории…";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var lookup = await ChannelUpdateService.FetchReleasesWithStatusAsync(ct: cts.Token);
            var dialog = VersionHistoryDialog.Create(
                lookup.Json, Content.XamlRoot, SettingsService.Instance.PreferredUpdateChannel);
            await dialog.ShowAsync();
            if (lookup.IsStale)
                UpdateStatusText.Text = "История показана из кэша (" +
                    lookup.FetchedAt.LocalDateTime.ToString("dd.MM HH:mm") +
                    "). GitHub сейчас недоступен или ограничил запросы.";
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
            UpdateStatusText.Text = "Не удалось загрузить историю версий: " + ex.Message;
        }
        finally
        {
            VersionHistoryButton.Content = "История версий";
            VersionHistoryButton.IsEnabled = true;
        }
    }

    private void UpdateChannelBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _downloadCancellation != null) return;
        var channel = UpdateChannelBox.SelectedIndex == 1 ? UpdateChannel.Testing : UpdateChannel.Stable;
        SettingsService.Instance.PreferredUpdateChannel = channel;
        SettingsService.Instance.Save();
        _pendingUpdate = null;
        InstallUpdateButton.Visibility = Visibility.Collapsed;
        UpdateReleaseNotesText.Text = "";
        UpdateStatusText.Text = "Канал выбран. Нажми «Проверить обновления».";
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        InstallUpdateButton.Visibility = Visibility.Collapsed;
        _pendingUpdate = null;
        UpdateReleaseNotesText.Text = "";
        UpdateStatusText.Text = "Проверка обновлений GitHub…";
        try
        {
            var channel = SettingsService.Instance.PreferredUpdateChannel;
            var (release, lookup) = await ChannelUpdateService.LatestWithStatusAsync(channel, forceRefresh: true);
            if (lookup.IsStale)
            {
                UpdateStatusText.Text = "Новая проверка GitHub пока недоступна. Последние сведения о релизах сохранены от " +
                    lookup.FetchedAt.LocalDateTime.ToString("dd.MM.yyyy HH:mm") +
                    ". Попробуйте позже; установленная версия не изменена.";
                return;
            }
            if (release == null)
            {
                UpdateStatusText.Text = "В выбранном канале пока нет опубликованных сборок.";
                return;
            }
            if (ChannelUpdateService.IsInstalled(release))
            {
                UpdateStatusText.Text = "Уже установлена актуальная сборка " + release.Tag + ".";
                return;
            }
            // v2.0.0 was published before the channel updater existed.
            // Installing it would remove the only in-app route back to testing.
            if (channel == UpdateChannel.Stable && release.Version == "2.0.0")
            {
                UpdateStatusText.Text = "Стабильная v2.0.0 не поддерживает выбор каналов. Чтобы не потерять обновлятор, автоматический откат на неё пока заблокирован. Нужен следующий стабильный релиз.";
                return;
            }
            // Fail closed if the release has no verifiable archive SHA-256.
            await ChannelUpdateService.RequireSha256Async(release);
            _pendingUpdate = release;
            UpdateStatusText.Text = "Доступна " + (channel == UpdateChannel.Testing ? "тестовая" : "стабильная") +
                " сборка " + release.Tag + " · " + release.PublishedAt.LocalDateTime.ToString("dd.MM.yyyy HH:mm");
            UpdateReleaseNotesText.Text = release.Notes.Length > 1200
                ? release.Notes[..1200] + "…" : release.Notes;
            InstallUpdateButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
            UpdateStatusText.Text = "Не удалось проверить обновления: " + ex.Message;
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void CancelUpdateButton_Click(object sender, RoutedEventArgs e)
        => _downloadCancellation?.Cancel();

    private async void InstallUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        var release = _pendingUpdate;
        if (release == null || _downloadCancellation != null) return;
        if (release.Channel != SettingsService.Instance.PreferredUpdateChannel) return;
        var dialog = new ContentDialog
        {
            Title = "Установить обновление " + release.Tag + "?",
            Content = "Приложение скачает проверенный архив, закроется и перезапустится после обновления. Предыдущая версия будет сохранена для восстановления.",
            PrimaryButtonText = "Обновить",
            CloseButtonText = Loc.Get("Common.Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        using var cts = new CancellationTokenSource();
        _downloadCancellation = cts;
        CheckUpdatesButton.IsEnabled = false;
        InstallUpdateButton.IsEnabled = false;
        UpdateChannelBox.IsEnabled = false;
        CancelUpdateButton.Visibility = Visibility.Visible;
        UpdateDownloadProgress.Visibility = Visibility.Visible;
        UpdateDownloadProgress.Value = 0;
        UpdateStatusText.Text = "Загрузка обновления…";
        var progress = new Progress<int>(percent =>
        {
            UpdateDownloadProgress.Value = percent;
            UpdateStatusText.Text = percent < 100
                ? "Загрузка обновления: " + percent + "%"
                : "Архив загружен. Проверка и подготовка установки…";
        });
        var installerStarted = false;
        try
        {
            // This UI event handler captures WinUI SynchronizationContext.
            await ChannelUpdateService.DownloadAndRestartAsync(release, progress, cts.Token);
            installerStarted = true;
        }
        catch (OperationCanceledException)
        {
            UpdateStatusText.Text = "Загрузка обновления отменена.";
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
            UpdateStatusText.Text = "Обновление не установлено: " + ex.Message;
        }
        finally
        {
            _downloadCancellation = null;
            CheckUpdatesButton.IsEnabled = true;
            InstallUpdateButton.IsEnabled = true;
            UpdateChannelBox.IsEnabled = true;
            CancelUpdateButton.Visibility = Visibility.Collapsed;
            UpdateDownloadProgress.Visibility = Visibility.Collapsed;
        }

        if (!installerStarted) return;

        // The old implementation called Exit() from a ThreadPool continuation
        // inside ChannelUpdateService (ConfigureAwait(false)), so the updater
        // waited 120 seconds and reported "Application did not exit".
        // Always exit from the WinUI event handler / UI dispatcher.
        SettingsService.Instance.Save();
        UpdateStatusText.Text = "Архив проверен. Закрытие приложения для установки…";
        try
        {
            if (!DispatcherQueue.HasThreadAccess)
                throw new InvalidOperationException("Завершение обновления запущено вне UI-потока.");

            // Safety net only: when native media/background threads keep the
            // WinUI process alive, stop this process after allowing graceful
            // shutdown. The detached updater still verifies/backs up the files.
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
                Environment.Exit(0);
            });
            Microsoft.UI.Xaml.Application.Current.Exit();
        }
        catch (Exception ex)
        {
            App.TryLog(ex);
            // The updater was already started. Leaving this process running
            // would repeat the timeout, so ensure it can proceed.
            Environment.Exit(0);
        }
    }

    private async void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        await RezkaService.Instance.LogoutAsync();
        if (Frame?.CanGoBack == true)
        {
            Frame.GoBack();
        }
    }
}
