using PhoenixAdult.Helpers;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// SiteJacquieEtMichel 探针：TV 站重写验证（pornrips 场景 "JacquieEtMichel.26.08.11.Jolly..."）。
/// 2026-08-12 重写：域名 jacquieetmichel.net → jacquieetmicheltv.net，搜索加 label=scene，
/// 选择器 content-card__wrapper + content-detail__title。
/// </summary>
[Trait("Category", "Online")]
public class JacquieEtMichelProbeTests
{
    [Fact(DisplayName = "JacquieEtMichel: 搜索命中 Jolly 场景")]
    public async Task Search_Hits_Scene()
    {
        TestInitializer.Init();
        var provider = Helper.GetBaseSiteByName("SiteJacquieEtMichel");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var results = await provider!.Search(new int[] { 162, 0 }, "Jolly", null, cts.Token);

        Assert.True(results.Count >= 1, $"期望 ≥1 结果，实际 {results.Count}");
        Console.WriteLine($"第一结果: {results[0].Name}");
    }

    [Fact(DisplayName = "JacquieEtMichel: Update 场景页直抓")]
    public async Task Update_Scene_URL()
    {
        TestInitializer.Init();
        var provider = Helper.GetBaseSiteByName("SiteJacquieEtMichel");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var sceneUrl = "/en/content/6a612906060b77ef5809323e/jolly-finds-a-savior-and-thanks-him-with-her-ass";
        var meta = await provider.Update(new int[] { 162, 0 }, new[] { Helper.Encode(sceneUrl) }, cts.Token);
        Console.WriteLine($"Update: Name='{meta.Item.Name}' date={meta.Item.PremiereDate:yyyy-MM-dd} 演员 {meta.People.Count} 流派 {meta.Item.Genres.Count()}");
        Assert.False(string.IsNullOrEmpty(meta.Item.Name), "Name 为空");
        Assert.True(meta.People.Count > 0, "无演员");
    }
}
