using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// 离线数据完整性测试：data/IdRoutes.json 的番号 → 站点 路由表。
/// 路由表是数据文件，代码里不再写死站点清单；这里保证它的结构、正则与站点引用都有效。
/// </summary>
public class IdRoutesDataTests
{
    private static readonly string DataDir = TestPaths.DataDir;

    private static readonly string RoutesPath = Path.Combine(DataDir, "IdRoutes.json");

    private static readonly string SiteListPath = Path.Combine(DataDir, "SiteList.json");

    private static JsonDocument LoadRoutes()
    {
        Assert.True(File.Exists(RoutesPath), $"IdRoutes.json 不存在: {RoutesPath}");
        using var stream = File.OpenRead(RoutesPath);
        return JsonDocument.Parse(stream);
    }

    [Fact]
    public void IdRoutes_Json结构完整()
    {
        using var doc = LoadRoutes();
        Assert.True(doc.RootElement.TryGetProperty("Routes", out var routes), "缺少 Routes 节点");
        Assert.True(routes.GetArrayLength() > 0, "Routes 为空");

        foreach (var route in routes.EnumerateArray())
        {
            Assert.True(route.TryGetProperty("Id", out var id) && !string.IsNullOrWhiteSpace(id.GetString()), "路由缺少 Id");
            Assert.True(route.TryGetProperty("Sites", out var sites), $"路由 {id} 缺少 Sites");
            Assert.True(sites.GetArrayLength() > 0, $"路由 {id} 的 Sites 为空");
        }
    }

    [Fact]
    public void IdRoutes_路由Id唯一()
    {
        using var doc = LoadRoutes();
        var ids = doc.RootElement.GetProperty("Routes").EnumerateArray()
            .Select(o => o.GetProperty("Id").GetString())
            .ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void IdRoutes_站点引用都能在SiteList中找到()
    {
        using var routesDoc = LoadRoutes();
        using var siteDoc = JsonDocument.Parse(File.ReadAllText(SiteListPath));
        var sites = siteDoc.RootElement.GetProperty("Sites");

        foreach (var route in routesDoc.RootElement.GetProperty("Routes").EnumerateArray())
        {
            var routeId = route.GetProperty("Id").GetString();
            foreach (var site in route.GetProperty("Sites").EnumerateArray())
            {
                Assert.Equal(2, site.GetArrayLength());
                var group = site[0].GetInt32().ToString();
                var siteIndex = site[1].GetInt32().ToString();

                Assert.True(sites.TryGetProperty(group, out var groupSites), $"路由 {routeId} 引用了不存在的组 {group}");
                Assert.True(groupSites.TryGetProperty(siteIndex, out _), $"路由 {routeId} 引用了不存在的站点 {group}:{siteIndex}");
            }
        }
    }

    [Fact]
    public void IdRoutes_LabelRegex都能编译()
    {
        using var doc = LoadRoutes();
        foreach (var route in doc.RootElement.GetProperty("Routes").EnumerateArray())
        {
            var routeId = route.GetProperty("Id").GetString();
            var pattern = route.TryGetProperty("LabelRegex", out var rx) ? rx.GetString() : null;
            if (string.IsNullOrEmpty(pattern))
            {
                continue;
            }

            var ex = Record.Exception(() => new Regex(pattern, RegexOptions.IgnoreCase));
            Assert.True(ex == null, $"路由 {routeId} 的正则无效: {pattern} ({ex?.Message})");
        }
    }

    [Fact]
    public void IdRoutes_最后一条是兜底路由()
    {
        using var doc = LoadRoutes();
        var routes = doc.RootElement.GetProperty("Routes").EnumerateArray().ToList();
        var last = routes[^1];

        var prefix = last.TryGetProperty("IdPrefix", out var p) ? p.GetString() : null;
        var pattern = last.TryGetProperty("LabelRegex", out var r) ? r.GetString() : null;

        Assert.True(string.IsNullOrEmpty(prefix) && string.IsNullOrEmpty(pattern), "最后一条路由必须是兜底（Prefix 与 LabelRegex 都为空）");
    }
}
