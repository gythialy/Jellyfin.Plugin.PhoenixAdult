using PhoenixAdult.Sites;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// Offline tests for the search term shapes: titles mix compact and hyphenated forms
/// and WordPress does not normalise, so both must be tried.
/// </summary>
public class SiteMadouQuSearchVariantTests
{
    [Theory]
    [InlineData("MD-0218", new[] { "MD0218", "MD-0218" })]
    [InlineData("md-0218", new[] { "md0218", "md-0218" })]
    [InlineData("MDHT-0021", new[] { "MDHT0021", "MDHT-0021" })]
    [InlineData("91CM-230", new[] { "91CM230", "91CM-230" })]
    [InlineData("MDSR-0005", new[] { "MDSR0005", "MDSR-0005" })]
    [InlineData("Md 0218", new[] { "Md0218", "Md-0218", "Md 0218" })]
    [InlineData("MD_0334", new[] { "MD0334", "MD-0334", "MD_0334" })]
    public void Code_CompactThenHyphenated_RawLast(string searchTitle, string[] expected)
    {
        Assert.Equal(expected, SiteMadouQu.GetSearchVariants(searchTitle));
    }

    [Theory]
    [InlineData("中文标题示例")]
    [InlineData("Remi Raw")]
    [InlineData("MD0165-1")]
    [InlineData("MDSR-0005-1")]
    [InlineData("Interracial 24")]
    public void NonCode_KeepsSingleCandidate(string searchTitle)
    {
        Assert.Equal(new[] { searchTitle }, SiteMadouQu.GetSearchVariants(searchTitle));
    }

    [Fact]
    public void EmptyInput_NoCandidates()
    {
        Assert.Empty(SiteMadouQu.GetSearchVariants(string.Empty));
        Assert.Empty(SiteMadouQu.GetSearchVariants("   "));
    }
}
