using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PhoenixAdult.Providers;
using Xunit;
using Xunit.Sdk;

namespace Jellyfin.Plugin.PhoenixAdult.Tests.ProviderRegression;

/// <summary>
/// 数据驱动的 provider Search 回归：逐条执行 ProviderCases.json 中 kind=search 的用例，
/// 支持多级搜索词回退；标记 chain 的用例在命中后继续 Update/GetImages 全链路。
/// </summary>
[Trait("Category", "Online")]
public class ProviderRegressionSearchTests
{
    /// <summary>xUnit Theory 参数源。</summary>
    public static IEnumerable<object[]> Cases
        => ProviderCaseCatalog.OfKind(ProviderCaseKind.Search).Select(c => new object[] { c });

    [Theory(DisplayName = "Provider Search 回归")]
    [MemberData(nameof(Cases))]
    public async Task Search_Case(ProviderCase c)
    {
        var provider = ProviderRegressionRunner.ResolveProvider(c);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        try
        {
            var outcome = await ProviderRegressionRunner.SearchAsync(provider, c, timeout.Token);
            var terms = string.Join(" → ", outcome.Terms);

            if (outcome.Results.Count < c.ExpectMinResults)
            {
                if (c.AllowZero)
                {
                    Log($"⚠️ {c.Id} :: {outcome.Results.Count} 结果（allowZero，不失败）| {terms}");
                    return;
                }

                Assert.Fail($"{c.Id}: 期望 ≥{c.ExpectMinResults} 结果，实际 {outcome.Results.Count} | 搜索词 {terms}");
            }

            // Handler 级 Search 只统计本实现能直接给出的图（部分站点的图只有在 GetImages 里，
            // 由 MovieProvider 的补图兜底覆盖），这里作为站点图片覆盖率的审计输出。
            var withImage = outcome.Results.Count(r => !string.IsNullOrEmpty(r.ImageUrl));

            if (c.MinDialogImageRatio > 0)
            {
                // 复跑识别对话框的补图路径：列表页没有海报的站点靠 GetImages 兜底
                var filled = await ProviderRegressionRunner.FillDialogImagesAsync(c.SiteNum, outcome.Results, timeout.Token).ConfigureAwait(false);
                var targets = filled.Take(MovieProvider.MaxThumbnailFillCount).ToList();
                var withDialogImage = targets.Count(r => !string.IsNullOrEmpty(r.ImageUrl));
                var dialogRatio = targets.Count == 0 ? 0 : (double)withDialogImage / targets.Count;
                Assert.True(
                    dialogRatio >= c.MinDialogImageRatio,
                    $"{c.Id}: 对话框补图率 {withDialogImage}/{targets.Count} ({dialogRatio:P0}) 低于 {c.MinDialogImageRatio:P0}");

                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                foreach (var imageUrl in targets.Where(r => !string.IsNullOrEmpty(r.ImageUrl)).Select(r => r.ImageUrl!).Take(3))
                {
                    Assert.True(Uri.TryCreate(imageUrl, UriKind.Absolute, out var imageUri), $"{c.Id}: 补图 URL 不是绝对地址 {imageUrl}");
                    try
                    {
                        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, imageUri), timeout.Token);
                        Assert.True(response.IsSuccessStatusCode, $"{c.Id}: 补图 URL 不可用 {(int)response.StatusCode} {imageUrl}");
                    }
                    catch (HttpRequestException ex)
                    {
                        Assert.Fail($"{c.Id}: 补图 URL 请求失败 {imageUrl} ({ex.Message})");
                    }
                }

                withImage = filled.Count(r => !string.IsNullOrEmpty(r.ImageUrl));
                Log($"🖼 {c.Id} :: 对话框补图 {withDialogImage}/{targets.Count}");
            }

            var first = outcome.Results[0].Name ?? string.Empty;
            if (!string.IsNullOrEmpty(c.ExpectedFirstContains)
                && !first.Contains(c.ExpectedFirstContains, StringComparison.OrdinalIgnoreCase))
            {
                Assert.Fail($"{c.Id}: 首结果 '{first}' 不含 '{c.ExpectedFirstContains}'");
            }

            if (!string.IsNullOrEmpty(c.ExpectedFirstHint)
                && !first.Contains(c.ExpectedFirstHint, StringComparison.OrdinalIgnoreCase))
            {
                Log($"ℹ️ {c.Id} :: 首结果 '{first}' 与历史观察 '{c.ExpectedFirstHint}' 不同（不断言）");
            }

            var images = 0;
            if (c.Chain)
            {
                var sceneId = ProviderRegressionRunner.SceneIdFrom(outcome.Results[0]);
                var meta = await ProviderRegressionRunner.UpdateWithSceneIdAsync(provider, c, sceneId, timeout.Token);

                if (c.ExpectName)
                {
                    Assert.False(string.IsNullOrEmpty(meta.Item.Name), $"{c.Id}: Update Name 为空");
                }

                if (c.ExpectPremiereDate)
                {
                    Assert.True(meta.Item.PremiereDate.HasValue, $"{c.Id}: Update 缺首播日期");
                }

                if (c.ExpectMinActors > 0)
                {
                    Assert.True(meta.People.Count >= c.ExpectMinActors, $"{c.Id}: 演员 {meta.People.Count} < {c.ExpectMinActors}");
                }

                if (c.ExpectImages)
                {
                    // 真实下载校验：走 MovieImageProvider + ImageHelper 官方校验（与 Jellyfin 自动下载一致）
                    var validated = await ProviderRegressionRunner.GetValidatedImagesAsync(c, sceneId, timeout.Token);
                    var primaries = validated.Where(i => i.Type == MediaBrowser.Model.Entities.ImageType.Primary).ToList();
                    Assert.True(primaries.Count > 0, $"{c.Id}: GetImages 未返回可下载的 Primary 图片（通过校验 {validated.Count} 张）");
                    Assert.True(primaries.Any(i => i.Width.HasValue && i.Height.HasValue), $"{c.Id}: Primary 图片缺少可解析尺寸");
                    images = primaries.Count;
                }
            }

            Log($"✅ {c.Id} :: {outcome.Results.Count} 结果 | 首 '{first}' | 图片 {withImage}/{outcome.Results.Count}" + (c.Chain ? $" | GetImages {images}" : string.Empty));
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
