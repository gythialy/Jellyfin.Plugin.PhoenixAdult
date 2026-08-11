using PhoenixAdult.Helpers;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// pornrips.to 真实发行标题回归测试。
/// 标题来自 https://pornrips.to/feed/ RSS（2026-08-11 抓取），格式：
/// SiteName.YY.MM.DD.Actor.Scene.XXX.Resolution.Codec.PRT。
/// 这些用例在真实站点上已验证能返回结果，固化作为回归保护：
/// 若站点改版导致解析失效（如 SiteJulesJordan 2026 年改版），此测试会失败。
/// 运行需要网络。
/// </summary>
[Trait("Category", "Online")]
public class PornripsRegressionTests
{
    /// <summary>
    /// 已验证能搜到结果的站点用例（标题 -> 期望 handler）。
    /// 断言至少返回 1 个结果，防止实现退化。
    /// </summary>
    private static readonly (string Handler, int Group, int Site, string RssTitle, string ExpectedFirstResult)[] Verified =
    {
        ("Network1service", 0, 16, "BrazzersExxtra.26.08.10.Ryan.Reid.Double.Indulgence.XXX.720p.HEVC.x265.PRT", "Double Indulgence"),
        ("Network1service", 22, 1, "AssParade.26.08.10.Maya.Luz.XXX.720p.HEVC.x265.PRT", "Big Booty Maya Luz"),
        ("NetworkAdultPrime", 72, 47, "Perfect18.26.07.20.Erika.Fox.XXX.1080p.HEVC.x265.PRT", "Dropping my sheer lingerie"),
        ("NetworkAdultPrime", 72, 61, "SinfulXXX.26.07.24.Gatita.Veve.XXX.1080p.HEVC.x265.PRT", "Colors of Love"),
        ("NetworkAdultPrime", 72, 20, "ElegantRaw.26.07.20.Sweet.Vicki.XXX.720p.HEVC.x265.PRT", "Blonde milf savors"),
        ("NetworkGammaEnt", 11, 1, "EvilAngel.26.07.12.TS.Shakira.Brazil.and.TS.Angel.Hadashian.XXX.720p.HEVC.x265.PRT", "TS ANGEL HADASHIAN"),
        ("NetworkMylf", 24, 32, "PervMom.26.08.09.Tiffani.Time.XXX.1080p.HEVC.x265.PRT", "Practice Time"),
        ("NetworkStrike3", 44, 2, "Vixen.26.08.10.Ella.Hughes.And.Audrey.Reid.Hotel.Vixen.Seaso.XXX.720p.HEVC.x265.PRT", "Hotel Vixen"),
        ("NetworkWowNetwork", 123, 0, "WowGirls.24.12.27.Liz.Ocean.And.Tiffany.Tatum.Toys.For.All.XXX.720p.HEVC.x265.PRT", "WowGirls"),
        ("SiteJulesJordan", 25, 0, "JulesJordan.26.02.17.Vanessa.Blake.Takes.A.Bbc.Up.Her.Ass.XXX.720p.HEVC.x265.PRT", "Thick & Bootylicious"),
    };

    /// <summary>
    /// 已验证不崩溃但搜索词（RSS 标题常只有演员名）搜不到结果的用例。
    /// 断言：不抛异常即可（0 结果可接受）。
    /// </summary>
    private static readonly (string Handler, int Group, int Site, string RssTitle)[] KnownZero =
    {
        ("SiteLegalPorno", 26, 0, "LegalPorno.26.05.24.Hari.Stark.XXX.720p.HEVC.x265.PRT"),
    };

    private static string CleanRssTitle(string rssTitle, string siteName)
    {
        var t = rssTitle;
        var idx = t.IndexOf(siteName, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            t = t[(idx + siteName.Length)..];
        }

        t = t.TrimStart('.');
        var parts = t.Split('.');
        var start = 0;
        if (parts.Length > 0 && System.Text.RegularExpressions.Regex.IsMatch(parts[0], @"^\d{2}$"))
        {
            start = 3; // YY.MM.DD
        }

        var end = parts.Length;
        while (end > start)
        {
            var p = parts[end - 1].ToUpperInvariant();
            if (p is "PRT" or "XXX" or "X265" or "HEVC" or "H264" or "X264" or "720P" or "1080P" or "2160P" or "4K" or "PRT2")
            {
                end--;
            }
            else
            {
                break;
            }
        }

        return string.Join(" ", parts[start..end]).Trim();
    }

    [Fact(DisplayName = "回归：已验证站点必须仍能搜到结果")]
    public async Task Verified_Sites_Must_Return_Results()
    {
        TestInitializer.Init();
        var lines = new List<string>();
        var failures = new List<string>();

        foreach (var (handler, group, site, rssTitle, expected) in Verified)
        {
            var siteName = rssTitle.Split('.')[0];
            var searchTerm = CleanRssTitle(rssTitle, siteName);
            var provider = Helper.GetBaseSiteByName(handler);

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var results = await provider!.Search(new int[] { group, site }, searchTerm, null, cts.Token);
                var first = results.Count > 0 ? results[0].Name : string.Empty;
                lines.Add($"✅ {siteName,-16} [{handler}] {results.Count,3} 结果 | first: {first}");
                if (results.Count == 0)
                {
                    failures.Add($"{siteName}: 0 结果（期望 ≥1，站点可能改版）");
                }
            }
            catch (Exception ex)
            {
                var msg = ex.Message.Split('\n')[0];
                lines.Add($"❌ {siteName,-16} [{handler}] {ex.GetType().Name}: {msg[..Math.Min(80, msg.Length)]}");
                failures.Add($"{siteName}: {ex.GetType().Name}");
            }
        }

        Console.WriteLine(string.Join(Environment.NewLine, lines));
        Assert.True(failures.Count == 0, $"退化 {failures.Count} 个: {string.Join("; ", failures)}");
    }

    [Fact(DisplayName = "回归：已知 0 结果站点不得崩溃")]
    public async Task Known_Zero_Sites_Must_Not_Crash()
    {
        TestInitializer.Init();
        var lines = new List<string>();

        foreach (var (handler, group, site, rssTitle) in KnownZero)
        {
            var siteName = rssTitle.Split('.')[0];
            var searchTerm = CleanRssTitle(rssTitle, siteName);
            var provider = Helper.GetBaseSiteByName(handler);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var results = await provider!.Search(new int[] { group, site }, searchTerm, null, cts.Token); // 不应抛异常
            lines.Add($"✅ {siteName,-16} [{handler}] {results.Count} 结果（不崩溃即通过）");
        }

        Console.WriteLine(string.Join(Environment.NewLine, lines));
    }
}
