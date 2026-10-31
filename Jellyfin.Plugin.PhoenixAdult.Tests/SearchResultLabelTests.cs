using HtmlAgilityPack;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Providers;
using PhoenixAdult.Sites;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// Dialog display names: prefix the source site and keep card noise out of the title.
/// </summary>
public class SearchResultLabelTests
{
    [Fact]
    public void AddSourcePrefix_按ProviderId标记站点名()
    {
        TestInitializer.Init();

        var results = new List<RemoteSearchResult>
        {
            new() { Name = "foo", ProviderIds = { { "PhoenixAdult", "96#0#abc" } } },
            new() { Name = "bar", ProviderIds = { { "PhoenixAdult", "255#0#def" } } },
            new() { Name = "baz" },
        };

        MovieProvider.AddSourcePrefix(results);

        Assert.Equal("[JavBus] foo", results[0].Name);
        Assert.Equal("[JavDB] bar", results[1].Name);
        Assert.Equal("baz", results[2].Name);

        // Idempotent: repeated calls do not stack prefixes.
        MovieProvider.AddSourcePrefix(results);
        Assert.Equal("[JavBus] foo", results[0].Name);
        Assert.Equal("[JavDB] bar", results[1].Name);
    }

    [Fact]
    public void ParseSearchTitle_去掉番号日期和按钮文字()
    {
        const string Html = "<a class=\"movie-box\"><div class=\"photo-info\"><span>三上里穂 もしもし<br />"
            + "<div style=\"white-space:normal;\"><button class=\"btn\" disabled=\"disabled\">高清</button></div> "
            + "<date>HEYZO-0929</date> / <date>2015-08-16</date></span></div></a>";

        var document = new HtmlDocument();
        document.LoadHtml(Html);

        var title = NetworkJavBus.ParseSearchTitle(document.DocumentNode.SelectSingleNode("//a"));

        Assert.Equal("三上里穂 もしもし", title);
    }

    [Fact]
    public void ParseSearchTitle_没有span时返回空串()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<a class=\"movie-box\"></a>");

        Assert.Equal(string.Empty, NetworkJavBus.ParseSearchTitle(document.DocumentNode.SelectSingleNode("//a")));
    }
}
