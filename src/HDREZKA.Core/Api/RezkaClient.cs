using System.Net;
using System.Text.Json;

namespace HDREZKA.Core.Api;

public sealed class RezkaClientOptions
{
    public string Mirror { get; set; } = "https://hdrzk.org/";
    public string UserAgent { get; set; } = BuildInfo.UserAgent;
    public bool UseAndroidHeaders { get; set; } = true;
    public string AndroidAppVersion { get; set; } = "2.2.2";

    /// <summary>
    /// Подмена транспорта (для тестов ретраев/редиректов/403). null — обычный
    /// HttpClientHandler с CookieContainer приложения.
    /// </summary>
    public HttpMessageHandler? MessageHandler { get; set; }
}

public sealed class RezkaClient : IDisposable
{
    private HttpClient _http;
    private readonly CookieContainer _cookies;
    private readonly SemaphoreSlim _requestGate = new(2, 2);
    private bool _disposed;

    public RezkaClientOptions Options { get; private set; }

    /// <summary>
    /// Raised when <see cref="Options.UseAndroidHeaders"/> was auto-toggled
    /// after a 403 (ported from the mac client which retries once with
    /// flipped headers). UI layer should persist the working value.
    /// </summary>
    public event Action? HeadersFlipped;

    public RezkaClient(RezkaClientOptions? options = null)
    {
        Options = options ?? new RezkaClientOptions();
        _cookies = new CookieContainer();
        _http = CreateHttp();
        ApplyHeaders();
    }

    private HttpClient CreateHttp()
    {
        var custom = Options.MessageHandler;
        HttpMessageHandler handler = custom ?? new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.All,
        };

        // Кастомный handler принадлежит вызывающему (тесты переиспользуют его
        // между клиентами) — его не диспозим.
        return new HttpClient(handler, disposeHandler: custom == null)
        {
            BaseAddress = new Uri(Options.Mirror),
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    private void ApplyHeaders()
    {
        _http.DefaultRequestHeaders.Clear();
        if (Options.UseAndroidHeaders)
        {
            _http.DefaultRequestHeaders.Add("X-Hdrezka-Android-App", "1");
            _http.DefaultRequestHeaders.Add("X-Hdrezka-Android-App-Version", Options.AndroidAppVersion);
        }

        _http.DefaultRequestHeaders.UserAgent.ParseAdd(Options.UserAgent);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _http.Dispose(); } catch { }
        try { _requestGate.Dispose(); } catch { }
    }

    public void SetMirror(string mirror)
    {
        if (string.Equals(Options.Mirror, mirror, StringComparison.OrdinalIgnoreCase))
        {
            ApplyHeaders();
            return;
        }

        Options.Mirror = mirror;
        var old = _http;
        _http = CreateHttp();
        ApplyHeaders();

        try
        {
            old.Dispose();
        }
        catch
        {
            // in-flight requests may still hold the old client
        }
    }

    public bool IsLoggedIn
    {
        get
        {
            try
            {
                return _cookies.GetCookies(new Uri(Options.Mirror)).Any(c => c.Name == "dle_user_id");
            }
            catch
            {
                return false;
            }
        }
    }

    // ---------- persisted session ----------

