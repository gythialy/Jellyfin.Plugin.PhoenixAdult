using PhoenixAdult.Helpers;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// SiteJulesJordan 改版修复回归测试：Search -> Update -> GetImages 全链路。
/// </summary>
[Trait("Category", "Online")]
public class JulesJordanProbeTests
{
    [Fact(DisplayName = "JulesJordan 全链路回归")]
    public async Task JulesJordan_FullFlow()
    {
        TestInitializer.Init();
        var lines = new List<string>();
        var provider = Helper.GetBaseSiteByName("SiteJulesJordan");
        Assert.NotNull(provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var results = await provider!.Search(new int[] { 25, 0 }, "Vanessa Blake Takes A BBC Up Her Ass", null, cts.Token);
        Assert.True(results.Count > 0, "Search 应返回结果");
        var r0 = results[0];
        var sceneId = r0.ProviderIds.First().Value;

        var update = await provider.Update(new int[] { 25, 0 }, sceneId.Split('#'), cts.Token);
        Assert.True(update.HasMetadata, "Update 应有元数据");
        Assert.True(update.People.Count > 0, "Update 应有演员");

        var images = (await provider.GetImages(new int[] { 25, 0 }, sceneId.Split('#'), update.Item, cts.Token)).ToList();
        lines.Add($"Search: {results.Count} 结果, Name={r0.Name}");
        lines.Add($"Update: {update.People.Count} 演员, {update.Item.Genres.Count()} 流派, Overview {update.Item.Overview?.Length} chars");
        lines.Add($"GetImages: {images.Count} 张");
        Assert.True(images.Count > 0, "应有图片");

        Console.WriteLine(string.Join("\n", lines));
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "julesjordan-report.txt"), lines);
    }
}
