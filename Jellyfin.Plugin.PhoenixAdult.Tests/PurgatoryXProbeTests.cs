using PhoenixAdult.Helpers;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// SitePurgatoryX 探针：pornrips 标题 "PurgatoryX.26.07.31.Audrey.Reid"。
/// 2026-08-12 修复：场景页改版（content-info-wrap/model-wrap 类删除），
/// Search/Update 选择器全部失效 → Update 返回空 Name → Search 结果被过滤为 0。
/// 修复为新结构：h1.title + div.meta/span[1] 日期 + div.description/p + ul.models-list/li。
/// </summary>
[Trait("Category", "Online")]
public class PurgatoryXProbeTests
{
    [Fact(DisplayName = "PurgatoryX: 搜索命中 Audrey Reid 场景")]
    public async Task Search_Hits_Audrey()
    {
        TestInitializer.Init();
        var provider = Helper.GetBaseSiteByName("SitePurgatoryX");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var results = await provider!.Search(new int[] { 61, 0 }, "Audrey Reid", null, cts.Token);

        Assert.True(results.Count >= 1, $"期望 ≥1 结果，实际 {results.Count}");
        Console.WriteLine($"第一结果: {results[0].Name}");
    }
}