    private sealed record StoredCookie(
        string Name,
        string Value,
        string Domain,
        string Path,
        DateTime? Expires,
        bool Secure,
        bool HttpOnly);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void SaveCookies(string filePath)
    {
        try
        {
            var uri = new Uri(Options.Mirror);
            var stored = _cookies.GetCookies(uri)
                .Cast<Cookie>()
                .Select(c => new StoredCookie(
                    c.Name,
                    c.Value,
                    c.Domain,
                    c.Path,
                    c.Expires == DateTime.MinValue ? null : c.Expires.ToUniversalTime(),
                    c.Secure,
                    c.HttpOnly))
                .ToList();

            // Файл cookies = токен аккаунта: на диске только в зашифрованном виде.
            CookieVault.Write(filePath, JsonSerializer.Serialize(stored));
        }
        catch
        {
            // best effort
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void LoadCookies(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return;
            var result = CookieVault.Read(filePath);
            var stored = JsonSerializer.Deserialize<List<StoredCookie>>(result.Json);
            if (stored == null) return;
            foreach (var s in stored)
            {
                if (string.IsNullOrEmpty(s.Name) || string.IsNullOrEmpty(s.Domain)) continue;
                if (s.Expires.HasValue && s.Expires.Value < DateTime.UtcNow) continue;
                var cookie = new Cookie(s.Name, s.Value ?? "", s.Path ?? "/", s.Domain)
                {
                    Secure = s.Secure,
                    HttpOnly = s.HttpOnly,
                };
                if (s.Expires.HasValue) cookie.Expires = s.Expires.Value;
                _cookies.Add(cookie);
            }

            // Старый открытый файл сразу перешифровываем.
            if (result.WasPlaintext)
            {
                CookieVault.Write(filePath, result.Json);
            }
        }
        catch
        {
            // corrupt file -> start fresh
        }
    }

    public static void DeleteCookies(string filePath)
    {
        try
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
        catch
        {
        }
    }

    public void ClearSession()
    {
        // CookieContainer has no removal API: overwrite with expired copies
        try
        {
            var uri = new Uri(Options.Mirror);
            foreach (var c in _cookies.GetCookies(uri).Cast<Cookie>().ToList())
            {
                _cookies.Add(new Cookie(c.Name, "", c.Path, c.Domain)
                {
                    Expires = DateTime.UtcNow.AddDays(-1),
                });
            }
        }
        catch
        {
        }
    }

    // ---------- http helpers ----------

    private static string PageUrl(string path, int page, IReadOnlyDictionary<string, string?>? query = null)
    {
        if (page > 1)
        {
            path = path.TrimEnd('/') + $"/page/{page}/";
        }

        if (query != null)
        {
            var pairs = query.Where(kv => kv.Value != null)
                .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}");
            var qs = string.Join("&", pairs);
            if (qs.Length > 0) path += "?" + qs;
        }

        return path;
    }

    // 404 сюда намеренно не входит: удалённый тайтл/персона не «восстановится»
    // повтором, а каждая лишняя попытка — это +1.2 с ожидания в UI.
    private static bool IsTransient(HttpStatusCode code) =>
        code is HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    private static Task BackoffDelay(int attempt, CancellationToken ct) =>
        Task.Delay(400 * attempt + Random.Shared.Next(200), ct);

    private void FlipHeadersForRetry()
    {
        Options.UseAndroidHeaders = !Options.UseAndroidHeaders;
        ApplyHeaders();
        try { HeadersFlipped?.Invoke(); } catch { }
    }

    private static bool IsRedirect(HttpStatusCode code) =>
        code is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect
            or (HttpStatusCode)308;

    /// <summary>
    /// Sends the request, manually following redirects left over by the
    /// handler — notably https→http downgrades (person pages!), which
    /// HttpClientHandler refuses to follow but browsers accept.
    /// Returns the final response (caller disposes).
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRedirectsAsync(
        Func<HttpRequestMessage> createRequest, bool isPost, CancellationToken ct,
        bool allowInsecureDowngrade = true)
    {
        var method = isPost ? HttpMethod.Post : HttpMethod.Get;
        Uri? nextUri = null;
        HttpResponseMessage? resp = null;
        try
        {
            for (var i = 0; i < 5; i++)
            {
                using var req = createRequest();
                if (nextUri != null)
                {
                    req.RequestUri = nextUri;
                    if (isPost && method == HttpMethod.Get)
                    {
                        // 301/302/303 convert POST to GET (standard).
                        req.Method = HttpMethod.Get;
                        req.Content?.Dispose();
                        req.Content = null;
                    }
                    else
                    {
                        req.Method = method;
                    }
                }

                // Effective URL (factory requests are mirror-relative).
                var effective = req.RequestUri?.IsAbsoluteUri == true
                    ? req.RequestUri
                    : new Uri(new Uri(Options.Mirror), req.RequestUri?.OriginalString ?? string.Empty);

                resp?.Dispose();
                resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
                if (!IsRedirect(resp.StatusCode) || resp.Headers.Location == null) return resp;

                var location = resp.Headers.Location;
                nextUri = location.IsAbsoluteUri ? location : new Uri(effective, location);

                // Понижение https -> http допустимо только для публичных GET
                // (персоны так отдают страницы), но не для форм с паролем.
                if (!allowInsecureDowngrade
                    && effective.Scheme == Uri.UriSchemeHttps
                    && nextUri.Scheme == Uri.UriSchemeHttp)
                {
                    throw RezkaException.InsecureMirror();
                }

                if (isPost && resp.StatusCode is HttpStatusCode.MovedPermanently
                    or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod)
                {
                    method = HttpMethod.Get;
                }
            }

            return resp!;
        }
        catch
        {
            resp?.Dispose();
            throw;
        }
    }

