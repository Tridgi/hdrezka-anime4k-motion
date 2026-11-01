using HDREZKA.Core.Api;

namespace HDREZKA.Tests;

public class ReleaseCatalogTests
{
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Json = """
        [
          {"tag_name":"v2.0.0","draft":false,"prerelease":false,"published_at":"2026-11-01T10:00:00Z",
           "html_url":"https://github.com/Tridgi/hdrezka-anime4k-motion/releases/tag/v2.0.0",
           "target_commitish":"main","assets":[
             {"name":"HDREZKA-Anime4K-Motion-v2-win-x64.zip","browser_download_url":"https://github.com/Tridgi/hdrezka-anime4k-motion/releases/download/v2.0.0/HDREZKA-Anime4K-Motion-v2-win-x64.zip",
              "digest":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}]},
          {"tag_name":"test-1234567","draft":false,"prerelease":true,"published_at":"2026-11-01T13:00:00Z",
           "target_commitish":"1234567890123456789012345678901234567890",
           "html_url":"https://github.com/Tridgi/hdrezka-anime4k-motion/releases/tag/test-1234567",
           "assets":[{"name":"HDREZKA-Anime4K-Motion-v2-win-x64.zip",
           "browser_download_url":"https://github.com/Tridgi/hdrezka-anime4k-motion/releases/download/test-1234567/HDREZKA-Anime4K-Motion-v2-win-x64.zip",
           "digest":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}]}
        ]
        """;

    [Fact]
    public void PicksStableAndTestingSeparately()
    {
        var stable = ReleaseCatalog.Latest(Json, UpdateChannel.Stable);
        var testing = ReleaseCatalog.Latest(Json, UpdateChannel.Testing);
        Assert.Equal("v2.0.0", stable?.Tag);
        Assert.Equal("test-1234567", testing?.Tag);
        Assert.Equal(Sha, testing?.Sha256);
        Assert.True(ReleaseCatalog.IsSameBuild(testing!, null, "1234567"));
        Assert.False(ReleaseCatalog.IsSameBuild(stable!, "test-1234567", null));
    }

    [Theory]
    [InlineData("https://evil.example.com/Tridgi/hdrezka-anime4k-motion/releases/download/x/file.zip")]
    [InlineData("http://github.com/Tridgi/hdrezka-anime4k-motion/releases/download/x/file.zip")]
    [InlineData("https://github.com/another/repo/releases/download/x/file.zip")]
    public void RejectsUntrustedAssets(string url) => Assert.False(ReleaseCatalog.IsTrustedReleaseUrl(url));

    [Fact]
    public void ReadsChecksumManifest()
    {
        Assert.Equal(Sha, ReleaseCatalog.ChecksumFromManifest(Sha + "  HDREZKA-Anime4K-Motion-v2-win-x64.zip\n", "HDREZKA-Anime4K-Motion-v2-win-x64.zip"));
        Assert.Null(ReleaseCatalog.ChecksumFromManifest(Sha + "  another.zip", "HDREZKA-Anime4K-Motion-v2-win-x64.zip"));
    }

    [Fact]
    public void DoesNotChooseDraftOrUnrelatedTestingRelease()
    {
        const string json = """
        [{"tag_name":"custom","draft":false,"prerelease":true,"published_at":"2026-11-01T14:00:00Z","html_url":"https://github.com/Tridgi/hdrezka-anime4k-motion/releases/tag/custom","assets":[]}]
        """;
        Assert.Null(ReleaseCatalog.Latest(json, UpdateChannel.Testing));
    }
}
