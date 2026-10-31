using System.Threading.Tasks;
using PhoenixAdult.Helpers;
using PhoenixAdult.Sites;
using Xunit;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// Network1service regression: instance_token acquisition
/// and the Search to GetImages pipeline.
/// </summary>
[Trait("Category", "Online")]
public class Network1serviceTests
{
    [Fact(DisplayName = "Network1service token 获取与 Brazzers 全链路")]
    public async Task Token_And_Brazzers_Chain()
    {
        TestInitializer.Init();
        var siteNum = new[] { 0, 0 };

        var token = await Network1service.GetToken(siteNum, true, default);
        Assert.False(string.IsNullOrEmpty(token), "instance_token 获取失败（HEAD/GET/FlareSolverr 均未拿到）");

        var provider = Helper.GetBaseSiteByName("Network1service");
        Assert.NotNull(provider);

        var results = await provider!.Search(siteNum, "Emily Willis", null, default);
        Assert.True(results.Count > 0, "Brazzers 搜索无结果");

        var sceneID = results[0].ProviderIds[global::PhoenixAdult.Plugin.Instance.Name].Split('#');
        var meta = await provider.Update(siteNum, sceneID, default);
        Assert.False(string.IsNullOrEmpty(meta.Item?.Name), "Update 未产出标题");

        var images = await provider.GetImages(siteNum, sceneID, meta.Item!, default);
        Assert.NotEmpty(images);
    }
}
