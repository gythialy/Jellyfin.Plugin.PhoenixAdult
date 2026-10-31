using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// Offline tests for code normalisation: the hyphen must survive and slugs must not be misread.
/// </summary>
public class JavIdTests
{
    [Theory]
    [InlineData("ABP-123", "ABP-123")]
    [InlineData("ABP 123", "ABP-123")]
    [InlineData("abp123", "ABP-123")]
    [InlineData("ABP_123", "ABP-123")]
    [InlineData("ssis277", "SSIS-277")]
    [InlineData("SSIS-277", "SSIS-277")]
    [InlineData("dldss339", "DLDSS-339")]
    [InlineData("300MIUM-1234", "300MIUM-1234")]
    [InlineData("START-114-V", "START-114-V")]
    [InlineData("FC2-PPV-1234567", "FC2-PPV-1234567")]
    [InlineData("fc2-1234567", "FC2-PPV-1234567")]
    [InlineData("071319_870", "071319_870")]
    [InlineData("050422-001", "050422-001")]
    public void Normalize_PreservesDash(string input, string expected)
    {
        Assert.Equal(expected, JavId.Normalize(input));
    }

    [Theory]
    [InlineData("Cum4K Aubry Babcock")]
    [InlineData("EvilAngel 26 10 20 Cherry Kiss")]
    [InlineData("Some Scene Title")]
    public void TryParse_RejectsNonIds(string input)
    {
        Assert.False(JavId.TryParse(input, out _));
    }

    [Fact]
    public void TryExtract_FromFileName()
    {
        Assert.True(Helper.TryExtractJavId("SSIS-277.mp4", out var id1));
        Assert.Equal("SSIS-277", id1);

        Assert.True(Helper.TryExtractJavId("blacked.24.01.14.the.real.thing.1080p", out var id2) == false);
        Assert.Null(id2);
    }
}
