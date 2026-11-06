using System.Net;
using System.Text;
using HDREZKA.Core.Api;
using Xunit;

namespace HDREZKA.Tests;

/// <summary>
/// Сетевые сценарии на фейковом HttpMessageHandler: ретраи, 403-flip,
/// редиректы и запрет понижения https→http для форм с паролем.
/// </summary>
public class RezkaClientTests
{
    private const string DetailsHtml = """
        <html><body><div id="wrapper">
          <div class="b-content__main"><div class="b-post__title">X</div></div>
        </div></body></html>
        """;

    [Theory]
    [InlineData("series/melodrama", "/series/melodrama/?filter=last")]
    [InlineData("films/drama", "/films/drama/?filter=last")]
    [InlineData("series/genre/melodrama", "/series/genre/melodrama/?filter=last")]
    [InlineData("genre/fantastic", "/genre/fantastic/?filter=last")]
    public async Task GetGenreList_UsesExactValidSiteRoute(
        string genrePath, string expectedRequest)
    {
        var paths = new List<string>();
        using var client = CreateClient((request, _) =>
        {
            paths.Add(request.RequestUri!.PathAndQuery);
            return Html("""
                <html><body><div id="wrapper">
                  <div class="b-content__inline_item"
                       data-url="/series/1234-love.html">
                    <div class="b-content__inline_item-cover">
                      <a><img src="https://cdn.example/1.jpg"></a>
                    </div>
                    <div class="b-content__inline_item-link"><a>Любовь</a></div>
                  </div>
                </div></body></html>
                """);
        });

        var (_, items) = await client.GetGenreListAsync(genrePath, filter: ListFilter.Latest);

        Assert.Equal(expectedRequest, Assert.Single(paths));
        Assert.Equal("Любовь", Assert.Single(items).Name);
    }

    [Fact]
    public async Task GetGenreOptions_UsesCorrectMediaNavbarFromActiveMirror()
    {
        var paths = new List<string>();
        using var client = CreateClient((request, _) =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            return Html("""
                <html><body><div id="wrapper"><ul>
                  <li class="b-topnav__item i1"><ul class="left">
                    <li><a href="/films/melodrama/">Мелодрамы</a></li>
                  </ul></li>
                  <li class="b-topnav__item i2"><ul class="left">
                    <li><a href="/series/melodrama/">Мелодрамы</a></li>
                  </ul></li>
                </ul></div></body></html>
                """);
        });

        var series = await client.GetGenreOptionsAsync(Section.Series);

