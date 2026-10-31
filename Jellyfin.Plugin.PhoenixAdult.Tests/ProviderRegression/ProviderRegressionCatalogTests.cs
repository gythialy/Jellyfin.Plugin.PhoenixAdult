using System.Globalization;
using System.Text.Json;
using Xunit;

namespace Jellyfin.Plugin.PhoenixAdult.Tests.ProviderRegression;

/// <summary>
/// Offline checks on the case catalog so it always matches
/// SiteList.json and the handlers.
/// </summary>
public class ProviderRegressionCatalogTests
{
    private static readonly ProviderCatalog Catalog = ProviderCaseCatalog.Current;

    private static readonly Lazy<JsonDocument> SiteListDoc = new(() =>
    {
        var path = Path.Combine(TestPaths.DataDir, "SiteList.json");
        Assert.True(File.Exists(path), $"SiteList.json 不存在: {path}");
        return JsonDocument.Parse(File.ReadAllText(path));
    });

    [Fact]
    public void Catalog_HasCases()
    {
        Assert.True(Catalog.Cases.Count > 0, "ProviderCases.json 用例为空");
    }

    [Fact]
    public void Catalog_CaseIdsAreUnique()
    {
        var duplicates = Catalog.Cases
            .GroupBy(c => c.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(duplicates.Count == 0, $"重复用例 ID: {string.Join(", ", duplicates)}");
    }

    [Fact]
    public void Catalog_RequiredFieldsPresent()
    {
        var problems = new List<string>();

        foreach (var c in Catalog.Cases)
        {
            if (string.IsNullOrWhiteSpace(c.Id))
            {
                problems.Add("存在空 ID 的用例");
            }

            if (string.IsNullOrWhiteSpace(c.SiteName))
            {
                problems.Add($"{c.Id}: siteName 为空");
            }

            if (c.Group < 0 || c.Site < 0)
            {
                problems.Add($"{c.Id}: group/site 为负");
            }

            if (c.ExpectMinResults < 0)
            {
                problems.Add($"{c.Id}: expectMinResults 为负");
            }

            switch (c.Kind)
            {
                case ProviderCaseKind.Search:
                    if (string.IsNullOrWhiteSpace(c.Handler))
                    {
                        problems.Add($"{c.Id}: handler 为空");
                    }

                    if (c.SearchTerms.Length == 0)
                    {
                        problems.Add($"{c.Id}: searchTerms 为空");
                    }

                    if (c.ExpectMinResults == 0 && !c.AllowZero)
                    {
                        problems.Add($"{c.Id}: expectMinResults=0 必须同时 allowZero=true");
                    }

                    break;
                case ProviderCaseKind.Update:
                    if (string.IsNullOrWhiteSpace(c.Handler))
                    {
                        problems.Add($"{c.Id}: handler 为空");
                    }

                    if (string.IsNullOrWhiteSpace(c.SceneUrl))
                    {
                        problems.Add($"{c.Id}: sceneUrl 为空");
                    }

                    break;
                case ProviderCaseKind.Images:
                    if (string.IsNullOrWhiteSpace(c.Handler))
                    {
                        problems.Add($"{c.Id}: handler 为空");
                    }

                    if (c.SceneId is null || c.SceneId.Length == 0)
                    {
                        problems.Add($"{c.Id}: sceneId 为空");
                    }

                    break;
                case ProviderCaseKind.E2e:
                    if (string.IsNullOrWhiteSpace(c.FileName))
                    {
                        problems.Add($"{c.Id}: fileName 为空");
                    }

                    if (string.IsNullOrWhiteSpace(c.ExpectDate))
                    {
                        problems.Add($"{c.Id}: expectDate 为空");
                    }

                    break;
                default:
                    problems.Add($"{c.Id}: 未知 kind {c.Kind}");
                    break;
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Catalog_HandlersHaveImplementations()
    {
        var assembly = typeof(global::PhoenixAdult.Sites.IProviderBase).Assembly;
        var missing = Catalog.Cases
            .Where(c => c.Kind != ProviderCaseKind.E2e)
            .Where(c => assembly.GetType($"PhoenixAdult.Sites.{c.Handler}", false, true) == null)
            .Select(c => $"{c.Id} -> {c.Handler}")
            .Distinct()
            .ToList();

        Assert.True(missing.Count == 0, $"以下 handler 无实现类:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
    }

    [Fact]
    public void Catalog_SitesExistInSiteList()
    {
        var sites = SiteListDoc.Value.RootElement.GetProperty("Sites");
        var problems = new List<string>();

        foreach (var c in Catalog.Cases.Where(c => c.Kind != ProviderCaseKind.E2e))
        {
            if (!sites.TryGetProperty(c.Group.ToString(CultureInfo.InvariantCulture), out var group))
            {
                problems.Add($"{c.Id}: 组 {c.Group} 在 SiteList.Sites 中不存在");
                continue;
            }

            if (!group.TryGetProperty(c.Site.ToString(CultureInfo.InvariantCulture), out _))
            {
                problems.Add($"{c.Id}: 组 {c.Group} 中无站号 {c.Site}");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Catalog_GroupAndHandlerMatchSiteIdList()
    {
        var siteIdList = SiteListDoc.Value.RootElement.GetProperty("SiteIDList");
        var problems = new List<string>();

        foreach (var c in Catalog.Cases.Where(c => c.Kind != ProviderCaseKind.E2e))
        {
            var groupKey = c.Group.ToString(CultureInfo.InvariantCulture);
            if (!siteIdList.TryGetProperty(groupKey, out var handler))
            {
                problems.Add($"{c.Id}: SiteIDList 缺组 {groupKey}");
                continue;
            }

            if (!string.Equals(handler.GetString(), c.Handler, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{c.Id}: SiteIDList[{groupKey}]={handler.GetString()} != handler {c.Handler}");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
