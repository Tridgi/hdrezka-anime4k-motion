using System.Text.Json;

namespace HDREZKA.App.Services;

public sealed record WatchPosition(
    string MovieId,
    string TranslatorId,
    string? SeasonId,
    string? EpisodeId,
    double PositionSeconds,
    double DurationSeconds,
    DateTime UpdatedAt,
    string? SubtitleLang = null,
    string? Quality = null,
    string? Title = null,
    string? PosterUrl = null,
    string? PagePath = null);

public sealed class PositionService
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HDREZKA-Anime4K-Motion");

    private static readonly string FilePath = Path.Combine(Dir, "positions.json");
    private static readonly string WatchedFilePath = Path.Combine(Dir, "watched.json");
    private static readonly string ActiveFilePath = Path.Combine(Dir, "active-playback.json");

    public static PositionService Instance { get; } = new();

    // Историю читает и фон (SeedFromHistory при проверке обновлений сериалов),
    // а пишет UI — поэтому доступ к словарям синхронизирован.
    private readonly object _lock = new();

    private Dictionary<string, WatchPosition> _positions = new();
    private HashSet<string> _watched = new();
    // One active episode per title, independent of per-voice/episode positions.
    private Dictionary<string, string> _activeEpisodeKeys = new(StringComparer.OrdinalIgnoreCase);

    private PositionService()
    {
        Load();
        LoadWatched();
        LoadActive();
    }

    private static string Key(string movieId, string translatorId, string? season, string? episode) =>
        $"{movieId}|{translatorId}|{season ?? "0"}|{episode ?? "0"}";

    public WatchPosition? Get(string movieId, string translatorId, string? season, string? episode)
    {
        lock (_lock)
        {
            _positions.TryGetValue(Key(movieId, translatorId, season, episode), out var pos);
            return pos;
        }
    }

    private static bool IsResumable(WatchPosition p) =>
        p.DurationSeconds > 60 && p.PositionSeconds > 30 &&
        p.PositionSeconds < p.DurationSeconds - 30;

    public IReadOnlyList<WatchPosition> GetAll()
    {
        lock (_lock)
            return _positions.Values.Where(IsResumable)
                .OrderByDescending(p => p.UpdatedAt).ToList();
    }

    /// <summary>One newest resumable card per work, regardless of season or voice.</summary>
    public IReadOnlyList<WatchPosition> GetContinueTitles()
    {
        lock (_lock)
        {
            return _positions.Values.Where(IsResumable)
                .GroupBy(p => p.MovieId, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var first = g.OrderByDescending(p => p.UpdatedAt).First();
                    return _activeEpisodeKeys.TryGetValue(g.Key, out var active) &&
                           _positions.TryGetValue(active, out var preferred) &&
                           IsResumable(preferred) ? preferred : first;
                })
                .OrderByDescending(p => p.UpdatedAt)
                .ToList();
        }
    }

    /// <summary>Only a successful media-open changes the active episode;
    /// background position saves must never switch the user's active series.</summary>
    public void SetActive(string movieId, string translatorId, string? season, string? episode)
    {
        lock (_lock)
        {
            var key = Key(movieId, translatorId, season, episode);
            if (_activeEpisodeKeys.GetValueOrDefault(movieId) == key) return;
            _activeEpisodeKeys[movieId] = key;
            PersistActive();
            Log.Info($"[PlaybackHistory] active movie={movieId} voice={translatorId} season={season ?? "0"} episode={episode ?? "0"}");
        }
    }

    /// <summary>
    /// Последняя позиция по тайтлу без фильтров «недосмотрено» — нужна, чтобы
    /// открыть фильм в той же озвучке/сезоне, где пользователь остановился
    /// (у разных озвучек разный набор сезонов).
    /// </summary>
    public WatchPosition? GetLatestFor(string movieId, string? pagePath = null)
    {
        lock (_lock)
        {
            var relevant = _positions.Values
                .Where(p => p.MovieId == movieId || (pagePath != null && p.PagePath == pagePath))
                .ToList();
            // An episode intentionally started by the user wins over an older
            // voice or the last timer write from a previously played episode.
            var active = relevant.FirstOrDefault(p =>
                _activeEpisodeKeys.TryGetValue(p.MovieId, out var key) &&
                key == Key(p.MovieId, p.TranslatorId, p.SeasonId, p.EpisodeId));
            return active ?? relevant.OrderByDescending(p => p.UpdatedAt).FirstOrDefault();
        }
    }

    public void Save(WatchPosition position)
    {
        lock (_lock)
        {
            _positions[Key(position.MovieId, position.TranslatorId, position.SeasonId, position.EpisodeId)] = position;
            Persist();
        }
    }

    public void Remove(string movieId)
    {
        lock (_lock)
        {
            var keys = _positions
                .Where(kv => kv.Value.MovieId == movieId || kv.Value.PagePath == movieId)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var key in keys)
            {
                _positions.Remove(key);
            }

            if (keys.Count > 0) Persist();
            if (_activeEpisodeKeys.Remove(movieId)) PersistActive();

            // Drop manual "watched" checkmarks for the same title.
            var watchedKeys = _watched
                .Where(k => k.Split('|')[0] == movieId)
                .ToList();
            if (watchedKeys.Count > 0)
            {
                foreach (var key in watchedKeys) _watched.Remove(key);
                PersistWatched();
            }
        }
    }

    // ---------- manual "watched" checkmarks (offline, per voice/season/episode) ----------

    public bool IsWatched(string movieId, string translatorId, string? season, string? episode)
    {
        lock (_lock)
        {
            return _watched.Contains(Key(movieId, translatorId, season, episode));
        }
    }

    /// <returns>New state.</returns>
    public bool ToggleWatched(string movieId, string translatorId, string? season, string? episode)
    {
        lock (_lock)
        {
            var key = Key(movieId, translatorId, season, episode);
            bool now;
            if (_watched.Contains(key))
            {
                _watched.Remove(key);
                now = false;
            }
            else
            {
                _watched.Add(key);
                now = true;
            }

            PersistWatched();
            return now;
        }
    }

    /// <summary>
    /// Idempotent "watched" mark: used when an episode/film plays to the end.
    /// Unlike <see cref="ToggleWatched"/> it never clears an existing mark.
    /// </summary>
    public void MarkWatched(string movieId, string translatorId, string? season, string? episode)
    {
        lock (_lock)
        {
            if (_watched.Add(Key(movieId, translatorId, season, episode)))
            {
                PersistWatched();
            }
        }
    }

    private void LoadActive()
    {
        try
        {
            if (File.Exists(ActiveFilePath))
                _activeEpisodeKeys = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    File.ReadAllText(ActiveFilePath)) ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Warn("[PlaybackHistory] Cannot load active-playback.json: " + ex.Message);
            _activeEpisodeKeys = new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void PersistActive()
    {
        WriteAtomically(ActiveFilePath, _activeEpisodeKeys);
    }

    private static void WriteAtomically<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Warn("[PlaybackHistory] Save failed: " + ex.Message);
        }
    }

    private void LoadWatched()
    {
        try
        {
            if (!File.Exists(WatchedFilePath)) return;
            var json = File.ReadAllText(WatchedFilePath);
            _watched = JsonSerializer.Deserialize<HashSet<string>>(json) ?? new();
        }
        catch
        {
            _watched = new();
        }
    }

    private void PersistWatched()
    {
        try
        {
            WriteAtomically(WatchedFilePath, _watched);
        }
        catch
        {
            // best effort
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var json = File.ReadAllText(FilePath);
            _positions = JsonSerializer.Deserialize<Dictionary<string, WatchPosition>>(json) ?? new();
        }
        catch
        {
            _positions = new();
        }
    }

    private void Persist()
    {
        try
        {
            WriteAtomically(FilePath, _positions);
        }
        catch
        {
            // best effort
        }
    }
}