        Assert.Equal("/series/", Assert.Single(paths));
        Assert.Equal("series/melodrama", Assert.Single(series).Id);
    }

    [Theory]
    [InlineData("https://evil.example/series/melodrama")]
    [InlineData("series/../../secret")]
    [InlineData("series/melodrama?other=1")]
    [InlineData("series/popular/page/2")]
    public void GetGenreList_RejectsNonGenreOrUnsafePaths(string path)
    {
        using var client = CreateClient((_, _) => Html(DetailsHtml));
        Assert.Throws<ArgumentException>(() => client.GetGenreListAsync(path));
    }

    [Fact]
    public async Task Get_403_FlipsHeaderVariant_AndRetriesOnce()
    {
        var androidSeen = new List<bool>();
        var flipped = 0;

        using var client = new RezkaClient(new RezkaClientOptions
        {
            Mirror = "https://mirror.example/",
            UseAndroidHeaders = true,
            MessageHandler = new FakeHandler((request, _) =>
            {
                var hasAndroid = request.Headers.Contains("X-Hdrezka-Android-App");
                androidSeen.Add(hasAndroid);
                return hasAndroid ? Status(HttpStatusCode.Forbidden) : Html(DetailsHtml);
            }),
        });
        client.HeadersFlipped += () => flipped++;

        var details = await client.GetDetailsAsync("films/1-x.html");

        Assert.Equal(2, androidSeen.Count);
        Assert.True(androidSeen[0]);
        Assert.False(androidSeen[1]);
        Assert.Equal(1, flipped);
        Assert.False(client.Options.UseAndroidHeaders);
        Assert.Equal("X", details.Name);
    }

    [Fact]
    public async Task Get_500_IsRetried()
    {
        var count = 0;

        using var client = CreateClient((_, _) =>
        {
            count++;
            return count == 1 ? Status(HttpStatusCode.InternalServerError) : Html(DetailsHtml);
        });

        var details = await client.GetDetailsAsync("films/1-x.html");

        Assert.Equal(2, count);
        Assert.Equal("X", details.Name);
    }

    [Fact]
    public async Task Get_404_IsNotRetried_AndMapsToNotFound()
    {
        var count = 0;

        using var client = CreateClient((_, _) =>
        {
            count++;
            return Status(HttpStatusCode.NotFound);
        });

        var ex = await Assert.ThrowsAsync<RezkaException>(() => client.GetDetailsAsync("films/404-x.html"));

        Assert.Equal(RezkaError.NotFound, ex.Kind);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Get_FollowsHttpsToHttpDowngrade_ForPublicPages()
    {
        var count = 0;

        using var client = CreateClient((_, index) =>
        {
            count++;
            return index == 1
                ? Redirect("http://mirror.example/films/1-x.html")
                : Html(DetailsHtml);
        });

        var details = await client.GetDetailsAsync("films/1-x.html");

        Assert.Equal(2, count);
        Assert.Equal("X", details.Name);
    }

    [Fact]
    public async Task SignIn_RefusesHttpMirror_WithoutAnyRequest()
    {
        var count = 0;
        using var client = new RezkaClient(new RezkaClientOptions
        {
            Mirror = "http://mirror.example/",
            MessageHandler = new FakeHandler((_, _) =>
            {
                count++;
                return Json("""{"success":true}""");
            }),
        });

        var ex = await Assert.ThrowsAsync<RezkaException>(() => client.SignInAsync("user", "pass"));

        Assert.Equal(RezkaError.InsecureMirror, ex.Kind);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task SignIn_RefusesHttpsToHttpRedirect()
    {
        var count = 0;
        using var client = CreateClient((_, _) =>
        {
            count++;
            return Redirect("http://mirror.example/ajax/login/");
        });

        var ex = await Assert.ThrowsAsync<RezkaException>(() => client.SignInAsync("user", "pass"));

        Assert.Equal(RezkaError.InsecureMirror, ex.Kind);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SignIn_WorksOverHttps()
    {
        using var client = CreateClient((_, _) => Json("""{"success":true}"""));

        await client.SignInAsync("user", "pass");
    }

    [Theory]
    [InlineData("""{"success":true,"comment":"ok"}""")]
    [InlineData("""{"success":1,"comment":"ok"}""")]
    [InlineData("""{"success":"1","comment":"ok"}""")]
    public async Task CommentPost_AcceptsExplicitSiteAcknowledgement(string response)
    {
        using var client = CreateClient((_, _) => Json(response));
        Assert.Equal("ok", await client.SendCommentAsync("123", "Test comment"));
    }

    [Theory]
    [InlineData("""{"success":false,"message":"Captcha required"}""", "Captcha required")]
    [InlineData("""{"success":0,"message":"Session expired"}""", "Session expired")]
    [InlineData("""{"success":"0","error":"Try again"}""", "Try again")]
    [InlineData("""{"message":"No success flag"}""", "No success flag")]
    [InlineData("<html>login</html>", "не JSON")]
    public async Task CommentPost_PreservesServerRejectionReason(string response, string expected)
    {
        using var client = CreateClient((_, _) => Json(response));
        var ex = await Assert.ThrowsAsync<RezkaException>(
            () => client.SendCommentAsync("123", "Unsent draft"));
        Assert.Equal(RezkaError.Site, ex.Kind);
        Assert.Contains(expected, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static RezkaClient CreateClient(Func<HttpRequestMessage, int, HttpResponseMessage> responder) =>
        new(new RezkaClientOptions
        {
            Mirror = "https://mirror.example/",
            MessageHandler = new FakeHandler(responder),
        });

    private static HttpResponseMessage Html(string html) =>
        new(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") };

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Status(HttpStatusCode code) => new(code);

    private static HttpResponseMessage Redirect(string location) =>
        new(HttpStatusCode.Found) { Headers = { Location = new Uri(location) } };

    private sealed class FakeHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        private int _count;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _count);
            return Task.FromResult(responder(request, index));
        }
    }
}
