using PhoenixAdult.Helpers;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// NetworkTeenMegaWorld 探针：pornrips 标题 "Beauty-Angels.26.08.09.Luna.Evans"。
/// 2026-08-12 修复：搜索节点从 div 变 a，标题从 a 变 span。
/// </summary>
[Trait("Category", "Online")]
public class TeenMegaWorldProbeTests
{
    [Fact(DisplayName = "TeenMegaWorld: 搜索命中 Luna Evans")]
    public async Task Search_Hits_Luna()
    {
        TestInitializer.Init();
        var provider = Helper.GetBaseSiteByName("NetworkTeenMegaWorld");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var results = await provider!.Search(new int[] { 116, 7 }, "Luna Evans", null, cts.Token);

        Assert.True(results.Count >= 1, $"期望 ≥1 结果，实际 {results.Count}");
        Console.WriteLine($"第一结果: {results[0].Name}");
    }

    [Fact(DisplayName = "TeenMegaWorld: Update 场景页直抓")]
    public async Task Update_Scene()
    {
        TestInitializer.Init();
        var provider = Helper.GetBaseSiteByName("NetworkTeenMegaWorld");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var sceneUrl = "https://teenmegaworld.net/trailers/Orgasming-after-a-trip.html";
        var meta = await provider.Update(new int[] { 116, 7 }, new[] { Helper.Encode(sceneUrl) }, cts.Token);
        Assert.False(string.IsNullOrEmpty(meta.Item.Name), "Name 为空");
        Console.WriteLine($"Update: {meta.Item.Name} | date={meta.Item.PremiereDate:yyyy-MM-dd} | 演员 {meta.People.Count}");
    }
}
