using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// 演员名规范化（Actor substitution）离线测试：
/// 数据来自 data/Actors.json，验证 CJK→罗马字、日式倒序与未知名字原样返回。
/// </summary>
public class ActorResolveTests
{
    public ActorResolveTests()
    {
        TestInitializer.Init();
    }

    [Theory]
    [InlineData("百多えみり", "Emiri Momota")]
    [InlineData("加藤えま", "Ema Kato")]
    [InlineData("Kato Ema", "Ema Kato")]
    [InlineData("Emiri Mizusawa", "Sumire Mizukawa")]
    [InlineData("水川スミレ", "Sumire Mizukawa")]
    [InlineData("Abby Rains", "Abbey Rain")]
    [InlineData("Emiri Momota", "Emiri Momota")]
    public void Resolve_MapsAliasesToCanonical(string input, string expected)
    {
        Assert.Equal(expected, Actors.Resolve(input));
    }

    [Theory]
    [InlineData("Kato Ema", "Ema Kato")]
    [InlineData("加藤えま", "Ema Kato")]
    [InlineData("Unknown Person", "Unknown Person")]
    [InlineData("Single", "Single")]
    public void ResolveWithJapaneseStyle_HandlesWordOrder(string input, string expected)
    {
        Assert.Equal(expected, Actors.ResolveWithJapaneseStyle(input));
    }

    [Fact]
    public void Resolve_EmptyInput_ReturnsInput()
    {
        Assert.Equal(string.Empty, Actors.Resolve(string.Empty));
    }
}
