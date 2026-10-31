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
    public void AddSourcePrefix_MarksSiteFromProviderId()
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
    public void ParseSearchTitle_DropsCodeDateAndButtonText()
    {
        const string Html = "<a class=\"movie-box\"><div class=\"photo-info\"><span>Sample Title<br />"
            + "<div style=\"white-space:normal;\"><button class=\"btn\" disabled=\"disabled\">HD</button></div> "
            + "<date>HEYZO-0929</date> / <date>2015-08-16</date></span></div></a>";

        Assert.Equal("Sample Title", NetworkJavBus.ParseSearchTitleFromHtml(Html));
    }

    [Fact]
    public void ParseSearchTitle_ReturnsEmptyWithoutSpan()
    {
        Assert.Equal(string.Empty, NetworkJavBus.ParseSearchTitleFromHtml("<a class=\"movie-box\"></a>"));
    }
}
