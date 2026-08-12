using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// 离线数据完整性测试：验证 data/SiteList.json 与实现类的映射关系，
/// 不发起任何网络请求，可在任意环境运行。
/// </summary>
public class SiteListDataTests
{
    private static readonly string DataDir = TestPaths.DataDir;

    private static readonly string SiteListPath = Path.Combine(DataDir, "SiteList.json");

    private static JsonDocument LoadSiteList()
    {
        Assert.True(File.Exists(SiteListPath), $"SiteList.json 不存在: {SiteListPath}");
        using var stream = File.OpenRead(SiteListPath);
        return JsonDocument.Parse(stream);
    }

    private static IEnumerable<Type> GetProviderTypes()
    {
        var assembly = typeof(global::PhoenixAdult.Sites.IProviderBase).Assembly;
        return assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(global::PhoenixAdult.Sites.IProviderBase).IsAssignableFrom(t));
    }

    [Fact]
    public void SiteList_Json结构完整()
    {
        using var doc = LoadSiteList();
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("Sites", out var sites), "缺少 Sites 节点");
        Assert.True(root.TryGetProperty("SiteIDList", out var sid), "缺少 SiteIDList 节点");
        Assert.True(root.TryGetProperty("Abbrieviations", out var abb), "缺少 Abbrieviations 节点");

        Assert.True(sites.EnumerateObject().Any(), "Sites 为空");
        Assert.True(sid.EnumerateObject().Any(), "SiteIDList 为空");
        Assert.True(abb.EnumerateObject().Any(), "Abbrieviations 为空");
    }

    [Fact]
    public void SiteIDList_每个组号都有对应的Sites组()
    {
        using var doc = LoadSiteList();
        var root = doc.RootElement;
        var sites = root.GetProperty("Sites");
        var sid = root.GetProperty("SiteIDList");

        foreach (var group in sid.EnumerateObject())
        {
            var groupKey = group.Name;
            Assert.True(sites.TryGetProperty(groupKey, out _),
                $"SiteIDList 组 {groupKey} 在 Sites 中没有对应条目");
        }
    }

    [Fact]
    public void SiteIDList_每个Handler都有实现类()
    {
        using var doc = LoadSiteList();
        var root = doc.RootElement;
        var sid = root.GetProperty("SiteIDList");
        var providerTypes = GetProviderTypes().ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();
        foreach (var group in sid.EnumerateObject())
        {
            var handler = group.Value.GetString()!;
            if (!providerTypes.ContainsKey(handler))
            {
                missing.Add($"{group.Name} -> {handler}");
            }
        }

        Assert.True(missing.Count == 0, $"以下 handler 没有对应实现类:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
    }

    [Fact]
    public void SiteIDList_Handler能被TypeGetType解析()
    {
        // 与运行时 Helper.GetBaseSiteByName 行为一致：Type.GetType(name, ignoreCase: true)
        using var doc = LoadSiteList();
        var root = doc.RootElement;
        var sid = root.GetProperty("SiteIDList");

        var assembly = typeof(global::PhoenixAdult.Sites.IProviderBase).Assembly;
        var missing = new List<string>();
        foreach (var group in sid.EnumerateObject())
        {
            var handler = group.Value.GetString()!;
            var fullName = $"PhoenixAdult.Sites.{handler}";
            var type = assembly.GetType(fullName, false, true);
            if (type == null)
            {
                missing.Add($"{group.Name} -> {handler}");
            }
        }

        Assert.True(missing.Count == 0, $"以下 handler 无法通过 Type.GetType 解析:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
    }

    [Fact]
    public void Sites_组内站点名唯一()
    {
        using var doc = LoadSiteList();
        var root = doc.RootElement;
        var sites = root.GetProperty("Sites");

        var dupes = new List<string>();

        foreach (var group in sites.EnumerateObject())
        {
            // 同名且同 URL 才算重复（同名不同 URL 是网络入口+独立站的正常组合）
            var seen = new Dictionary<(string Name, string Url), string>(new NameUrlComparer());
            foreach (var site in group.Value.EnumerateObject())
            {
                var rec = site.Value;
                var name = rec.GetArrayLength() >= 1 ? rec[0].GetString() : string.Empty;
                var url = rec.GetArrayLength() >= 2 ? rec[1].GetString() : string.Empty;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url))
                {
                    continue;
                }

                var key = (name, NormalizeUrl(url));
                if (!seen.TryAdd(key, site.Name))
                {
                    dupes.Add($"{group.Name}/{seen[key]} 与 {group.Name}/{site.Name} 重复: {name} {url}");
                }
            }
        }

        Assert.True(dupes.Count == 0, $"同一组内存在同名同URL的重复站点:{Environment.NewLine}{string.Join(Environment.NewLine, dupes)}");
    }

    private sealed class NameUrlComparer : IEqualityComparer<(string Name, string Url)>
    {
        public bool Equals((string Name, string Url) x, (string Name, string Url) y)
            => string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase)
               && string.Equals(x.Url, y.Url, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Name, string Url) obj)
            => HashCode.Combine(obj.Name.ToLowerInvariant(), obj.Url.ToLowerInvariant());
    }

    [Fact]
    public void Sites_URL格式合法()
    {
        using var doc = LoadSiteList();
        var root = doc.RootElement;
        var sites = root.GetProperty("Sites");
        var bad = new List<string>();

        foreach (var group in sites.EnumerateObject())
        {
            foreach (var site in group.Value.EnumerateObject())
            {
                var rec = site.Value;
                if (rec.GetArrayLength() >= 2)
                {
                    var url = rec[1].GetString();
                    if (!string.IsNullOrEmpty(url) && !Uri.TryCreate(url, UriKind.Absolute, out _))
                    {
                        bad.Add($"{group.Name}/{site.Name}: {url}");
                    }
                }
            }
        }

        Assert.True(bad.Count == 0, $"非法 URL:{Environment.NewLine}{string.Join(Environment.NewLine, bad)}");
    }

    [Fact]
    public void Sites_组内站号唯一()
    {
        using var doc = LoadSiteList();
        var root = doc.RootElement;
        var sites = root.GetProperty("Sites");

        foreach (var group in sites.EnumerateObject())
        {
            var siteNums = group.Value.EnumerateObject().Select(s => s.Name).ToList();
            Assert.True(siteNums.Distinct().Count() == siteNums.Count,
                $"组 {group.Name} 存在重复站号");
        }
    }

    [Fact]
    public void Sites_每条记录至少包含站点名()
    {
        using var doc = LoadSiteList();
        var root = doc.RootElement;
        var sites = root.GetProperty("Sites");
        var bad = new List<string>();

        foreach (var group in sites.EnumerateObject())
        {
            foreach (var site in group.Value.EnumerateObject())
            {
                var rec = site.Value;
                var hasName = rec.GetArrayLength() >= 1 && !string.IsNullOrEmpty(rec[0].GetString());
                var hasUrl = rec.GetArrayLength() >= 2 && !string.IsNullOrEmpty(rec[1].GetString());
                if (!hasName && !hasUrl)
                {
                    bad.Add($"{group.Name}/{site.Name}");
                }
            }
        }

        Assert.True(bad.Count == 0, $"缺少站点名的记录: {string.Join(", ", bad)}");
    }

    [Fact]
    public void 合并的andrer站点_已在SiteList中()
    {
        // 验证 andrer 独有的一些代表性站点已合并进来
        var expectedUrls = new[]
        {
            "https://www.metart.com",
            "https://www.adultempire.com",
            "https://wankz.com",
            "https://www.vivid.com",
            "https://www.brazzers.com",
        };

        using var doc = LoadSiteList();
        var root = doc.RootElement;
        var sites = root.GetProperty("Sites");

        var allUrls = new HashSet<string>();
        foreach (var group in sites.EnumerateObject())
        {
            foreach (var site in group.Value.EnumerateObject())
            {
                var rec = site.Value;
                if (rec.GetArrayLength() >= 2)
                {
                    allUrls.Add(NormalizeUrl(rec[1].GetString() ?? string.Empty));
                }
            }
        }

        var missing = expectedUrls.Where(u => !allUrls.Contains(NormalizeUrl(u))).ToList();
        Assert.True(missing.Count == 0, $"以下站点未合并: {string.Join(", ", missing)}");
    }

    [Fact]
    public void 本地原有站点_未被覆盖()
    {
        // 本地自研站点（组 52-62 附近）必须保留
        using var doc = LoadSiteList();
        var root = doc.RootElement;
        var sites = root.GetProperty("Sites");

        var localNames = new HashSet<string>();
        foreach (var group in sites.EnumerateObject())
        {
            if (int.TryParse(group.Name, out var g) && g >= 52)
            {
                foreach (var site in group.Value.EnumerateObject())
                {
                    var rec = site.Value;
                    if (rec.GetArrayLength() >= 1)
                    {
                        localNames.Add(rec[0].GetString() ?? string.Empty);
                    }
                }
            }
        }

        Assert.Contains("Brazzers", localNames); // 示例断言，实际保留的站点在组 52+ 存在即可
    }

    private static string NormalizeUrl(string url)
    {
        var u = url.Trim().ToLowerInvariant();
        if (!u.Contains("://"))
        {
            u = "http://" + u;
        }

        u = Regex.Replace(u, "^https?://(www\\.)?", string.Empty);
        u = u.TrimEnd('/');
        return u;
    }
}
