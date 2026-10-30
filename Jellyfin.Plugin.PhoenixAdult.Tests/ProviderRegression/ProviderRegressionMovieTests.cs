using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Sdk;

namespace Jellyfin.Plugin.PhoenixAdult.Tests.ProviderRegression;

/// <summary>
/// 数据驱动的全链路回归：逐条执行 ProviderCases.json 中 kind=e2e 的用例，
/// 经 MovieProvider 走真实集成路径 Search→GetMetadata，断言标题/日期/演员。
/// </summary>
[Trait("Category", "Online")]
public class ProviderRegressionMovieTests
{
    /// <summary>xUnit Theory 参数源。</summary>
    public static IEnumerable<object[]> Cases
        => ProviderCaseCatalog.OfKind(ProviderCaseKind.E2e).Select(c => new object[] { c });

    [Theory(DisplayName = "Provider 全链路(MovieProvider) 回归")]
    [MemberData(nameof(Cases))]
    public async Task E2E_Case(ProviderCase c)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        try
        {
            var result = await ProviderRegressionRunner.RunE2eAsync(c, timeout.Token);

            if (result.SearchCount == 0 && c.AllowZero)
            {
                // 站点整站不可达（地域封锁/维护）时允许 0 结果，不判失败
                Console.WriteLine($"⚠️ {c.Id}: 0 结果（allowZero：站点不可达）");
                return;
            }

            Assert.True(result.SearchCount > 0, $"{c.Id}: Search 0 结果（{c.FileName}）");
            Assert.NotNull(result.Meta);
            var meta = result.Meta!;
            Assert.True(meta.HasMetadata, $"{c.Id}: 无元数据（{c.FileName}）");

            var actualDate = meta.Item.PremiereDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
            if (!string.IsNullOrEmpty(c.ExpectDate))
            {
                Assert.Equal(c.ExpectDate, actualDate);
            }

            var title = meta.Item.Name ?? string.Empty;
            foreach (var fragment in c.ExpectTitleContains)
            {
                Assert.Contains(fragment, title, StringComparison.OrdinalIgnoreCase);
            }

            foreach (var actor in c.ExpectActors)
            {
                Assert.True(
                    meta.People.Any(p => p.Name.Contains(actor, StringComparison.OrdinalIgnoreCase)),
                    $"{c.Id}: 缺演员 '{actor}'，实际 [{string.Join(", ", meta.People.Select(p => p.Name))}]");
            }

            // 识别对话框只渲染 Name/ProductionYear：搜索结果的 Name 带日期前缀（到日），
            // 而 Apply 后的条目标题必须仍是站点原始标题（前缀不能泄漏）。
            var firstResult = result.SearchResults.FirstOrDefault();
            if (firstResult?.PremiereDate is not null && !string.IsNullOrEmpty(firstResult.Name))
            {
                var prefix = firstResult.PremiereDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " · ";
                Assert.StartsWith(prefix, firstResult.Name, StringComparison.Ordinal);
                Assert.False(
                    title.StartsWith(prefix, StringComparison.Ordinal),
                    $"{c.Id}: 日期前缀泄漏进条目标题 '{title}'");
            }

            if (c.MinSearchImageRatio > 0 && result.SearchResults.Count > 0)
            {
                var withImage = result.SearchResults.Count(r => !string.IsNullOrEmpty(r.ImageUrl));
                var ratio = (double)withImage / result.SearchResults.Count;
                Assert.True(
                    ratio >= c.MinSearchImageRatio,
                    $"{c.Id}: 搜索结果配图率 {withImage}/{result.SearchResults.Count} ({ratio:P0}) 低于 {c.MinSearchImageRatio:P0}");

                // 识别对话框的缩略图由浏览器直连加载，样例图必须能裸取（无 Referer）才算真的会显示。
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                foreach (var imageUrl in result.SearchResults.Where(r => !string.IsNullOrEmpty(r.ImageUrl)).Select(r => r.ImageUrl!).Take(5))
                {
                    using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, imageUrl), timeout.Token);
                    Assert.True(response.IsSuccessStatusCode, $"{c.Id}: 搜索缩略图不可用 {(int)response.StatusCode} {imageUrl}");
                }

                Log($"🖼 {c.Id} :: 配图 {withImage}/{result.SearchResults.Count}");
            }

            Log($"✅ {c.Id} :: '{title}' {actualDate} 演员 {meta.People.Count}");
        }
        catch (Exception ex) when (ProviderRegressionRunner.IsEnvironmentSkip(ex))
        {
            Log($"ℹ️ {c.Id} :: SKIP 环境依赖（FlareSolverr 未配置）");
        }
        catch (XunitException ex)
        {
            Log($"❌ {c.Id} :: {ProviderRegressionRunner.FirstLine(ex.Message)}");
            throw;
        }
        catch (Exception ex)
        {
            var message = $"{ex.GetType().Name}: {ProviderRegressionRunner.FirstLine(ex.Message)}";
            Log($"❌ {c.Id} :: {message}");
            Assert.Fail($"{c.Id}: {message}");
        }
    }

    private static void Log(string line) => ProviderRegressionReport.Line(line);
}
