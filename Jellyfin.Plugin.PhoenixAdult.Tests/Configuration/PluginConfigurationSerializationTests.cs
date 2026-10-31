using System.Xml.Serialization;
using PhoenixAdult;
using PhoenixAdult.Configuration;
using PhoenixAdult.Helpers;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// 插件配置序列化回归测试。
/// Jellyfin/Emby 用 System.Xml.Serialization.XmlSerializer 把插件配置存成 XML，
/// 配置类里只要出现 IDictionary 成员，序列化器在反射类型时就会抛
/// "Cannot serialize member ... because it implements IDictionary"，
/// 导致整个配置的读取与保存全部失败（历史 bug：ImageReferers 曾是
/// Dictionary&lt;string, string&gt;）。
/// </summary>
public class PluginConfigurationSerializationTests
{
    [Fact]
    public void XmlSerializer_可以反射配置类型()
    {
        // 构造序列化器即会扫描全部成员，IDictionary 成员会在此抛 NotSupportedException
        var serializer = new XmlSerializer(typeof(PluginConfiguration));

        Assert.NotNull(serializer);
    }

    [Fact]
    public void XmlSerializer_往返保留ImageReferers()
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
    public void 空配置会补上内置Referer规则()
    {
        Assert.Equal(
            new[] { "phncdn.com=https://www.pornhub.com/", "javbus.com=https://www.javbus.com/" },
            ImageRefererDefaults.SeedIfEmpty(null)!.ToArray());
        Assert.Equal(
            new[] { "phncdn.com=https://www.pornhub.com/", "javbus.com=https://www.javbus.com/" },
            ImageRefererDefaults.SeedIfEmpty(new[] { "   " })!.ToArray());

        // 已经有有效规则时不再补，避免把用户删掉的条目又塞回去
        Assert.Null(ImageRefererDefaults.SeedIfEmpty(new[] { "example.com=https://example/" }));
        Assert.Null(ImageRefererDefaults.SeedIfEmpty(new[] { "phncdn.com=" }));
    }

    [Fact]
    public void 内置Referer表由默认规则派生()
    {
        TestInitializer.Init();

        Assert.Equal("https://www.pornhub.com/", Consts.ImageReferers["phncdn.com"]);
        Assert.Equal("https://www.javbus.com/", Consts.ImageReferers["javbus.com"]);
        Assert.Equal(ImageRefererDefaults.Rules.Length, Consts.ImageReferers.Count);
    }

    [Fact]
    public void ResolveImageReferer_命中内置规则()
    {
        TestInitializer.Init();

        Assert.Equal(
            "https://www.pornhub.com/",
            Helper.ResolveImageReferer("https://pix-cdn77.phncdn.com/abc.jpg", Array.Empty<string>()));
    }

    [Fact]
    public void ResolveImageReferer_用户规则覆盖内置规则()
    {
        TestInitializer.Init();

        Assert.Equal(
            "https://override.example/",
            Helper.ResolveImageReferer("https://pix-cdn77.phncdn.com/abc.jpg", new[] { "phncdn.com=https://override.example/" }));
    }

    [Fact]
    public void ResolveImageReferer_空值显式禁用内置规则()
    {
        TestInitializer.Init();

        Assert.Null(Helper.ResolveImageReferer("https://pix-cdn77.phncdn.com/abc.jpg", new[] { "phncdn.com=" }));
    }

    [Fact]
    public void ResolveImageReferer_无匹配或畸形规则返回null()
    {
        TestInitializer.Init();

        Assert.Null(Helper.ResolveImageReferer(
            "https://img.example.com/abc.jpg",
            new[] { "   ", "no-separator", "=https://ignored.example/" }));
        Assert.Null(Helper.ResolveImageReferer(null!, Array.Empty<string>()));
    }
}
