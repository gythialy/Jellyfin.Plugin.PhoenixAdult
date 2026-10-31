#if !__EMBY__
using MediaBrowser.Model.Configuration;
using PhoenixAdult.Web;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// Offline tests for the frame grab entry: shown only when the library's
/// metadata or image provider is this plugin.
/// </summary>
public class FrameGrabAvailabilityTests
{
    [Fact]
    public void MetadataProviderWithPlugin_Enabled()
    {
        var typeOptions = new TypeOptions { MetadataFetchers = new[] { "MetaTube", "PhoenixAdult" } };

        Assert.True(FrameGrabAvailability.IsEnabled(typeOptions, "PhoenixAdult"));
    }

    [Fact]
    public void ImageProviderWithPlugin_Enabled()
    {
        var typeOptions = new TypeOptions { ImageFetchers = new[] { "PhoenixAdult", "Screen Grabber" } };

        Assert.True(FrameGrabAvailability.IsEnabled(typeOptions, "PhoenixAdult"));
    }

    [Fact]
    public void NeitherProviderWithPlugin_Disabled()
    {
        var typeOptions = new TypeOptions
        {
            MetadataFetchers = new[] { "MetaTube" },
            ImageFetchers = new[] { "ThePornDB" },
        };

        Assert.False(FrameGrabAvailability.IsEnabled(typeOptions, "PhoenixAdult"));
    }

    [Fact]
    public void EmptyOptions_Disabled()
    {
        Assert.False(FrameGrabAvailability.IsEnabled(null, "PhoenixAdult"));
        Assert.False(FrameGrabAvailability.IsEnabled(new TypeOptions(), "PhoenixAdult"));
        Assert.False(FrameGrabAvailability.IsEnabled(new TypeOptions { MetadataFetchers = new[] { "PhoenixAdult" } }, string.Empty));
    }
}
#endif
