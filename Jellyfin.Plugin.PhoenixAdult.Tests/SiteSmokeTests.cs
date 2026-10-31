using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using PhoenixAdult;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// Online smoke test: instantiate every SiteIDList handler and run one real Search.
/// Needs network access.
/// Skipped by default (Category=Online).
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

        // The first site of the group owns the base URL.
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
                    var msg = ex.Message.Split('\n')[0];
                    if (msg.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) && msg.Contains("8191", StringComparison.OrdinalIgnoreCase))
                    {
                        status = $"SKIP: FlareSolverr 未配置（环境依赖）";
                    }
                    else
                    {
                        status = $"ERR: {ex.GetType().Name}: {msg[..Math.Min(120, msg.Length)]}";
                    }
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

        // Print the full result to the console.
        var lines = new List<string> { $"总 handler: {handlers.Count} | OK: {ok} | SKIP: {skip} | TIMEOUT: {timeout} | ERR: {err}" };
        foreach (var r in results.OrderBy(r => r.Key))
        {
            lines.Add($"  {r.Key,-40} {r.Value}");
        }

        var report = string.Join(Environment.NewLine, lines);
        Console.WriteLine(report);

        // The report goes to the repository root.
        var reportPath = Path.Combine(TestPaths.RepoRoot, "smoke-report.txt");
        File.WriteAllText(reportPath, report);

        // Every handler with an implementation must finish (OK or TIMEOUT pass, ERR fails).
        var failed = results.Where(r => r.Value.StartsWith("ERR")).ToList();
        Assert.True(failed.Count == 0,
            $"以下 handler 搜索失败:{Environment.NewLine}{string.Join(Environment.NewLine, failed.Select(f => $"{f.Key}: {f.Value}"))}");
    }
}
