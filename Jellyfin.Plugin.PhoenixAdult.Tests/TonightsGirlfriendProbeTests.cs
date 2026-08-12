using PhoenixAdult.Helpers;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// SiteTonightsGirlfriend 探针：修复 Update 演员/图片 NRE。
/// </summary>
[Trait("Category", "Online")]
public class TonightsGirlfriendProbeTests
{
    [Fact(DisplayName = "TonightsGirlfriend: Search 不崩溃")]
    public async Task Search_No_Crash()
    {
        TestInitializer.Init();
        var provider = Helper.GetBaseSiteByName("SiteTonightsGirlfriend");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var results = await provider!.Search(new int[] { 59, 0 }, "Kate Dalia", null, cts.Token);
        Console.WriteLine($"results: {results.Count}");
    }

    [Fact(DisplayName = "TonightsGirlfriend: Update 直抓场景 URL")]
    public async Task Update_Scene_URL()
    {
        TestInitializer.Init();
        var provider = Helper.GetBaseSiteByName("SiteTonightsGirlfriend");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var sceneUrl = "https://www.tonightsgirlfriend.com/scene/kate-dalia-happily-welcomes-her-new-stepdaughter-home";
        var meta = await provider.Update(new int[] { 59, 0 }, new[] { Helper.Encode(sceneUrl) }, cts.Token);
        Console.WriteLine($"Update: Name='{meta.Item.Name}' 演员 {meta.People.Count}");
    }
}
