using PhoenixAdult.Helpers;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

[Trait("Category", "Online")]
public class NaughtyAmericaProbeTests
{
    [Fact(DisplayName = "NaughtyAmerica: 搜索命中 JoJo Austin")]
    public async Task Search_Hits_JoJo()
    {
        TestInitializer.Init();
        var provider = Helper.GetBaseSiteByName("SiteNaughtyAmerica");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var searchURL = Helper.GetSearchSearchURL(new[] { 10, 22 }) + "JoJo Austin";
        Console.WriteLine($"searchURL: {searchURL}");
        var results = await provider!.Search(new int[] { 10, 22 }, "JoJo Austin", null, cts.Token);

        Assert.True(results.Count >= 1, $"期望 ≥1 结果，实际 {results.Count}");
        Console.WriteLine($"第一结果: {results[0].Name}");
    }

    [Fact(DisplayName = "NaughtyAmerica: Update 直抓场景 URL")]
    public async Task Update_Scene_URL()
    {
        TestInitializer.Init();
        var provider = Helper.GetBaseSiteByName("SiteNaughtyAmerica");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var sceneUrl = "https://www.naughtyamerica.com/scene/the-ever-so-horny-jojo-austin-takes-the-new-roomate-for-a-ride-when-her-boyfriend-steps-out-33944";
        var meta = await provider.Update(new int[] { 10, 22 }, new[] { Helper.Encode(sceneUrl) }, cts.Token);
        Console.WriteLine($"Update: Name='{meta.Item.Name}' date={meta.Item.PremiereDate:yyyy-MM-dd} 演员 {meta.People.Count} 流派 {meta.Item.Genres.Count()}");
        Assert.False(string.IsNullOrEmpty(meta.Item.Name), "Name 为空");
    }
}
