using PhoenixAdult.Helpers;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// SitePlayboyPlus 探针：pornrips 真实标题 "PlayboyPlus.26.08.08.Sunlit.Allure"。
/// 2026-08-12 重写为 Algolia API 模式（网页搜索已全部重定向首页）。
/// </summary>
[Trait("Category", "Online")]
public class PlayboyPlusProbeTests
{
    [Fact(DisplayName = "PlayboyPlus: 搜索命中 Sunlit Allure 2026-08-08")]
    public async Task Search_Hits_Sunlit_Allure()
    {
        TestInitializer.Init();
        var provider = Helper.GetBaseSiteByName("SitePlayboyPlus");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var results = await provider!.Search(new int[] { 185, 0 }, "Sunlit Allure", null, cts.Token);

        Assert.True(results.Count >= 1, $"期望 ≥1 结果，实际 {results.Count}");
        var top = results[0];
        Console.WriteLine($"第一结果: {top.Name} | date={top.PremiereDate:yyyy-MM-dd}");
        Assert.Contains("Sunlit Allure", top.Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "PlayboyPlus: 全链路 Search→Update→GetImages")]
    public async Task Full_Chain()
    {
        TestInitializer.Init();
        var provider = Helper.GetBaseSiteByName("SitePlayboyPlus");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var results = await provider!.Search(new int[] { 185, 0 }, "Sunlit Allure", null, cts.Token);
        Assert.True(results.Count >= 1);

        var sceneID = results[0].ProviderIds.First().Value.Split('#');
        var meta = await provider.Update(new int[] { 185, 0 }, sceneID, cts.Token);
        Assert.False(string.IsNullOrEmpty(meta.Item.Name), "Name 为空");
        Assert.True(meta.Item.PremiereDate.HasValue, "PremiereDate 为空");
        Console.WriteLine($"Update: {meta.Item.Name} | {meta.Item.PremiereDate:yyyy-MM-dd} | 演员 {meta.People.Count} | 流派 {meta.Item.Genres.Count()}");

        var images = (await provider.GetImages(new int[] { 185, 0 }, sceneID, meta.Item, cts.Token)).ToList();
        Console.WriteLine($"GetImages: {images.Count()} 张");
        Assert.True(images.Any(), "无图片");
    }
}
