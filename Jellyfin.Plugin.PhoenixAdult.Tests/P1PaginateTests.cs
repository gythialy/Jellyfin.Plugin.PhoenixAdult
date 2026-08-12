using PhoenixAdult.Helpers;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// P1 分页浏览试点：GirlsOutWest / NewSensations / ExploitedX / ModelCentro / BBCSurprise。
/// 2026-08-12 重写：Search 从 WebSearch(Google 反爬) 改为分页浏览 + 标题过滤。
/// </summary>
[Trait("Category", "Online")]
public class P1PaginateTests
{
    [Fact(DisplayName = "GirlsOutWest: 分页浏览命中 Cartier Rose")]
    public async Task GirlsOutWest_Paginate()
    {
        TestInitializer.Init();
        var provider = Helper.GetBaseSiteByName("SiteGirlsOutWest");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var results = await provider!.Search(new int[] { 154, 0 }, "Cartier Rose", null, cts.Token);
        Console.WriteLine($"results={results.Count}");
        Assert.True(results.Count >= 1, $"期望 ≥1，实际 {results.Count}");
    }

    [Fact(DisplayName = "NewSensations: 分页浏览命中 Summer Stevens")]
    public async Task NewSensations_Paginate()
    {
        TestInitializer.Init();
        var provider = Helper.GetBaseSiteByName("SiteNewSensations");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var results = await provider!.Search(new int[] { 55, 0 }, "Summer Stevens", null, cts.Token);
        Console.WriteLine($"results={results.Count}");
        Assert.True(results.Count >= 1, $"期望 ≥1，实际 {results.Count}");
    }

    [Fact(DisplayName = "ExploitedX: 分页浏览命中最新场景")]
    public async Task ExploitedX_Paginate()
    {
        TestInitializer.Init();
        var provider = Helper.GetBaseSiteByName("NetworkExploitedX");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var results = await provider!.Search(new int[] { 58, 0 }, "Girls Girls Girls", null, cts.Token);
        Console.WriteLine($"results={results.Count}");
        Assert.True(results.Count >= 1, $"期望 ≥1，实际 {results.Count}");
    }

    [Fact(DisplayName = "ModelCentro: 分页兜底命中 Luna Lark")]
    public async Task ModelCentro_Paginate()
    {
        TestInitializer.Init();
        var provider = Helper.GetBaseSiteByName("NetworkModelCentro");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var results = await provider!.Search(new int[] { 100, 18 }, "Luna Lark", null, cts.Token);
        Console.WriteLine($"results={results.Count}");
        Assert.True(results.Count >= 1, $"期望 ≥1，实际 {results.Count}");
    }

    [Fact(DisplayName = "BBCSurprise(站2): 分页浏览命中")]
    public async Task BBCSurprise_Paginate()
    {
        TestInitializer.Init();
        var provider = Helper.GetBaseSiteByName("NetworkExploitedX");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var results = await provider!.Search(new int[] { 58, 2 }, "Brittany", null, cts.Token);
        Console.WriteLine($"results={results.Count}");
        Assert.True(results.Count >= 1, $"期望 ≥1，实际 {results.Count}");
    }
}
