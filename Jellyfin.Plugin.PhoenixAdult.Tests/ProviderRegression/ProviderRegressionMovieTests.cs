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
