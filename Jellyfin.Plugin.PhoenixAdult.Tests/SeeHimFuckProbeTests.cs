using PhoenixAdult.Helpers;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

[Trait("Category", "Online")]
public class SeeHimFuckProbeTests
{
    [Fact(DisplayName = "SeeHimFuck: search URL 修正后命中")]
    public async Task SeeHimFuck_Search()
    {
        TestInitializer.Init();
        var provider = Helper.GetBaseSiteByName("NetworkBellaPass");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var results = await provider!.Search(new int[] { 75, 18 }, "Brayden Banks", null, cts.Token);
        Console.WriteLine($"results={results.Count}");
        foreach (var r in results.Take(3)) Console.WriteLine($"  {r.Name}");
        Assert.True(results.Count >= 1, $"期望 ≥1，实际 {results.Count}");
    }
}
