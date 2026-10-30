using System.Linq;
using PhoenixAdult.Helpers.Utils;
using Xunit;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// 多源搜索路由离线测试：番号类型 → 并发检索站点集合。
/// </summary>
public class SearchRouterTests
{
    [Theory]
    [InlineData("MDSR-0005")]
    [InlineData("MDX-0267")]
    [InlineData("MDCM-0013")]
    [InlineData("MTVQ18-EP1-AV")]
    public void MadouIds_RouteToMadouSources(string id)
    {
        var sources = SearchRouter.GetSources(id);
        Assert.Contains(sources, s => s.Group == 234); // MadouQu
        Assert.Contains(sources, s => s.Group == 235); // ModelMediaAsia
    }

    [Theory]
    [InlineData("ABP-123")]
    [InlineData("SSIS-354")]
    [InlineData("SSIS277")]
    public void JavIds_RouteToJavSources(string id)
    {
        var sources = SearchRouter.GetSources(id);
        Assert.Contains(sources, s => s.Group == 96);  // JavBus
        Assert.Contains(sources, s => s.Group == 239); // AVBase
        Assert.DoesNotContain(sources, s => s.Group == 234);
    }

    [Theory]
    [InlineData("FC2-PPV-1234567")]
    [InlineData("FC2-PPV-4680562")]
    public void Fc2Ids_RouteToFc2Sources(string id)
    {
        var sources = SearchRouter.GetSources(id);
        Assert.Contains(sources, s => s.Group == 238); // FC2 官方
        Assert.Contains(sources, s => s.Group == 249); // FC2PPVDB
    }

    [Fact]
    public void Overrides_ReplaceFamilySources()
    {
        var sources = SearchRouter.GetSources("ABP-123", "jav=96:0|17:0");
        Assert.Equal(new[] { (96, 0), (17, 0) }, sources);
    }

    [Fact]
    public void Overrides_OnlyAffectTheirFamily()
    {
        var sources = SearchRouter.GetSources("MDSR-0005", "jav=96:0");
        Assert.Contains(sources, s => s.Group == 234); // madou 仍用默认清单
    }

    [Fact]
    public void InvalidOverrides_FallBackToDefaults()
    {
        var sources = SearchRouter.GetSources("ABP-123", "jav=abc:0;jav=not-a-number");
        Assert.Contains(sources, s => s.Group == 239);
    }

    [Fact]
    public void ParseOverrides_SkipsMalformedItems()
    {
        var parsed = SearchRouter.ParseOverrides("madou=234:0|235:0;fc2=238:0;bad;empty=");
        Assert.Equal(2, parsed["madou"].Count);
        Assert.Single(parsed["fc2"]);
        Assert.False(parsed.ContainsKey("empty"));
    }

    [Fact]
    public void Family_IsReported()
    {
        Assert.Equal("madou", SearchRouter.GetFamily("MDSR-0005"));
        Assert.Equal("jav", SearchRouter.GetFamily("ABP-123"));
        Assert.Equal("fc2", SearchRouter.GetFamily("FC2-PPV-1234567"));
    }
}
