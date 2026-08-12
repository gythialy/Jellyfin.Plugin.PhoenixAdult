using PhoenixAdult.Helpers;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// NetworkDogfart 探针：pornrips 标题 "BlacksOnBlondes.26.08.07.Harley.Love"。
/// 2026-08-12 重写：搜索 URL /tour/search.php(404) → Algolia API（dogfartnetwork 的 window.env key）。
/// </summary>
[Trait("Category", "Online")]
public class DogfartProbeTests
{
    [Fact(DisplayName = "Dogfart: 搜索命中 Harley Love")]
    public async Task Search_Hits_Harley()
    {
        TestInitializer.Init();
        var provider = Helper.GetBaseSiteByName("NetworkDogfart");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var results = await provider!.Search(new int[] { 19, 1 }, "Harley Love", null, cts.Token);

        Assert.True(results.Count >= 1, $"期望 ≥1 结果，实际 {results.Count}");
        var top = results[0];
        Console.WriteLine($"第一结果: {top.Name} | date={top.PremiereDate:yyyy-MM-dd}");
    }

    [Fact(DisplayName = "Dogfart: 全链路 Search→Update→GetImages")]
    public async Task Full_Chain()
    {
        TestInitializer.Init();
        var provider = Helper.GetBaseSiteByName("NetworkDogfart");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var results = await provider!.Search(new int[] { 19, 1 }, "Harley Love", null, cts.Token);
        Assert.True(results.Count >= 1);

        var sceneID = results[0].ProviderIds.First().Value.Split('#');
        var meta = await provider.Update(new int[] { 19, 1 }, sceneID, cts.Token);
        Assert.False(string.IsNullOrEmpty(meta.Item.Name), "Name 为空");
        Console.WriteLine($"Update: {meta.Item.Name} | {meta.Item.PremiereDate:yyyy-MM-dd} | 演员 {meta.People.Count} | 流派 {meta.Item.Genres.Count()}");

        var images = (await provider.GetImages(new int[] { 19, 1 }, sceneID, meta.Item, cts.Token)).ToList();
        Console.WriteLine($"GetImages: {images.Count()} 张");
        Assert.True(images.Any(), "无图片");
    }
}
