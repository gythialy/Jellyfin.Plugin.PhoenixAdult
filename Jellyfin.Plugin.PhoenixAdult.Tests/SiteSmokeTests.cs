using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using PhoenixAdult;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// 在线冒烟测试：对每个 SiteIDList handler 实例化并执行一次真实 Search 请求，
/// 验证实现类能正常解析站点页面。需要网络访问。
/// 默认跳过（[Trait("Category", "Online")]），在 docker 测试环境中执行。
/// </summary>
[Trait("Category", "Online")]
public class SiteSmokeTests
{
    private static readonly string DataDir = TestPaths.DataDir;

    private static readonly string SiteListPath = Path.Combine(DataDir, "SiteList.json");

    private static List<(string Group, string Handler)> GetHandlers()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SiteListPath));
        var root = doc.RootElement;
        var list = new List<(string, string)>();
        foreach (var group in root.GetProperty("SiteIDList").EnumerateObject())
        {
            list.Add((group.Name, group.Value.GetString()!));
        }

        return list;
    }

    private static (int Group, int Site, string SearchUrl, string Title) GetProbe(int group)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SiteListPath));
        var root = doc.RootElement;
        var sites = root.GetProperty("Sites");
        if (!sites.TryGetProperty(group.ToString(), out var groupSites))
        {
            return (group, 0, string.Empty, string.Empty);
        }

        // 组内第一个站点作为探测目标（base URL 拥有者）
        var first = groupSites.EnumerateObject().First();
        var rec = first.Value;
        var url = rec.GetArrayLength() >= 2 ? rec[1].GetString() : string.Empty;
        var name = rec.GetArrayLength() >= 1 ? rec[0].GetString() : string.Empty;
        return (group, int.Parse(first.Name), url ?? string.Empty, name ?? string.Empty);
    }

    [Fact(DisplayName = "所有实现类能完成一次真实 Search（含网络）")]
    public async Task All_Providers_Search_Online()
    {
        TestInitializer.Init();

        var handlers = GetHandlers();
        var results = new ConcurrentDictionary<string, string>();
        var semaphore = new SemaphoreSlim(8);

        var tasks = handlers.Select(async h =>
        {
            await semaphore.WaitAsync();
            try
            {
                var (group, site, url, name) = GetProbe(int.Parse(h.Group));
                var stopwatch = Stopwatch.StartNew();

                var provider = Helper.GetBaseSiteByName(h.Handler);
                if (provider == null)
                {
                    results[h.Handler] = $"SKIP: 无实现类";
                    return;
                }

                string status;
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                    var found = await provider.Search(
                        new int[] { group, site },
                        "Scene",
                        null,
                        cts.Token);
                    stopwatch.Stop();
                    status = $"OK ({found.Count} results, {stopwatch.ElapsedMilliseconds}ms)";
                }
                catch (TaskCanceledException)
                {
                    status = "TIMEOUT";
                }
                catch (Exception ex)
                {
                    status = $"ERR: {ex.GetType().Name}: {ex.Message.Split('\n')[0][..Math.Min(120, ex.Message.Split('\n')[0].Length)]}";
                }

                results[h.Handler] = status;
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        var ok = results.Count(r => r.Value.StartsWith("OK"));
        var skip = results.Count(r => r.Value.StartsWith("SKIP"));
        var timeout = results.Count(r => r.Value == "TIMEOUT");
        var err = results.Count(r => r.Value.StartsWith("ERR"));

        // 输出完整结果到控制台（TestOutput 模式）
        var lines = new List<string> { $"总 handler: {handlers.Count} | OK: {ok} | SKIP: {skip} | TIMEOUT: {timeout} | ERR: {err}" };
        foreach (var r in results.OrderBy(r => r.Key))
        {
            lines.Add($"  {r.Key,-40} {r.Value}");
        }

        var report = string.Join(Environment.NewLine, lines);
        Console.WriteLine(report);

        // 报告写到仓库根（docker 挂载或 PHOENIX_REPO_ROOT 可见）
        var reportPath = Path.Combine(TestPaths.RepoRoot, "smoke-report.txt");
        File.WriteAllText(reportPath, report);

        // 断言：所有有实现类的 handler 都必须能完成请求（OK 或 TIMEOUT 可接受，ERR 视为失败）
        var failed = results.Where(r => r.Value.StartsWith("ERR")).ToList();
        Assert.True(failed.Count == 0,
            $"以下 handler 搜索失败:{Environment.NewLine}{string.Join(Environment.NewLine, failed.Select(f => $"{f.Key}: {f.Value}"))}");
    }
}
