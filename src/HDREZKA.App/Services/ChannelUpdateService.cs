using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using HDREZKA.Core.Api;

namespace HDREZKA.App.Services;

/// <summary>Discover verified archives for stable releases or test prereleases.</summary>
public static class ChannelUpdateService
{
    private static readonly HttpClient Api = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly HttpClient Downloads = new() { Timeout = Timeout.InfiniteTimeSpan };
    private const long MaximumArchiveBytes = 700L * 1024 * 1024;

    static ChannelUpdateService()
    {
        Api.DefaultRequestHeaders.UserAgent.ParseAdd("HDREZKA-Anime4K-Motion-Updater");
        Api.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        Downloads.DefaultRequestHeaders.UserAgent.ParseAdd("HDREZKA-Anime4K-Motion-Updater");
    }

    private sealed record InstalledMetadata(string Tag, string Commit, string Channel);
    private static InstalledMetadata? Installed
    {
        get
        {
            try
            {
                var file = Path.Combine(AppContext.BaseDirectory, "update-build.json");
                return File.Exists(file)
                    ? JsonSerializer.Deserialize<InstalledMetadata>(File.ReadAllText(file))
                    : null;
            }
            catch { return null; }
        }
    }

    public static string? InstalledTag => Installed?.Tag;

    public static string? InstalledCommit
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Installed?.Commit)) return Installed.Commit;
            var value = typeof(App).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
            var plus = value.LastIndexOf('+');
            if (plus < 0) return null;
            var commit = value[(plus + 1)..];
            return commit.Length >= 7 && commit.All(Uri.IsHexDigit) ? commit : null;
        }
    }

    public static string CurrentDescription
    {
        get
        {
            var sha = InstalledCommit;
            var id = sha?.Length >= 7 ? " · " + sha[..7] : "";
            var channel = Installed?.Channel is "Testing" ? " · тестовая" : "";
            return UpdateService.CurrentVersion + id + channel;
        }
    }

    public static async Task<ChannelRelease?> LatestAsync(UpdateChannel channel, CancellationToken ct = default)
    {
        using var response = await Api.GetAsync(ReleaseCatalog.ApiUrl, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ReleaseCatalog.Latest(json, channel);
    }

    public static bool IsInstalled(ChannelRelease release)
        => ReleaseCatalog.IsSameBuild(release, InstalledTag, InstalledCommit);

    /// <summary>Require a trustworthy SHA-256 before allowing installation.</summary>
    public static async Task<string> RequireSha256Async(ChannelRelease release, CancellationToken ct = default)
    {
        if (release.Sha256 is { } digest) return digest;
        if (release.ManifestUrl is not { } url)
            throw new InvalidDataException("У релиза нет SHA-256. Установка заблокирована.");
        using var resp = await Api.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var hash = ReleaseCatalog.ChecksumFromManifest(body, release.AssetName);
        return hash ?? throw new InvalidDataException("SHA-256 архива отсутствует в SHA256SUMS.txt.");
    }

    /// <summary>
    /// Download and verify completely before launching a detached, trusted local helper.
    /// The helper replaces the directory only after this app exits and rolls back on failure.
    /// </summary>
    public static async Task DownloadAndRestartAsync(
        ChannelRelease release, IProgress<int> progress, CancellationToken ct)
    {
        var expected = await RequireSha256Async(release, ct).ConfigureAwait(false);
        var helper = Path.Combine(AppContext.BaseDirectory, "Updater", "InstallUpdate.ps1");
        if (!File.Exists(helper))
            throw new FileNotFoundException("Отсутствует локальный помощник обновлений.", helper);
        var temp = Path.Combine(Path.GetTempPath(), "HDREZKA-Update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var script = Path.Combine(temp, "InstallUpdate.ps1");
        var archive = Path.Combine(temp, "update.zip");
        File.Copy(helper, script);

        try
        {
            using var response = await Downloads.GetAsync(
                release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaximumArchiveBytes)
                throw new InvalidDataException("Архив обновления слишком большой.");
            await using (var output = File.Create(archive))
            await using (var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            {
                var buffer = new byte[128 * 1024];
                long received = 0;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                while (true)
                {
                    timeout.CancelAfter(TimeSpan.FromMinutes(2));
                    int length;
                    try { length = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    { throw new IOException("Загрузка не отвечает более двух минут."); }
                    if (length == 0) break;
                    received += length;
                    if (received > MaximumArchiveBytes)
                        throw new InvalidDataException("Архив превышает допустимый размер.");
                    await output.WriteAsync(buffer.AsMemory(0, length), ct).ConfigureAwait(false);
                    if (response.Content.Headers.ContentLength is long total && total > 0)
                        progress.Report((int)Math.Clamp(received * 100.0 / total, 0, 100));
                }
            }
            ct.ThrowIfCancellationRequested();
            var actual = ReleaseIntegrity.ComputeSha256(archive);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Контрольная сумма архива не совпадает. Установка отменена.");
            progress.Report(100);
            var exe = Path.Combine(AppContext.BaseDirectory, "HDREZKA.App.exe");
            if (!File.Exists(exe)) throw new FileNotFoundException("Не найден файл приложения.", exe);

            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = temp
            };
            foreach (var arg in new[]
            {
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
                "-InstallPath", Path.GetDirectoryName(exe)!,
                "-ArchivePath", archive, "-ExpectedSha256", expected,
                "-ParentProcessId", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "-ReleaseTag", release.Tag, "-Commit", release.Commit,
                "-ReleaseChannel", release.Channel.ToString()
            }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start);
            if (process == null) throw new InvalidOperationException("Не удалось запустить помощник установки.");
            // Return to the calling UI event handler. Only the UI dispatcher can
            // safely call WinUI Application.Exit(). Calling Exit() here after
            // ConfigureAwait(false) left the process alive and timed out the updater.
            // Nothing under AppData is removed: login, history, settings are kept.
        }
        catch
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
            throw;
        }
    }
}
