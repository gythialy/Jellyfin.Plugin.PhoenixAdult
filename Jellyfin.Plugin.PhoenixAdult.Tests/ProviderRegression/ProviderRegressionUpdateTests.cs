using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Sdk;

namespace Jellyfin.Plugin.PhoenixAdult.Tests.ProviderRegression;

/// <summary>
/// 数据驱动的 provider Update 回归：逐条执行 ProviderCases.json 中 kind=update 的用例，
/// 以场景 URL 直接抓取，验证站点改版后 URL 直抓链路不退化。
/// </summary>
[Trait("Category", "Online")]
public class ProviderRegressionUpdateTests
{
    /// <summary>xUnit Theory 参数源。</summary>
    public static IEnumerable<object[]> Cases
        => ProviderCaseCatalog.OfKind(ProviderCaseKind.Update).Select(c => new object[] { c });

    [Theory(DisplayName = "Provider Update(URL 直抓) 回归")]
    [MemberData(nameof(Cases))]
    public async Task Update_Case(ProviderCase c)
    {
        var provider = ProviderRegressionRunner.ResolveProvider(c);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        try
        {
            var meta = await ProviderRegressionRunner.UpdateByUrlAsync(provider, c, timeout.Token);

            if (c.ExpectName)
            {
                Assert.False(string.IsNullOrEmpty(meta.Item.Name), $"{c.Id}: Update Name 为空 ({c.SceneUrl})");
            }

            if (c.ExpectMinActors > 0)
            {
                Assert.True(meta.People.Count >= c.ExpectMinActors, $"{c.Id}: 演员 {meta.People.Count} < {c.ExpectMinActors}");
            }

            Log($"✅ {c.Id} :: Name='{meta.Item.Name}' 日期={meta.Item.PremiereDate:yyyy-MM-dd} 演员 {meta.People.Count}");
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
