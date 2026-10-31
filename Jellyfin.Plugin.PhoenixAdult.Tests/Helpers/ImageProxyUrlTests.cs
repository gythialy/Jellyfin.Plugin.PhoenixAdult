using PhoenixAdult.Helpers.Utils;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// 图片代理地址的构造规则回归测试。
/// 背景：JavBus 封面 / phncdn 签名图要求 Referer，而 Jellyfin 下载远程图片不带 Referer、
/// 浏览器直连也只带本站 Referer，直接给原始地址必然 403 —— 表现为「元数据有了，海报背景图都没有」。
/// </summary>
public class ImageProxyUrlTests
{
    private const string JavbusCover = "https://www.javbus.com/imgs/cover/w77_b.jpg";

    [Fact]
    public void Encode_与Decode可以往返()
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
    public void Decode_非法输入返回null(string? value)
    {
        Assert.Null(ImageProxyUrl.Decode(value!));
    }

    [Fact]
    public void NeedsProxy_只对命中Referer规则的主机为真()
    {
        TestInitializer.Init();

        Assert.True(ImageProxyUrl.NeedsProxy(JavbusCover));
        Assert.True(ImageProxyUrl.NeedsProxy("https://pix-cdn77.phncdn.com/abc.jpg"));
        Assert.False(ImageProxyUrl.NeedsProxy("https://c0.jdbstatic.com/covers/ab/abc.jpg"));
        Assert.False(ImageProxyUrl.NeedsProxy(null!));
    }

    [Fact]
    public void ForDownload_没有代理端点时原样返回()
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
    public void ForDownload_返回绝对代理地址_ForBrowser返回同源相对地址()
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

            // 不需要 Referer 的图保持原地址（不要无谓地绕一圈代理）
            Assert.Equal("https://c0.jdbstatic.com/covers/ab/abc.jpg", ImageProxyUrl.ForDownload("https://c0.jdbstatic.com/covers/ab/abc.jpg"));
        }
        finally
        {
            ImageProxyUrl.BaseUrl = previous;
        }
    }
}
