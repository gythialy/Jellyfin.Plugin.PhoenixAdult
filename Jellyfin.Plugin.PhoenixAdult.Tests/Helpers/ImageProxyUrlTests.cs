using PhoenixAdult.Helpers.Utils;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// Proxy URL building: CDNs that require a Referer answer 403 to the downloader and to a browser.
/// </summary>
public class ImageProxyUrlTests
{
    private const string JavbusCover = "https://www.javbus.com/imgs/cover/w77_b.jpg";

    [Fact]
    public void Encode_RoundTripsThroughDecode()
    {
        var url = "https://www.javbus.com/pics/cover/abc_b.jpg?x=1&y=中文";

        var encoded = ImageProxyUrl.Encode(url);

        Assert.DoesNotContain('=', encoded);
        Assert.DoesNotContain('+', encoded);
        Assert.DoesNotContain('/', encoded);
        Assert.Equal(url, ImageProxyUrl.Decode(encoded));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    [InlineData("!!!not-base64!!!")]
    public void Decode_ReturnsNullForInvalidInput(string? value)
    {
        Assert.Null(ImageProxyUrl.Decode(value!));
    }

    [Fact]
    public void NeedsProxy_OnlyForHostsWithRule()
    {
        TestInitializer.Init();

        Assert.True(ImageProxyUrl.NeedsProxy(JavbusCover));
        Assert.True(ImageProxyUrl.NeedsProxy("https://pix-cdn77.phncdn.com/abc.jpg"));
        Assert.False(ImageProxyUrl.NeedsProxy("https://c0.jdbstatic.com/covers/ab/abc.jpg"));
        Assert.False(ImageProxyUrl.NeedsProxy(null!));
    }

    [Fact]
    public void ForDownload_WithoutProxy_ReturnsOriginal()
    {
        TestInitializer.Init();
        var previous = ImageProxyUrl.BaseUrl;
        ImageProxyUrl.BaseUrl = null;
        try
        {
            Assert.Equal(JavbusCover, ImageProxyUrl.ForDownload(JavbusCover));
            Assert.Equal(JavbusCover, ImageProxyUrl.ForBrowser(JavbusCover));
            Assert.False(ImageProxyUrl.IsAvailable);
        }
        finally
        {
            ImageProxyUrl.BaseUrl = previous;
        }
    }

    [Fact]
    public void ForDownload_Absolute_ForBrowser_Relative()
    {
        TestInitializer.Init();
        var previous = ImageProxyUrl.BaseUrl;
        ImageProxyUrl.BaseUrl = "http://127.0.0.1:8096/jellyfin";
        try
        {
            var download = ImageProxyUrl.ForDownload(JavbusCover);
            var browser = ImageProxyUrl.ForBrowser(JavbusCover);

            Assert.StartsWith("http://127.0.0.1:8096/jellyfin/PhoenixAdult/image?u=", download);
            Assert.StartsWith("/jellyfin/PhoenixAdult/image?u=", browser);
            Assert.Equal(
                JavbusCover,
                ImageProxyUrl.Decode(download.Substring(download.IndexOf("?u=", System.StringComparison.Ordinal) + 3)));

            // URLs without a rule keep the original address.
            Assert.Equal("https://c0.jdbstatic.com/covers/ab/abc.jpg", ImageProxyUrl.ForDownload("https://c0.jdbstatic.com/covers/ab/abc.jpg"));
        }
        finally
        {
            ImageProxyUrl.BaseUrl = previous;
        }
    }
}
