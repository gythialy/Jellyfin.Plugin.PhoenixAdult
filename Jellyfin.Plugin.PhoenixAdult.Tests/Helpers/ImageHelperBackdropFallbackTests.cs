using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PhoenixAdult;
using PhoenixAdult.Helpers.Utils;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// 海报兜底成横幅的回归测试。
/// 背景：JavBus 无码页只有一张封面，没有截图集；插件同时用同一 URL 报 Primary 与 Backdrop 时，
/// Jellyfin 会按 URL 去重，横幅就地消失。兜底实现给横幅 URL 加 #backdrop 片段（片段不会发送到服务器）。
/// </summary>
public class ImageHelperBackdropFallbackTests
{
    private const string CoverUrl = "https://www.javbus.com/imgs/cover/w77_b.jpg";

    [Fact]
    public async Task 没有横幅时把海报当横幅()
    {
        var images = await Validate(new RemoteImageInfo
        {
            ProviderName = "PhoenixAdult",
            Url = CoverUrl,
            Type = ImageType.Primary,
            Width = 800,
            Height = 450,
        });

        var backdrop = Assert.Single(images, o => o.Type == ImageType.Backdrop);
        Assert.Equal(CoverUrl + "#backdrop", backdrop.Url);

        // 800 宽 < 1280（Jellyfin 自动下载横幅的 MinWidth），不能填尺寸，否则会被静默过滤
        Assert.Null(backdrop.Width);
        Assert.Null(backdrop.Height);
    }

    [Fact]
    public async Task 已有横幅时不重复添加()
    {
        var images = await Validate(
            new RemoteImageInfo { ProviderName = "PhoenixAdult", Url = CoverUrl, Type = ImageType.Primary, Width = 1920, Height = 1080 },
            new RemoteImageInfo { ProviderName = "PhoenixAdult", Url = "https://www.javbus.com/pics/sample/1.jpg", Type = ImageType.Backdrop, Width = 1280, Height = 720 });

        var backdrops = images.Where(o => o.Type == ImageType.Backdrop).ToList();
        Assert.Single(backdrops);
        Assert.Equal("https://www.javbus.com/pics/sample/1.jpg", backdrops[0].Url);
    }

    [Fact]
    public async Task 只有横幅没有海报时不做任何兜底()
    {
        var images = await Validate(new RemoteImageInfo
        {
            ProviderName = "PhoenixAdult",
            Url = "https://www.javbus.com/pics/sample/1.jpg",
            Type = ImageType.Backdrop,
            Width = 1280,
            Height = 720,
        });

        Assert.DoesNotContain(images, o => o.Type == ImageType.Primary);
        Assert.Single(images, o => o.Type == ImageType.Backdrop);
    }

    /// <summary>
    /// 关掉图片尺寸校验再调用，测试不触网。
    /// </summary>
    private static async Task<List<RemoteImageInfo>> Validate(params RemoteImageInfo[] images)
    {
        TestInitializer.Init();
        var config = global::PhoenixAdult.Plugin.Instance.Configuration;
        var previous = config.DisableImageValidation;
        config.DisableImageValidation = true;
        try
        {
            return await ImageHelper.GetImagesSizeAndValidate(images, CancellationToken.None);
        }
        finally
        {
            config.DisableImageValidation = previous;
        }
    }
}
