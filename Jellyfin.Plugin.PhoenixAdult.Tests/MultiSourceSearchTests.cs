using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Providers;
using PhoenixAdult;
using PhoenixAdult.Providers;
using Xunit;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// 多源搜索在线测试：同一个番号应并发检索多个站点并合并结果（不再绑定单一站点）。
/// </summary>
[Trait("Category", "Online")]
public class MultiSourceSearchTests
{
    public static IEnumerable<object[]> Cases => new List<object[]>
    {
        // 查询词, 期望命中来源里的 group（任一结果带该 group 前缀即可）
        new object[] { "MDSR-0005", 234 },        // 麻豆系：MadouQu
        new object[] { "XJX-0813", 234 },         // 麻豆系：sitemap 采集到的非 MD 厂牌
        new object[] { "SSIS-354", 239 },         // JAV 系：AVBase
        new object[] { "FC2-PPV-4680562", 238 },  // FC2：官方
    };

    [Theory(DisplayName = "多源番号搜索")]
    [MemberData(nameof(Cases))]
    public async Task MultiSource_ContainsExpectedProvider(string term, int expectedGroup)
    {
        TestInitializer.Init();
        var provider = new MovieProvider();

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var results = (await provider.GetSearchResults(new MovieInfo { Name = term }, false, cts.Token)).ToList();

        Assert.True(results.Count > 0, $"{term}: 多源搜索无结果");

        var groups = results
            .Select(r => r.ProviderIds.TryGetValue(global::PhoenixAdult.Plugin.Instance.Name, out var id) ? id.Split('#')[0] : null)
            .Where(o => o != null)
            .Distinct()
            .ToList();

        Assert.True(
            groups.Contains(expectedGroup.ToString()),
            $"{term}: 期望来源 group {expectedGroup}，实际来源 groups=[{string.Join(",", groups)}]");
    }
}
