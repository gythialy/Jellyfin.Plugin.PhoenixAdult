using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PhoenixAdult;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;
using PhoenixAdult.Sites;
using Xunit;
using Xunit.Sdk;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// MetaTube 移植站点的冒烟测试：Search → Update → GetImages → 图片 URL 可达。
/// 每条用例对应一个站点/一条真实样例；站点维护或被 Cloudflare/地域封锁的用例标 allowZero
/// （只验证不崩溃，站点恢复后应改为严格档）。
/// </summary>
[Trait("Category", "Online")]
public class MetatubePortSmokeTests
{
    public static IEnumerable<object[]> Cases => new List<object[]>
    {
        // handler, group, site, searchTerm, minResults, allowZero
        new object[] { "SiteMadouQu", 234, 0, "MDX-0267", 1, false },
        new object[] { "ActorBabepedia", 231, 0, "Emiri Momota", 1, false },
        new object[] { "ActorAvLeague", 232, 0, "北条麻妃", 1, false },
        new object[] { "SiteModelMediaAsia", 235, 0, "MDCM-0013", 0, true },
        new object[] { "NetworkPondo", 236, 0, "042922_01", 1, false },
        new object[] { "NetworkPondo", 236, 1, "071319_870", 1, false },
        new object[] { "NetworkPondo", 236, 2, "091522_959", 1, false },
        new object[] { "NetworkPondo", 236, 3, "032622_623", 1, false },
        new object[] { "NetworkFaleno", 237, 0, "dldss339", 1, false },
        new object[] { "NetworkFaleno", 237, 1, "fsdss754", 0, true },
        new object[] { "SiteFC2", 238, 0, "4680562", 1, false },
        new object[] { "SiteAVBase", 239, 0, "SSIS-354", 1, false },
        new object[] { "SiteDuga", 240, 0, "glory-4262", 1, false },
        new object[] { "SiteMyWife", 241, 0, "2320", 1, false },
        new object[] { "SiteJavFree", 242, 0, "4995343", 1, false },
        new object[] { "SiteMgsStage", 243, 0, "ABP-177", 0, true },
        new object[] { "SiteSOD", 244, 0, "STAR-399", 0, true },
        new object[] { "SiteTokyoHot", 245, 0, "n1612", 1, false },
        new object[] { "SiteGetchu", 246, 0, "4012345", 1, false },
        new object[] { "SitePcolle", 247, 0, "2772796a0e98aba0a28", 1, false },
        new object[] { "SiteFC2Hub", 248, 0, "4680562", 0, true },
        new object[] { "SiteFC2PPVDB", 249, 0, "4680562", 0, true },
        new object[] { "SiteAVEntertainments", 250, 0, "100001", 1, false },
        new object[] { "SiteHeyDouga", 253, 0, "4037-479", 0, true },
        new object[] { "NetworkD2Pass", 254, 0, "ki220913", 0, true },
        new object[] { "SiteFanza", 251, 0, "SSIS-123", 1, false },
        new object[] { "SiteGcolle", 252, 0, "123456", 0, true },

        // 既有 JAV handler（P2 修复后的回归基线）
        new object[] { "NetworkJavBus", 96, 0, "ABP-123", 1, false },
        new object[] { "NetworkJAVLibrary", 38, 0, "ABP-123", 0, true },
        new object[] { "NetworkR18", 17, 0, "ABP-123", 0, true },
        new object[] { "NetworkJavBus", 96, 0, "HEYZO-0929", 1, false },
        new object[] { "SiteJavDB", 255, 0, "SSIS-354", 1, false },
    };

    [Theory(DisplayName = "移植站点冒烟")]
    [MemberData(nameof(Cases))]
    public async Task Port_Smoke(string handler, int group, int site, string term, int minResults, bool allowZero)
    {
        TestInitializer.Init();
        var provider = Helper.GetBaseSiteByName(handler) ?? throw new XunitException($"{handler}: 无实现类");
        var siteNum = new[] { group, site };

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        var results = await provider.Search(siteNum, term, null, timeout.Token).ConfigureAwait(false);
        if (results.Count < minResults)
        {
            if (allowZero)
            {
                Console.WriteLine($"⚠️ {handler}::{site} 「{term}」0 结果（allowZero：站点维护/封锁，仅验证不崩溃）");
                return;
            }

            throw new XunitException($"{handler}::{site} 搜索「{term}」期望 ≥{minResults}，实际 {results.Count}");
        }

        if (results.Count == 0)
        {
            Console.WriteLine($"⚠️ {handler}::{site} 「{term}」0 结果（allowZero）");
            return;
        }

        var first = results[0];
        var rawId = first.ProviderIds.TryGetValue(global::PhoenixAdult.Plugin.Instance.Name, out var value) ? value : null;
        if (string.IsNullOrEmpty(rawId))
        {
            throw new XunitException($"{handler}: 搜索结果缺少 ProviderId");
        }

        var parts = rawId.Split('#');
        var sceneID = parts.Length >= 3 ? parts.Skip(2).ToArray() : new[] { rawId };

        var meta = await provider.Update(siteNum, sceneID, timeout.Token).ConfigureAwait(false);
        if (meta?.Item == null || string.IsNullOrEmpty(meta.Item.Name))
        {
            if (allowZero)
            {
                Console.WriteLine($"⚠️ {handler}::{site} Update 空（allowZero：站点维护/封锁）");
                return;
            }

            throw new XunitException($"{handler}::{site} Update 未产出元数据（Name 为空）");
        }

        var images = (await provider.GetImages(siteNum, sceneID, meta.Item, timeout.Token).ConfigureAwait(false)).ToList();
        if (images.Count == 0 || images.All(i => string.IsNullOrEmpty(i.Url)))
        {
            if (allowZero)
            {
                Console.WriteLine($"⚠️ {handler}::{site} 无图片（allowZero）");
                return;
            }

            throw new XunitException($"{handler}::{site} GetImages 未返回任何图片 URL");
        }

        var imageUrl = images.First(i => !string.IsNullOrEmpty(i.Url)).Url;
        using var head = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var check = await HTTP.Request(imageUrl, HttpMethod.Head, head.Token).ConfigureAwait(false);
        if (!check.IsOK && check.StatusCode != System.Net.HttpStatusCode.MethodNotAllowed && check.StatusCode != System.Net.HttpStatusCode.Forbidden)
        {
            throw new XunitException($"{handler}::{site} 图片不可达：{imageUrl} ({(int)check.StatusCode})");
        }

        Console.WriteLine($"✅ {handler}::{site} 「{term}」-> {meta.Item.Name} | 图片 {images.Count} 张 | {imageUrl}");
    }
}
