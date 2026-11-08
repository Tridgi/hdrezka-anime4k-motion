using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using HDREZKA.Core.Api;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace HDREZKA.App.Services;

/// <summary>
/// Poster cache: memory LRU + disk (%LocalAppData%/HDREZKA/postercache,
/// 200MB / 30 days). Stops re-downloading posters on every scroll.
/// </summary>
public static class PosterCache
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly ConcurrentDictionary<string, BitmapImage> Memory = new();

    // Один URL — одна загрузка, даже если его одновременно просят 5 карточек.
    private static readonly ConcurrentDictionary<string, Task<string>> InFlight =
        new(StringComparer.OrdinalIgnoreCase);

    // Ограничение параллелизма: GridView при быстром скролле просит десятки
    // постеров разом, без гейта это шторм из HTTP-запросов и декодирований.
    private static readonly SemaphoreSlim DownloadGate = new(6, 6);

    private const int MemoryCap = 160;
    private const int DecodeWidth = 360;
    private const long DirCapBytes = 200L * 1024 * 1024;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HDREZKA-Anime4K-Motion", "postercache");

    static PosterCache()
    {
        // Poster CDNs commonly block non-browser user agents even when the
        // HTML/API mirror itself accepts HDREZKA's custom client agent.
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        Http.DefaultRequestHeaders.Accept.ParseAdd("image/avif,image/webp,image/apng,image/*;q=0.9,*/*;q=0.8");
        _ = Task.Run(CleanupAsync);
    }

    // Support protocol-relative and relative URLs returned by lazy-loaded
    // hot-slider images. Never accept non-HTTP schemes or embedded data URLs.
    internal static string? NormalizePosterUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim();
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;
        if (url.StartsWith("//")) url = "https:" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            if (!Uri.TryCreate(SettingsService.Instance.Mirror, UriKind.Absolute, out var basis))
                return null;
            if (!Uri.TryCreate(basis, url, out uri)) return null;
        }
        return uri.Scheme is "http" or "https" ? uri.AbsoluteUri : null;
    }

    public static void SetSource(Image image, string? url, Action? onFailure = null)
    {
        var normalizedUrl = NormalizePosterUrl(url);
        if (normalizedUrl == null)
        {
            image.Source = null;
            image.Tag = null;
            return;
        }

        var key = normalizedUrl;
        image.Tag = key;
        if (Memory.TryGetValue(key, out var cached))
        {
            image.Source = cached;
            return;
        }

        var queue = image.DispatcherQueue;
        _ = Task.Run(async () =>
        {
            string path;
            try
            {
                path = await GetFileForUrlAsync(key).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warn("[PosterCache] Download failed (" + GetHost(key) + "): " + ex.Message);
                if (onFailure != null)
                    queue.TryEnqueue(() => { if ((string?)image.Tag == key) onFailure(); });
                return;
            }

            queue.TryEnqueue(() =>
            {
                try
                {
                    if ((string?)image.Tag != key) return; // recycled for another item
                    var bmp = Memory.GetOrAdd(key, _ =>
                    {
                        // Декодируем в размер карточки (×2 для HiDPI), а не в
                        // оригинал: постеры на сайте бывают 500+ px, и 300 таких
                        // в памяти — это сотни мегабайт.
                        var decoded = new BitmapImage { DecodePixelWidth = DecodeWidth };
                        decoded.ImageFailed += (_, e) =>
                        {
                            Log.Warn("[PosterCache] Image decoding failed (" + GetHost(key) + "): " + e.ErrorMessage);
                            Memory.TryRemove(key, out var ignored);
                            try { File.Delete(path); } catch { }
                            if ((string?)image.Tag == key) onFailure?.Invoke();
                        };
                        decoded.UriSource = new Uri(path);
                        return decoded;
                    });
                    image.Source = bmp;
                    if (Memory.Count > MemoryCap)
                    {
                        foreach (var k in Memory.Keys.Take(Memory.Count - MemoryCap / 2).ToList())
                        {
                            Memory.TryRemove(k, out _);
                        }
                    }
                }
                catch
                {
                }
            });
        });
    }

    private static string GetHost(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "unknown";
    }

    private static Task<string> GetFileForUrlAsync(string url) =>
        InFlight.GetOrAdd(url, static key => DownloadAndForgetAsync(key));

    private static async Task<string> DownloadAndForgetAsync(string url)
    {
        try
        {
            return await GetFileAsync(url).ConfigureAwait(false);
        }
        finally
        {
            InFlight.TryRemove(url, out _);
        }
    }

    private static async Task<string> GetFileAsync(string url)
    {
        Directory.CreateDirectory(Dir);
        var ext = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".bmp")) ext = ".jpg";
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ext;
        var path = Path.Combine(Dir, name);

        if (IsFresh(path)) return path;

        await DownloadGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Пока ждали слот, файл мог скачать соседний поток.
            if (IsFresh(path)) return path;

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (Uri.TryCreate(SettingsService.Instance.Mirror, UriKind.Absolute, out var mirror))
                request.Headers.Referrer = mirror;
            using var resp = await Http.SendAsync(request).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var mime = resp.Content.Headers.ContentType?.MediaType;
            if (mime != null && !mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
                mime != "application/octet-stream" && mime != "binary/octet-stream")
                throw new InvalidDataException("Poster response is not an image: " + mime);
            using var net = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);

            // Уникальный временный файл: два потока с одним ".tmp" ломали
            // и файл, и его последующее 30-дневное кэширование.
            var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                using (var file = File.Create(tmp))
                {
                    await net.CopyToAsync(file).ConfigureAwait(false);
                }

                if (!IsImageFile(tmp))
                    throw new InvalidDataException("Poster CDN returned content with no supported image signature");
                File.Move(tmp, path, overwrite: true);
            }
            catch
            {
                try { File.Delete(tmp); } catch { }
                throw;
            }

            return path;
        }
        finally
        {
            DownloadGate.Release();
        }
    }

    private static bool IsFresh(string path)
    {
        var info = new FileInfo(path);
        return info.Exists && info.Length > 0 && IsImageFile(path) &&
               DateTime.UtcNow - info.LastWriteTimeUtc < MaxAge;
    }

    private static bool IsImageFile(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> b = stackalloc byte[16];
            var n = fs.Read(b);
            if (n < 12) return false;
            return
                (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) || // JPEG
                (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) || // PNG
                (b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46) || // GIF
                (b[0] == 0x42 && b[1] == 0x4D) || // BMP
                (b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 &&
                 b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) || // WebP
                (b[4] == 0x66 && b[5] == 0x74 && b[6] == 0x79 && b[7] == 0x70 &&
                 (b[8] == 0x61 && b[9] == 0x76 && b[10] == 0x69 && b[11] == 0x66)); // AVIF
        }
        catch { return false; }
    }

    private static async Task CleanupAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            if (!Directory.Exists(Dir)) return;
            var files = new DirectoryInfo(Dir).GetFiles()
                .OrderBy(f => f.LastWriteTimeUtc)
                .ToList();
            foreach (var f in files.Where(f => DateTime.UtcNow - f.LastWriteTimeUtc > MaxAge))
            {
                try { f.Delete(); } catch { }
            }

            files = new DirectoryInfo(Dir).GetFiles().OrderBy(f => f.LastWriteTimeUtc).ToList();
            long total = files.Sum(f => f.Length);
            foreach (var f in files)
            {
                if (total <= DirCapBytes) break;
                try
                {
                    total -= f.Length;
                    f.Delete();
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }
}
