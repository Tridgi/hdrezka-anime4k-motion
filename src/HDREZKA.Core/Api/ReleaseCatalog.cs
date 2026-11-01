using System.Text.Json;

namespace HDREZKA.Core.Api;

public enum UpdateChannel { Stable, Testing }

public sealed record ChannelRelease(
    string Tag, string Version, string Url, string Notes, string AssetUrl,
    string AssetName, string? Sha256, string? ManifestUrl, string Commit,
    DateTimeOffset PublishedAt, UpdateChannel Channel);

/// <summary>
/// GitHub Releases are the distribution channel. GitHub Actions artifacts
/// expire and their REST downloads require authentication, so are not used.
/// </summary>
public static class ReleaseCatalog
{
    public const string Repository = "Tridgi/hdrezka-anime4k-motion";
    public const string ApiUrl = "https://api.github.com/repos/" + Repository + "/releases?per_page=50";
    public const string ReleasesUrl = "https://github.com/" + Repository + "/releases";

    public static ChannelRelease? Latest(string json, UpdateChannel channel)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
        var found = new List<ChannelRelease>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object) continue;
            if (GetBool(release, "draft") || GetBool(release, "prerelease") != (channel == UpdateChannel.Testing)) continue;
            var tag = Get(release, "tag_name");
            if (tag == null || (channel == UpdateChannel.Testing && !tag.StartsWith("test-", StringComparison.OrdinalIgnoreCase))) continue;
            if (channel == UpdateChannel.Stable && tag.StartsWith("test-", StringComparison.OrdinalIgnoreCase)) continue;
            var releaseUrl = Get(release, "html_url");
            var commit = Get(release, "target_commitish") ?? "";
            if (releaseUrl == null || !IsTrustedReleaseUrl(releaseUrl)) continue;
            if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;
            string? assetUrl = null, assetName = null, sha = null, manifestUrl = null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = Get(asset, "name");
                var url = Get(asset, "browser_download_url");
                if (name == null || url == null || !IsTrustedReleaseUrl(url)) continue;
                if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) manifestUrl = url;
                if (name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase) &&
                    name.StartsWith("HDREZKA-", StringComparison.OrdinalIgnoreCase))
                {
                    assetUrl = url;
                    assetName = name;
                    sha = ReleaseIntegrity.NormalizeSha256(Get(asset, "digest"));
                }
            }
            if (assetUrl == null || assetName == null) continue;
            var dateText = Get(release, "published_at") ?? Get(release, "created_at");
            DateTimeOffset.TryParse(dateText, out var published);
            var version = channel == UpdateChannel.Stable ? tag.TrimStart('v', 'V') : tag;
            found.Add(new ChannelRelease(tag, version, releaseUrl, Get(release, "body") ?? "",
                assetUrl, assetName, sha, manifestUrl, commit, published, channel));
        }
        return found.OrderByDescending(r => r.PublishedAt).FirstOrDefault();
    }

    public static bool IsTrustedReleaseUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && uri.Scheme == Uri.UriSchemeHttps
           && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
           && uri.AbsolutePath.StartsWith("/" + Repository + "/releases/", StringComparison.OrdinalIgnoreCase);

    public static bool IsSameBuild(ChannelRelease release, string? installedTag, string? installedCommit)
    {
        if (!string.IsNullOrWhiteSpace(installedTag) &&
            string.Equals(installedTag, release.Tag, StringComparison.OrdinalIgnoreCase)) return true;
        // GitHub release target_commitish may be a branch name; compare only actual hashes.
        var commit = release.Commit;
        return commit.Length == 40 && commit.All(Uri.IsHexDigit)
            && !string.IsNullOrWhiteSpace(installedCommit) && installedCommit.Length >= 7
            && commit.StartsWith(installedCommit, StringComparison.OrdinalIgnoreCase);
    }

    public static string? ChecksumFromManifest(string text, string assetName)
    {
        foreach (var line in text.Split('\n'))
        {
            var chunks = line.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (chunks.Length < 2) continue;
            var hash = ReleaseIntegrity.NormalizeSha256(chunks[0]);
            if (hash != null && chunks[^1].TrimStart('*') == assetName) return hash;
        }
        return null;
    }

    private static string? Get(JsonElement obj, string key)
        => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool GetBool(JsonElement obj, string key)
        => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out var value)
           && value.ValueKind == JsonValueKind.True;
}
