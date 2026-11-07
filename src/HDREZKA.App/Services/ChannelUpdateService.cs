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

    /// <summary>
    /// Shared release metadata cache. Repeated history checks do not consume
    /// GitHub's low anonymous REST API quota. Stale data can be shown in history
    /// on an outage, but is never reported as a successful fresh update check.
    /// </summary>
    public sealed record ReleasesLookup(string Json, bool FromCache, bool IsStale, DateTimeOffset FetchedAt);
    private sealed record ReleaseCacheFile(DateTimeOffset FetchedAt, string Json);

    private static readonly SemaphoreSlim ReleasesGate = new(1, 1);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan ManualCheckThrottle = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaximumStaleAge = TimeSpan.FromDays(7);
    private const int MaxReleasesJsonLength = 4_000_000;
    private static ReleaseCacheFile? _releasesCache;
    private static DateTimeOffset _lastFetchAttempt;
    private static bool _lastRefreshFailed;
    private static readonly string ReleasesCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HDREZKA-Anime4K-Motion", "releases-cache.json");

    public static async Task<ReleasesLookup> FetchReleasesWithStatusAsync(
        bool forceRefresh = false, CancellationToken ct = default)
    {
        await ReleasesGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var cache = LoadReleaseCache();
            if (cache != null)
            {
                var age = now - cache.FetchedAt;
                if (age >= TimeSpan.Zero && age < CacheTtl &&
                    (!forceRefresh || now - _lastFetchAttempt < ManualCheckThrottle))
                    return new ReleasesLookup(cache.Json, true, _lastRefreshFailed, cache.FetchedAt);

                // Do not hammer GitHub after an unsuccessful request.
                if (now - _lastFetchAttempt < ManualCheckThrottle && age < MaximumStaleAge)
                    return new ReleasesLookup(cache.Json, true, true, cache.FetchedAt);
            }

            _lastFetchAttempt = now;
            try
            {
                using var response = await Api.GetAsync(ReleaseCatalog.ApiUrl, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or
                        System.Net.HttpStatusCode.TooManyRequests)
                    {
                        var until = "";
                        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var resets) &&
                            long.TryParse(resets.FirstOrDefault(), out var epoch))
                        {
                            try
                            {
                                until = " Сброс лимита примерно в " +
                                    DateTimeOffset.FromUnixTimeSeconds(epoch).ToLocalTime().ToString("HH:mm") + ".";
                            }
                            catch (ArgumentOutOfRangeException) { }
                        }
                        throw new InvalidOperationException(
                            "GitHub временно ограничил запросы к релизам." + until + " Попробуйте позже.");
                    }
                    response.EnsureSuccessStatusCode();
                }

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (json.Length > MaxReleasesJsonLength || !IsReleaseListJson(json))
                    throw new InvalidDataException("GitHub вернул некорректный список релизов.");

                var fresh = new ReleaseCacheFile(DateTimeOffset.UtcNow, json);
                _releasesCache = fresh;
                _lastRefreshFailed = false;
                SaveReleaseCache(fresh);
                return new ReleasesLookup(json, false, false, fresh.FetchedAt);
            }
            catch (Exception ex) when (
                !ct.IsCancellationRequested &&
                ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                _lastRefreshFailed = true;
                if (cache == null) throw;
                Log.Info("[Updater] Cached releases used after GitHub failure: " + ex.Message);
                return new ReleasesLookup(cache.Json, true, true, cache.FetchedAt);
            }
        }
        finally
        {
            ReleasesGate.Release();
        }
    }

    public static async Task<string> FetchReleasesAsync(CancellationToken ct = default)
        => (await FetchReleasesWithStatusAsync(ct: ct).ConfigureAwait(false)).Json;

    public static async Task<(ChannelRelease? Release, ReleasesLookup Lookup)> LatestWithStatusAsync(
        UpdateChannel channel, bool forceRefresh = false, CancellationToken ct = default)
    {
        var lookup = await FetchReleasesWithStatusAsync(forceRefresh, ct).ConfigureAwait(false);
        return (ReleaseCatalog.Latest(lookup.Json, channel), lookup);
    }

    public static async Task<ChannelRelease?> LatestAsync(UpdateChannel channel, CancellationToken ct = default)
        => (await LatestWithStatusAsync(channel, ct: ct).ConfigureAwait(false)).Release;

    public static bool IsInstalled(ChannelRelease release)
        => ReleaseCatalog.IsSameBuild(release, InstalledTag, InstalledCommit);

    private static bool IsReleaseListJson(string json)
    {
        try
        {
            using var parsed = JsonDocument.Parse(json);
            return parsed.RootElement.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException) { return false; }
    }

    private static ReleaseCacheFile? LoadReleaseCache()
    {
        if (_releasesCache != null) return _releasesCache;
        try
        {
            var file = new FileInfo(ReleasesCachePath);
            if (!file.Exists || file.Length > MaxReleasesJsonLength + 2048) return null;
            var cache = JsonSerializer.Deserialize<ReleaseCacheFile>(File.ReadAllText(ReleasesCachePath));
            if (cache == null || cache.Json.Length > MaxReleasesJsonLength ||
                !IsReleaseListJson(cache.Json)) return null;
            var age = DateTimeOffset.UtcNow - cache.FetchedAt;
            if (age < TimeSpan.Zero || age >= MaximumStaleAge) return null;
            return _releasesCache = cache;
        }
        catch { return null; }
    }

    private static void SaveReleaseCache(ReleaseCacheFile cache)
    {
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReleasesCachePath)!);
            temporary = ReleasesCachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(cache));
            File.Move(temporary, ReleasesCachePath, true);
        }
        catch (Exception ex)
        {
            Log.Info("[Updater] Could not persist release cache: " + ex.Message);
        }
        finally
        {
            if (temporary != null)
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch { }
            }
        }
    }

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
