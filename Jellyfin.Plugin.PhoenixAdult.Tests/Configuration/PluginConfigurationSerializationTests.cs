using System.Xml.Serialization;
using PhoenixAdult;
using PhoenixAdult.Configuration;
using PhoenixAdult.Helpers;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// Serialization tests: XmlSerializer rejects IDictionary members, which used to break the whole configuration.
/// </summary>
public class PluginConfigurationSerializationTests
{
    [Fact]
    public void XmlSerializer_CanReflectConfigurationType()
    {
        // Building the serializer scans every member; an IDictionary member throws here.
        var serializer = new XmlSerializer(typeof(PluginConfiguration));

        Assert.NotNull(serializer);
    }

    [Fact]
    public void XmlSerializer_RoundTripsImageReferers()
    {
        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        var config = new PluginConfiguration();
        config.ImageReferers.Add("phncdn.com=https://www.pornhub.com/");
        config.ImageReferers.Add("example.com=");

        using var writer = new StringWriter();
        serializer.Serialize(writer, config);

        using var reader = new StringReader(writer.ToString());
        var roundTripped = (PluginConfiguration)serializer.Deserialize(reader)!;

        Assert.Equal(config.ImageReferers, roundTripped.ImageReferers);
    }

    [Fact]
    public void EmptyConfiguration_GetsBuiltInRefererRules()
    {
        Assert.Equal(
            new[] { "phncdn.com=https://www.pornhub.com/", "javbus.com=https://www.javbus.com/" },
            ImageRefererDefaults.SeedIfEmpty(null)!.ToArray());
        Assert.Equal(
            new[] { "phncdn.com=https://www.pornhub.com/", "javbus.com=https://www.javbus.com/" },
            ImageRefererDefaults.SeedIfEmpty(new[] { "   " })!.ToArray());

        // Existing rules are kept; deleted entries must not come back.
        Assert.Null(ImageRefererDefaults.SeedIfEmpty(new[] { "example.com=https://example/" }));
        Assert.Null(ImageRefererDefaults.SeedIfEmpty(new[] { "phncdn.com=" }));
    }

    [Fact]
    public void BuiltInReferers_DerivedFromDefaults()
    {
        TestInitializer.Init();

        Assert.Equal("https://www.pornhub.com/", Consts.ImageReferers["phncdn.com"]);
        Assert.Equal("https://www.javbus.com/", Consts.ImageReferers["javbus.com"]);
        Assert.Equal(ImageRefererDefaults.Rules.Length, Consts.ImageReferers.Count);
    }

    [Fact]
    public void ResolveImageReferer_MatchesBuiltInRule()
    {
        TestInitializer.Init();

        Assert.Equal(
            "https://www.pornhub.com/",
            Helper.ResolveImageReferer("https://pix-cdn77.phncdn.com/abc.jpg", Array.Empty<string>()));
    }

    [Fact]
    public void ResolveImageReferer_UserRuleOverridesBuiltIn()
    {
        TestInitializer.Init();

        Assert.Equal(
            "https://override.example/",
            Helper.ResolveImageReferer("https://pix-cdn77.phncdn.com/abc.jpg", new[] { "phncdn.com=https://override.example/" }));
    }

    [Fact]
    public void ResolveImageReferer_EmptyValueDisablesBuiltIn()
    {
        TestInitializer.Init();

        Assert.Null(Helper.ResolveImageReferer("https://pix-cdn77.phncdn.com/abc.jpg", new[] { "phncdn.com=" }));
    }

    [Fact]
    public void ResolveImageReferer_ReturnsNullForNoMatchOrMalformed()
    {
        TestInitializer.Init();

        Assert.Null(Helper.ResolveImageReferer(
            "https://img.example.com/abc.jpg",
            new[] { "   ", "no-separator", "=https://ignored.example/" }));
        Assert.Null(Helper.ResolveImageReferer(null!, Array.Empty<string>()));
    }
}
