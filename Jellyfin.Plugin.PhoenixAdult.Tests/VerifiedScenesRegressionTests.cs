using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using PhoenixAdult.Providers;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// 已验证场景回归测试：文件名来自实际验证用例（pornrips 风格 Site.YY.MM.DD.Actors.Title）。
/// 每个用例在真实站点上已验证全链路（Search 命中 + Update 日期/演员），固化作为回归保护：
/// 若站点改版/搜索词策略失效/日期或演员解析退化，此测试会失败。
/// 运行需要网络（与 PornripsRegressionTests 一样属于 Online 冒烟）。
/// </summary>
[Trait("Category", "Online")]
public class VerifiedScenesRegressionTests
{
    /// <summary>
    /// 已验证场景：文件名 -> 期望的标题片段 / Update 日期 / 期望演员（宽松 Contains 匹配）。
    /// </summary>
    private static readonly (string FileName, string TitlePart, string Date, string Actor1, string Actor2)[] Verified =
    {
        ("HomeGrownEurope.26.05.16.Shina.Ryen", "Screw my skinny pussy now", "2026-05-16", "Shinaryen", "Alex Ryen"),
        ("AngelsLove.26.07.11.Vixi.Rafi.And.Matty.Mila.Perez.Too.Late.Not.Quite", "Too Late", "2026-07-11", "Vixi Rafi", "Matty"),
        ("JulesJordan.26.06.19.Lucy.Mochi.Glamorous.Asian.Beauty.Mochis.Passion.Ignites.With.Manuel.Ferrara", "Glamorous Asian Beauty", "2026-06-19", "Lucy Mochi", "Manuel Ferrara"),
        ("DorcelClub.26.05.06.Candee.Licious.Dakota.Dove.Alyssa.Bounty.And.Black.Angel.Lesbian.Temptations.At.The.Gym", "Lesbian temptations at the gym", "2026-05-06", "Candee Licious", "Black Angel"),
        ("MomSwap.26.06.18.Skylar.Snow.And.Penny.Barber.", "Gold Star System", "2026-06-18", "Penny Barber", "Skylar Snow"),
        ("Wicked.26.07.03.Blake.Blossom.And.Melissa.Stratton", "Doctor Will See You Now", "2026-07-03", "Blake Blossom", "Melissa Stratton"),
        ("Transfixed.26.07.01.Ariel.Demure.and.Penny.Barber.An.Easy.Prospect.", "An Easy Prospect", "2026-07-01", "Ariel Demure", "Penny Barber"),
        ("Cum4K.26.06.30.Aubry.Babcock.Hot.Handywoman", "Hot Handywoman", "2026-06-30", "Aubry Babcock", "Sam Shock"),
        ("NewSensations.26.06.27.Lucy.Mochi.", "Lucy Mochi Finds Out", "2026-06-27", "Lucy Mochi", "Vince Karter"),
        ("DirtyWivesClub.26.08.18.Lucy.Mochi", "Hot and Married Lucy Mochi", "2026-08-18", "Lucy Mochi", "Codey Steele"),
        ("SinDeLuxe.26.07.15.Olivia.Sparkle.Prologue.Chapter.6", "Prologue - Chapter 6", "2026-07-15", "Olivia Sparkle", "Charlie Dean"),
        // Hegre：匿名页按 IP 地理服务返回中文标题/演员（og:title 中文），日期/演员受会员墙限制，
        // 只断言日期与 HasMetadata（标题/演员断言置 null 跳过）
        ("Hegre.26.08.11.Nyx.And.Syrena.Lesbian.Leisure.Part.1", null, "2026-08-11", null, null),
    };

    [Fact(DisplayName = "回归：已验证场景全链路（Search 命中 + Update 日期/演员）")]
    public async Task Verified_Scenes_Full_Chain()
    {
        TestInitializer.Init();
        var lines = new List<string>();
        var failures = new List<string>();

        foreach (var (fileName, titlePart, date, actor1, actor2) in Verified)
        {
            var siteName = fileName.Split('.')[0];
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                var movieProvider = new MovieProvider();
                var info = new MovieInfo { Name = fileName };

                var searchResults = await movieProvider.GetSearchResults(info, cts.Token);
                if (!searchResults.Any())
                {
                    failures.Add($"{siteName}: Search 0 结果");
                    lines.Add($"❌ {siteName,-16} Search 0 结果");
                    continue;
                }

                var target = searchResults.First();
                info.ProviderIds = target.ProviderIds;
                info.PremiereDate = target.PremiereDate;

                var meta = await movieProvider.GetMetadata(info, cts.Token);
                var metaDate = meta.Item.PremiereDate?.ToString("yyyy-MM-dd") ?? string.Empty;
                var actors = string.Join(", ", meta.People.Select(p => p.Name));

                var ok = meta.HasMetadata
                    && metaDate == date
                    && (titlePart == null || meta.Item.Name?.Contains(titlePart, StringComparison.OrdinalIgnoreCase) == true)
                    && (actor1 == null || meta.People.Any(p => p.Name.Contains(actor1, StringComparison.OrdinalIgnoreCase)))
                    && (actor2 == null || meta.People.Any(p => p.Name.Contains(actor2, StringComparison.OrdinalIgnoreCase)));

                lines.Add($"{(ok ? "✅" : "❌")} {siteName,-16} '{meta.Item.Name}' {metaDate} actors({meta.People.Count}): {actors}");
                if (!ok)
                {
                    failures.Add($"{siteName}: 全链路断言失败 (Name={meta.Item.Name} date={metaDate} actors={actors})");
                }
            }
            catch (Exception ex)
            {
                var msg = ex.Message.Split('\n')[0];
                lines.Add($"❌ {siteName,-16} {ex.GetType().Name}: {msg[..Math.Min(80, msg.Length)]}");
                failures.Add($"{siteName}: {ex.GetType().Name}: {msg[..Math.Min(60, msg.Length)]}");
            }
        }

        Console.WriteLine(string.Join(Environment.NewLine, lines));
        Assert.True(failures.Count == 0, $"退化 {failures.Count} 个: {string.Join("; ", failures)}");
    }
}
