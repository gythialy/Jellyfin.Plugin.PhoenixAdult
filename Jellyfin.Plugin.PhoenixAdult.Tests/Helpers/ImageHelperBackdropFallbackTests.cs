using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PhoenixAdult;
using PhoenixAdult.Helpers.Utils;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// Backdrop fallback: a shared URL is deduplicated by the client, so the banner URL carries a #backdrop fragment.
/// </summary>
public class ImageHelperBackdropFallbackTests
{
    private const string CoverUrl = "https://www.javbus.com/imgs/cover/w77_b.jpg";

    [Fact]
    public async Task PosterBecomesBackdropWithoutOne()
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

        // Below the 1280 minimum the size must stay unset, otherwise Jellyfin skips it.
        Assert.Null(backdrop.Width);
        Assert.Null(backdrop.Height);
    }

    [Fact]
    public async Task ExistingBackdropIsNotDuplicated()
    {
        var images = await Validate(
            new RemoteImageInfo { ProviderName = "PhoenixAdult", Url = CoverUrl, Type = ImageType.Primary, Width = 1920, Height = 1080 },
            new RemoteImageInfo { ProviderName = "PhoenixAdult", Url = "https://www.javbus.com/pics/sample/1.jpg", Type = ImageType.Backdrop, Width = 1280, Height = 720 });

        var backdrops = images.Where(o => o.Type == ImageType.Backdrop).ToList();
        Assert.Single(backdrops);
        Assert.Equal("https://www.javbus.com/pics/sample/1.jpg", backdrops[0].Url);
    }

    [Fact]
    public async Task BackdropOnly_NoFallback()
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
    /// Disable image validation so the test stays offline.
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
