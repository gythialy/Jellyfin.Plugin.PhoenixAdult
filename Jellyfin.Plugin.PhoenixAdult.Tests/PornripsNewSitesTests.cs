using PhoenixAdult.Helpers;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// pornrips 未实测站点批量验证（2026-08-11 扩展）。
/// 从 pornrips.to /feed/ 分页（10 页 883 标题 / 210 站点）匹配 sitelist，
/// 覆盖之前未用真实标题测过的 handler。
/// </summary>
[Trait("Category", "Online")]
public class PornripsNewSitesTests
{
    private static readonly (string Handler, int Group, int Site, string SiteName, string RssTitle)[] Probes =
    {
        ("SiteWatch4Beauty", 221, 0, "Watch4Beauty", "Watch4Beauty.26.08.10.Irene.Rouse.Flexible.In.Black.XXX.720p.HEVC.x265.PRT"),
        ("NetworkVNA", 121, 26, "RomeMajor", "RomeMajor.26.07.31.Xbiz.Bikini.Contest.BTS.XXX.720p.HEVC.x265.PRT"),
        ("SiteNaughtyAmerica", 10, 22, "MyFriendsHotGirl", "MyFriendsHotGirl.26.08.10.JoJo.Austin.XXX.720p.HEVC.x265.PRT"),
        ("NetworkNubiles", 28, 1, "Nubiles", "Nubiles.26.08.10.Maia.Spir.Make.Me.Sweat.XXX.720p.HEVC.x265.PRT"),
        ("SiteJesseLoadsMonsterFacials", 164, 0, "JesseLoadsMonsterFacials", "JesseLoadsMonsterFacials.26.08.09.Carly.Kiss.XXX.720p.HEVC.x265.PRT"),
        ("NetworkAdultEmpireCash", 71, 1, "SpankMonster", "SpankMonster.26.08.06.Kloe.Love.XXX.720p.HEVC.x265.PRT"),
        ("NetworkVIP4K", 120, 4, "Hunt4K", "Hunt4K.26.08.06.Larisa.XXX.720p.HEVC.x265.PRT"),
        ("NetworkGammaEntOther", 64, 75, "GangbangCreampie", "GangbangCreampie.26.08.10.Angelina.Moon.Blowbang.XXX.720p.HEVC.x265.PRT"),
        ("SiteBrandNewAmateurs", 137, 0, "BrandNewAmateurs", "BrandNewAmateurs.26.07.28.Itty.Bitty.Lil.Haven.Moss.Fuck.Suck.Teasing.XXX.720p.HEVC.x265.PRT"),
        ("SiteATKGirlfriends", 131, 0, "ATKGirlfriends", "ATKGirlfriends.26.08.10.Lana.Gee.Sarasota.4.XXX.720p.HEVC.x265.PRT"),
        ("NetworkTeenMegaWorld", 116, 7, "Beauty-Angels", "Beauty-Angels.26.08.09.Luna.Evans.XXX.720p.HEVC.x265.PRT"),
        ("NetworkAuntJudys", 73, 0, "AuntJudysXXX", "AuntJudysXXX.26.08.09.Sadie.Star.XXX.720p.HEVC.x265.PRT"),
        ("SiteSpizoo", 202, 8, "MrLuckyPOV", "MrLuckyPOV.26.07.31.Kathryn.Mae.Petite.Blonde.Slut.Drains.Cock.XXX.720p.HEVC.x265.PRT"),
        ("SiteFamilyTherapyXXX", 52, 0, "FamilyTherapyXXX", "FamilyTherapyXXX.26.08.04.Angel.Youngs.The.Secret.Deal.XXX.720p.HEVC.x265.PRT"),
        ("NetworkPornWorld", 106, 16, "PornWorld", "PornWorld.26.08.09.Jena.Larose.XXX.720p.HEVC.x265.PRT"),
        ("SiteSexMex", 199, 0, "SexMex", "SexMex.26.08.08.Gabriela.Veracruz.XXX.720p.HEVC.x265.PRT"),
        ("NetworkScoreGroup", 111, 0, "PornMegaLoad", "PornMegaLoad.26.08.08.Barbie.Nicole.Solo.42571.XXX.720p.HEVC.x265.PRT"),
        ("SitePenthouseGold", 183, 0, "PenthouseGold", "PenthouseGold.26.08.08.Angel.Golightly.XXX.720p.HEVC.x265.PRT"),
        ("NetworkFTV", 88, 1, "FTVGirls", "FTVGirls.26.08.05.Cecee.Pretty.And.Petite.XXX.720p.HEVC.x265.PRT"),
        ("SiteGirlsOutWest", 154, 0, "GirlsOutWest", "GirlsOutWest.26.08.09.Cartier.Rose.And.Hazel.Leone.XXX.720p.HEVC.x265.PRT"),
        ("NetworkRadicalCash", 67, 4, "BigGulpGirls", "BigGulpGirls.26.08.08.Lola.Aiko.XXX.720p.HEVC.x265.PRT"),
        ("SiteFemjoy", 149, 0, "FemJoy", "FemJoy.26.08.08.Wikki.K.Cozy.Sensuality.XXX.720p.HEVC.x265.PRT"),
        ("NetworkKink", 16, 29, "WhippedAss", "WhippedAss.26.08.05.Kasey.Warner.And.Tessa.Thomas.XXX.720p.HEVC.x265.PRT"),
        ("NetworkBellaPass", 75, 18, "SeeHimFuck", "SeeHimFuck.26.08.07.Brayden.Banks.And.Mira.Luv.XXX.720p.HEVC.x265.PRT"),
        ("SitePlumperPass", 186, 0, "PlumperPass", "PlumperPass.26.08.07.Peggy.Deville.XXX.720p.HEVC.x265.PRT"),
        ("SiteNewSensations", 55, 0, "NewSensations", "NewSensations.26.08.08.Summer.Stevens.XXX.720p.HEVC.x265.PRT"),
        ("SitePrivate", 190, 0, "Private", "Private.26.08.08.Renata.Fox.XXX.720p.HEVC.x265.PRT"),
        ("SitePlayboyPlus", 185, 0, "PlayboyPlus", "PlayboyPlus.26.08.08.Sunlit.Allure.XXX.720p.HEVC.x265.PRT"),
        ("SiteAbbyWinters", 57, 0, "AbbyWinters", "AbbyWinters.26.08.07.Larisa.And.Ivi.Facesitting.XXX.720p.HEVC.x265.PRT"),
        ("SitePurgatoryX", 61, 0, "PurgatoryX", "PurgatoryX.26.07.31.Audrey.Reid.XXX.720p.HEVC.x265.PRT"),
        ("SiteTonightsGirlfriend", 59, 0, "TonightsGirlfriend", "TonightsGirlfriend.26.08.07.Kate.Dalia.XXX.720p.HEVC.x265.PRT"),
        ("NetworkModelCentro", 100, 18, "SlutInspection", "SlutInspection.26.07.31.Luna.Lark.Thick.Sluts.Do.It.Better.XXX.720p.HEVC.x265.PRT"),
        ("NetworkPureCFNM", 108, 1, "PureCFNM", "PureCFNM.26.08.07.Charlotte.Rose.Jasmine.Fine.And.Robyn.Quinn.Hazmat.Facial.XXX.720p.HEVC.x265.PRT"),
        ("NetworkKellyMadison", 97, 0, "PornFidelity", "PornFidelity.E1168.Serena.Sterling.XXX.720p.HEVC.x265.PRT"),
        ("NetworkBang", 1, 0, "Bang", "Bang.YNGR.26.08.07.JoJo.Austin.XXX.720p.HEVC.x265.PRT"),
        ("NetworkDogfart", 19, 1, "BlacksOnBlondes", "BlacksOnBlondes.26.08.07.Harley.Love.XXX.720p.HEVC.x265.PRT"),
        ("SiteOnlyFans", 182, 0, "OnlyFans", "OnlyFans.25.02.22.Veronica.Diaz.Aka.PrettyPinkCatWomen.Nothing.Like.A.Quickie.Before.A.Shower.XXX.720p.HEVC.x265.PRT"),
        ("SiteInterracialPass", 160, 6, "HotMilfsFuck", "HotMilfsFuck.26.07.26.Jasmine.If.You.Dont.Love.Me.Lie.To.Me.XXX.720p.HEVC.x265.PRT"),
        ("NetworkThickCashOther", 118, 0, "MilfAF", "MilfAF.26.07.30.Harlie.Hotwife.XXX.720p.HEVC.x265.PRT"),
        ("SiteHegre", 37, 0, "Hegre", "Hegre.26.08.04.Malena.A.Nude.Fashion.XXX.720p.HEVC.x265.PRT"),
        ("SiteMissaX", 176, 0, "MissaX", "MissaX.26.01.16.Hazel.Heart.And.Kell.Fire.The.Tea.Party.XXX.720p.HEVC.x265.PRT"),
        ("NetworkExploitedX", 58, 2, "BBCSurprise", "BBCSurprise.26.08.01.Brittany.Loveless.Big.Dick.Good.Day.XXX.720p.HEVC.x265.PRT"),
        ("Network5Kporn", 70, 0, "5KPorn", "5KPorn.26.08.04.Remi.Raw.XXX.720p.HEVC.x265.PRT"),
        ("NetworkMylf", 24, 31, "FamilyStrokes", "FamilyStrokes.26.08.07.Della.Cate.And.Koda.Monroe.XXX.720p.HEVC.x265.PRT"),
        ("Network1service", 22, 35, "MomIsHorny", "MomIsHorny.26.08.07.Melissa.Moore.XXX.720p.HEVC.x265.PRT"),
        ("Network1service", 2, 14, "DigitalPlayground", "DigitalPlayground.26.08.06.Rissa.May.Just.Visiting.Episode.1.XXX.720p.HEVC.x265.PRT"),
        ("Network1service", 8, 3, "PublicAgent", "PublicAgent.26.08.06.Violet.Viper.XXX.720p.HEVC.x265.PRT"),
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

    [Fact(DisplayName = "pornrips 新站点批量验证（47 站）")]
    public async Task New_Sites_Probe()
    {
        TestInitializer.Init();
        var lines = new List<string> { "== pornrips 新站点验证（未实测 handler）==" };
        var ok = new List<string>();
        var zero = new List<string>();
        var crash = new List<string>();

        foreach (var (handler, group, site, siteName, rssTitle) in Probes)
        {
            var searchTerm = CleanRssTitle(rssTitle, siteName);
            var provider = Helper.GetBaseSiteByName(handler);

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var results = await provider!.Search(new int[] { group, site }, searchTerm, null, cts.Token);
                sw.Stop();
                if (results.Count > 0)
                {
                    lines.Add($"✅ {siteName,-22} [{handler}] {results.Count,3} 结果 {sw.ElapsedMilliseconds,5}ms | first: {results[0].Name}");
                    ok.Add(siteName);
                }
                else
                {
                    lines.Add($"⚠️  {siteName,-22} [{handler}] 0 结果 {sw.ElapsedMilliseconds,5}ms | \"{searchTerm}\"");
                    zero.Add(siteName);
                }
            }
            catch (Exception ex)
            {
                var msg = ex.Message.Split('\n')[0];
                lines.Add($"❌ {siteName,-22} [{handler}] {ex.GetType().Name}: {msg[..Math.Min(70, msg.Length)]}");
                crash.Add(siteName);
            }
        }

        lines.Add("");
        lines.Add($"== 汇总: OK {ok.Count} / ZERO {zero.Count} / CRASH {crash.Count} ==");
        if (zero.Any()) lines.Add($"0 结果: {string.Join(", ", zero)}");
        if (crash.Any()) lines.Add($"崩溃: {string.Join(", ", crash)}");

        var report = string.Join(Environment.NewLine, lines);
        Console.WriteLine(report);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "pornrips-new-sites-report.txt"), report);
        Assert.True(crash.Count == 0, $"实现崩溃 {crash.Count} 个: {string.Join("; ", crash)}");
    }
}