    private async Task<string> GetStringAsync(string pathAndQuery, CancellationToken ct)
    {
        await _requestGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var flipped = false;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    using var resp = await SendWithRedirectsAsync(
                        () => new HttpRequestMessage(HttpMethod.Get, pathAndQuery), false, ct).ConfigureAwait(false);
                    if (resp.StatusCode == HttpStatusCode.Forbidden)
                    {
                        // Port of mac CustomInterceptor: one retry with flipped
                        // X-Hdrezka-Android-App headers — some mirrors/WAFs
                        // accept only one of the two variants.
                        if (!flipped)
                        {
                            flipped = true;
                            FlipHeadersForRetry();
                            continue;
                        }

                        throw IsLoggedIn
                            ? RezkaException.LoginRequired()
                            : RezkaException.AccessDenied();
                    }

                    if (!resp.IsSuccessStatusCode)
                    {
                        if (attempt < 3 && IsTransient(resp.StatusCode))
                        {
                            await BackoffDelay(attempt, ct).ConfigureAwait(false);
                            continue;
                        }

                        // 404/410 — тайтл удалён/скрыт, повтор не поможет.
                        throw resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone
                            ? RezkaException.NotFound()
                            : RezkaException.Site($"HTTP {(int)resp.StatusCode}");
                    }

                    return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                }
                catch (RezkaException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    // Отмена со стороны UI (ушли со страницы) — не ретраим.
                    if (ct.IsCancellationRequested) throw;
                    if (attempt >= 3)
                    {
                        throw new RezkaException(RezkaError.Network, ex.Message);
                    }

                    await BackoffDelay(attempt, ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private async Task<string> PostFormAsync(string pathAndQuery, IReadOnlyDictionary<string, string?> form, CancellationToken ct)
    {
        await _requestGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var flipped = false;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    using var resp = await SendWithRedirectsAsync(
                        () => new HttpRequestMessage(HttpMethod.Post, pathAndQuery)
                        {
                            Content = new FormUrlEncodedContent(form.Where(kv => kv.Value != null)
                                .Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value!))),
                        }, isPost: true, ct: ct, allowInsecureDowngrade: false).ConfigureAwait(false);
                    if (resp.StatusCode == HttpStatusCode.Forbidden)
                    {
                        // Same 403-flip retry as GET (covers ajax/login/).
                        if (!flipped)
                        {
                            flipped = true;
                            FlipHeadersForRetry();
                            continue;
                        }

                        throw IsLoggedIn
                            ? RezkaException.LoginRequired()
                            : RezkaException.AccessDenied();
                    }

                    if (!resp.IsSuccessStatusCode)
                    {
                        if (attempt < 3 && IsTransient(resp.StatusCode))
                        {
                            await BackoffDelay(attempt, ct).ConfigureAwait(false);
                            continue;
                        }

                        // 404/410 — тайтл удалён/скрыт, повтор не поможет.
                        throw resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone
                            ? RezkaException.NotFound()
                            : RezkaException.Site($"HTTP {(int)resp.StatusCode}");
                    }

                    return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                }
                catch (RezkaException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    // Отмена со стороны UI (ушли со страницы) — не ретраим.
                    if (ct.IsCancellationRequested) throw;
                    if (attempt >= 3)
                    {
                        throw new RezkaException(RezkaError.Network, ex.Message);
                    }

                    await BackoffDelay(attempt, ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private static long Ts() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // ---------- catalogs ----------

    public Task<(string Title, IReadOnlyList<MovieSimple> Items)> GetHomeListAsync(ListFilter filter, int genre = 0, int page = 1, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?> { ["filter"] = filter.ToQuery() };
        if (genre != 0) query["genre"] = genre.ToString();
        return GetListAsync(PageUrl("", page, query), ct);
    }

    public Task<(string Title, IReadOnlyList<MovieSimple> Items)> GetSectionListAsync(Section section, ListFilter filter, int genre = 0, int page = 1, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?> { ["filter"] = filter.ToQuery() };
        if (genre != 0) query["genre"] = genre.ToString();
        return GetListAsync(PageUrl(section.ToPath() + "/", page, query), ct);
    }

    public Task<(string Title, IReadOnlyList<MovieSimple> Items)> GetNewestListAsync(ListFilter filter, int genre = 0, int page = 1, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?> { ["filter"] = filter.ToQuery() };
        if (genre != 0) query["genre"] = genre.ToString();
        return GetListAsync(PageUrl("new/", page, query), ct);
    }

    public Task<(string Title, IReadOnlyList<MovieSimple> Items)> GetYearListAsync(Section section, ListFilter filter, string year, int page = 1, CancellationToken ct = default)
    {
        var path = $"{section.ToPath()}/{(filter == ListFilter.Popular ? "popular" : "best")}/{year}/";
        return GetListAsync(PageUrl(path, page), ct);
    }

    public Task<(string Title, IReadOnlyList<MovieSimple> Items)> GetCollectionsListAsync(string collectionId, int page = 1, CancellationToken ct = default)
    {
        var path = $"collections/{collectionId}/";
        return GetListAsync(PageUrl(path, page), ct);
    }

    public async Task<IReadOnlyList<CollectionSimple>> GetAllCollectionsAsync(CancellationToken ct = default)
    {
        // HDRezka collections use /collections/page/N/, not a single index.
        // A conservative cap plus duplicate-page detection protects against a
        // mirror that ignores /page/N/ and returns the first page repeatedly.
        const int hardPageLimit = 25;
        var result = new List<CollectionSimple>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var page = 1; page <= hardPageLimit; page++)
        {
            ct.ThrowIfCancellationRequested();
            string html;
            try
            {
                html = await GetStringAsync(PageUrl("collections/", page), ct)
                    .ConfigureAwait(false);
            }
            catch (RezkaException ex) when (page > 1 &&
                ex.Kind == RezkaError.NotFound)
            {
                break; // end of pagination on mirrors returning a 404
            }
            var items = Parsers.ParseCollectionsList(html);
            if (items.Count == 0) break;
            var fresh = 0;
            foreach (var item in items)
                if (seen.Add(item.Id))
                {
                    result.Add(item);
                    fresh++;
                }
            if (fresh == 0) break; // mirror repeated a page (or looped)
            // If the website exposes pagination, stop at its last page. When
            // absent, probe the next page, stopping on empty/duplicate/404.
            var lastPage = Parsers.ParseCollectionsLastPage(html);
            if (lastPage is { } last && page >= last) break;
        }

        return result;
    }

    public Task<(string Title, IReadOnlyList<MovieSimple> Items)> GetSourceSeriesListAsync(
        string sourcePath, int page = 1, CancellationToken ct = default)
    {
        // A source "Из серии" entry can be a category/year/best list rather
        // than a real /collections/ folder. Only permit local browse paths.
        var path = sourcePath.Trim('/');
        if (!System.Text.RegularExpressions.Regex.IsMatch(path,
            @"^(films|series|animation|cartoons|show)/(best|popular|year|genre|country|new)/[a-z0-9_-]+(?:/[a-z0-9_-]+)?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new ArgumentException("Unsupported source list path", nameof(sourcePath));
        return GetListAsync(PageUrl(path + "/", page), ct);
    }

    public Task<(string Title, IReadOnlyList<MovieSimple> Items)> GetGenreListAsync(
        string genrePath, int page = 1, CancellationToken ct = default)
    {
        // Only a same-site genre route is accepted; never make the application
        // follow an arbitrary URL supplied by page markup.
        var normalized = genrePath.Trim('/');
        if (!normalized.StartsWith("genre/", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Contains("/genre/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Expected a genre page path", nameof(genrePath));
        if (normalized.Contains("..", StringComparison.Ordinal) ||
            normalized.Contains("://", StringComparison.Ordinal) ||
            normalized.Contains('?') ||
            normalized.Contains('#'))
            throw new ArgumentException("Unsafe genre path", nameof(genrePath));
        return GetListAsync(PageUrl(normalized + "/", page), ct);
    }

    public async Task<IReadOnlyList<MovieSimple>> GetHotMoviesAsync(int genre = 0, CancellationToken ct = default)
    {
        var body = await PostFormAsync("engine/ajax/get_newest_slider_content.php", new Dictionary<string, string?> { ["id"] = genre.ToString() }, ct).ConfigureAwait(false);
        return Parsers.ParseHotMovies(body);
    }

    public Task<(string Title, IReadOnlyList<MovieSimple> Items)> SearchAsync(string query, int page = 1, CancellationToken ct = default)
    {
        var qs = new Dictionary<string, string?>
        {
            ["do"] = "search",
            ["subaction"] = "search",
            ["q"] = query,
            ["page"] = page.ToString(),
        };
        return GetListAsync("search/" + (qs.Count > 0
            ? "?" + string.Join("&", qs.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}"))
            : ""), ct);
    }

    private async Task<(string Title, IReadOnlyList<MovieSimple> Items)> GetListAsync(string pathAndQuery, CancellationToken ct)
    {
        var html = await GetStringAsync(pathAndQuery, ct).ConfigureAwait(false);
        return Parsers.ParseMovieList(html);
    }

    // ---------- details / video ----------

    public async Task<MovieDetailed> GetDetailsAsync(string pagePath, CancellationToken ct = default)
    {
        var html = await GetStringAsync(pagePath, ct).ConfigureAwait(false);
        return Parsers.ParseDetails(html, pagePath);
    }

    public async Task<PersonDetailed> GetPersonAsync(string pagePath, CancellationToken ct = default)
    {
        var html = await GetStringAsync(pagePath, ct).ConfigureAwait(false);
        return Parsers.ParsePerson(html, pagePath);
    }

    public async Task<IReadOnlyList<MovieSeason>> GetSeriesSeasonsAsync(string movieId, MovieVoiceActing voice, string favs, CancellationToken ct = default)
    {
        if (voice.Url == null)
        {
            var body = await PostFormAsync(
                $"ajax/get_cdn_series/?t={Ts()}",
                new Dictionary<string, string?>
                {
                    ["id"] = movieId,
                    ["translator_id"] = voice.TranslatorId,
                    ["action"] = "get_episodes",
                    ["favs"] = favs,
                },
                ct).ConfigureAwait(false);
            return Parsers.ParseSeasonsResponse(body);
        }

        var html = await GetStringAsync(voice.Url, ct).ConfigureAwait(false);
        var doc = Parsers.ParseHtml(html);
        return Parsers.ParseSeasonsFromDocument(doc);
    }

    public async Task<MovieVideo> GetMovieVideoAsync(MovieVoiceActing voice, MovieSeason? season, MovieEpisode? episode, string favs, CancellationToken ct = default)
    {
        var url = episode?.Url ?? voice.Url;
        string body;
        if (url == null)
        {
            var form = new Dictionary<string, string?>
            {
                ["id"] = voice.VoiceId,
                ["translator_id"] = voice.TranslatorId,
                ["favs"] = favs,
            };

            if (voice.IsCamrip.Length > 0) form["is_camrip"] = voice.IsCamrip;
            if (voice.IsAds.Length > 0) form["is_ads"] = voice.IsAds;
            if (voice.IsDirector.Length > 0) form["is_director"] = voice.IsDirector;

            if (season != null && episode != null)
            {
                form["season"] = season.SeasonId;
                form["episode"] = episode.EpisodeId;
                form["action"] = "get_stream";
            }
            else
            {
                form["action"] = "get_movie";
            }

            body = await PostFormAsync($"ajax/get_cdn_series/?t={Ts()}", form, ct).ConfigureAwait(false);
        }
        else
        {
            body = await GetStringAsync(url, ct).ConfigureAwait(false);
        }

        return Parsers.ParseVideoResponse(body);
    }

    // ---------- account ----------

    public async Task SignInAsync(string login, string password, CancellationToken ct = default)
    {
        // Пароль уходит только по https: http-зеркало = передача открытым текстом.
        if (!Uri.TryCreate(Options.Mirror, UriKind.Absolute, out var mirrorUri)
            || mirrorUri.Scheme != Uri.UriSchemeHttps)
        {
            throw RezkaException.InsecureMirror();
        }

        var body = await PostFormAsync(
            "ajax/login/",
            new Dictionary<string, string?>
            {
                ["login_name"] = login,
                ["login_password"] = password,
                ["login_not_save"] = "0",
            },
            ct).ConfigureAwait(false);
        Parsers.ParseLoginResponse(body);
    }

    public Task LogoutAsync(CancellationToken ct = default) => GetStringAsync("logout/", ct);

    public async Task<IReadOnlyList<BookmarkCategory>> GetBookmarkCategoriesAsync(CancellationToken ct = default)
    {
        var html = await GetStringAsync("favorites/", ct).ConfigureAwait(false);
        return Parsers.ParseBookmarksCategories(html);
    }

    public Task<(string Title, IReadOnlyList<MovieSimple> Items)> GetBookmarksAsync(int categoryId, ListFilter filter = ListFilter.Latest, int genre = 0, int page = 1, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?> { ["filter"] = filter.ToQuery() };
        if (genre != 0) query["genre"] = genre.ToString();
        return GetListAsync(PageUrl($"favorites/{categoryId}/", page, query), ct);
    }

    public Task AddToBookmarksAsync(string movieId, int categoryId, CancellationToken ct = default) =>
        PostFormAsync(
            "ajax/favorites/",
            new Dictionary<string, string?>
            {
                ["post_id"] = movieId,
                ["cat_id"] = categoryId.ToString(),
                ["action"] = "add_post",
            },
            ct);

    public Task RemoveFromBookmarksAsync(IReadOnlyList<string> movieIds, int categoryId, CancellationToken ct = default) =>
        PostFormAsync(
            "ajax/favorites/",
            new Dictionary<string, string?>
            {
                ["items"] = string.Join(",", movieIds),
                ["cat_id"] = categoryId.ToString(),
                ["action"] = "remove_items",
            },
            ct);

    public Task MoveBetweenBookmarksAsync(IReadOnlyList<string> movieIds, int fromCatId, int toCatId, CancellationToken ct = default) =>
        PostFormAsync(
            "ajax/favorites/",
            new Dictionary<string, string?>
            {
                ["from_cat_id"] = fromCatId.ToString(),
                ["to_cat_id"] = toCatId.ToString(),
                ["items"] = string.Join(",", movieIds),
                ["action"] = "change_items_cat",
            },
            ct);

    public Task CreateBookmarkCategoryAsync(string name, CancellationToken ct = default) =>
        PostFormAsync("ajax/favorites/", new Dictionary<string, string?> { ["name"] = name, ["action"] = "add_cat" }, ct);

    public Task DeleteBookmarkCategoryAsync(int categoryId, CancellationToken ct = default) =>
        PostFormAsync("ajax/favorites/", new Dictionary<string, string?> { ["cat_id"] = categoryId.ToString(), ["action"] = "remove_cat" }, ct);

    public Task SaveWatchingAsync(MovieVoiceActing voice, MovieSeason? season, MovieEpisode? episode, int? currentTime, int? duration, CancellationToken ct = default) =>
        PostFormAsync(
            $"ajax/send_save/?t={Ts()}",
            new Dictionary<string, string?>
            {
                ["post_id"] = voice.VoiceId,
                ["translator_id"] = voice.TranslatorId,
                ["season"] = season?.SeasonId ?? "0",
                ["episode"] = episode?.EpisodeId ?? "0",
                ["current_time"] = currentTime?.ToString(),
                ["duration"] = duration != 1 ? duration?.ToString() : null,
            },
            ct);

    // ---------- continue watching / account history ----------

    public async Task<IReadOnlyList<AccountWatchItem>> GetContinueWatchingAsync(CancellationToken ct = default)
    {
        var html = await GetStringAsync("continue/", ct).ConfigureAwait(false);
        return Parsers.ParseContinueWatching(html);
    }

    public Task MarkWatchedItemAsync(string dataId, CancellationToken ct = default) =>
        PostFormAsync("engine/ajax/cdn_saves_view.php", new Dictionary<string, string?> { ["id"] = dataId }, ct);

    public Task RemoveWatchedItemAsync(string dataId, CancellationToken ct = default) =>
        PostFormAsync("engine/ajax/cdn_saves_remove.php", new Dictionary<string, string?> { ["id"] = dataId }, ct);

    // ---------- comments ----------

    public async Task<IReadOnlyList<CommentItem>> GetCommentsAsync(string movieId, int page = 1, string? commentId = null, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["news_id"] = movieId,
            ["cstart"] = page.ToString(),
            ["t"] = Ts().ToString(),
            ["type"] = "0",
            ["comment_id"] = commentId ?? "0",
            ["skin"] = "hdrezka",
        };
        var qs = string.Join("&", query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value!)}"));
        var body = await GetStringAsync("ajax/get_comments/?" + qs, ct).ConfigureAwait(false);
        return Parsers.ParseCommentsResponse(body);
    }

    public async Task<string> SendCommentAsync(string postId, string text, string? parentId = null, string? adb = null, CancellationToken ct = default)
    {
        var body = await PostFormAsync(
            "ajax/add_comment/",
            new Dictionary<string, string?>
            {
                ["parent"] = parentId ?? "0",
                ["replyto_id"] = parentId,
                ["post_id"] = postId,
                ["name"] = null,
                ["comments"] = text,
                ["type"] = "0",
                ["has_adb"] = adb ?? "1",
                ["g_recaptcha_response"] = "",
            },
            ct).ConfigureAwait(false);

        return ValidatePostedComment(body);
    }

    /// <summary>
    /// DLE mirrors have sent success as true/1/"1". Treat missing/unknown
    /// replies as failures rather than clearing the user's unsent draft.
    /// Do not guess whether CAPTCHA / an expired session can be bypassed.
    /// </summary>
    public static string ValidatePostedComment(string body)
    {
        JsonDocument json;
        try { json = JsonDocument.Parse(body); }
        catch (JsonException)
        {
            throw RezkaException.Site(
                "Сайт вернул не JSON, а страницу ошибки или авторизации. " +
                "Проверьте зеркало и войдите в аккаунт повторно.");
        }

        using (json)
        {
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw RezkaException.Site("Некорректный ответ сайта на комментарий.");
            if (!root.TryGetProperty("success", out var value))
                throw RezkaException.Site(ExtractCommentError(root) ??
                    "Сайт не подтвердил отправку комментария. Попробуйте на сайте.");

            var accepted = value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.Number => value.TryGetInt32(out var count) && count > 0,
                JsonValueKind.String => value.GetString()?.Trim().ToLowerInvariant()
                    is "1" or "true" or "ok" or "success",
                _ => false
            };
            if (!accepted)
                throw RezkaException.Site(ExtractCommentError(root) ??
                    "Сайт отказал в отправке. Возможно, нужна повторная авторизация или CAPTCHA.");

            return root.TryGetProperty("comment", out var posted) &&
                posted.ValueKind == JsonValueKind.String ? posted.GetString() ?? "" : "";
        }
    }

    private static string? ExtractCommentError(JsonElement root)
    {
        foreach (var key in new[] { "message", "msg", "error", "reason" })
        {
            if (!root.TryGetProperty(key, out var value)) continue;
            var raw = value.ValueKind == JsonValueKind.String
                ? value.GetString() : value.ToString();
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var plain = System.Net.WebUtility.HtmlDecode(
                System.Text.RegularExpressions.Regex.Replace(raw,
                    "<[^>]*>", " ")).Trim();
            if (!string.IsNullOrWhiteSpace(plain))
                return plain.Length > 350 ? plain[..350] : plain;
        }
        return null;
    }

    public async Task<bool> ToggleCommentLikeAsync(string commentId, CancellationToken ct = default)
    {
        var body = await PostFormAsync("engine/ajax/comments_like.php", new Dictionary<string, string?> { ["id"] = commentId }, ct).ConfigureAwait(false);
        using var json = JsonDocument.Parse(body);
        return json.RootElement.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
    }

    public Task RateAsync(string newsId, int rating, CancellationToken ct = default) =>
        PostFormAsync(
            "engine/ajax/rating.php",
            new Dictionary<string, string?>
            {
                ["news_id"] = newsId,
                ["go_rate"] = rating.ToString(),
                ["skin"] = "hdrezka",
            },
            ct);
}
