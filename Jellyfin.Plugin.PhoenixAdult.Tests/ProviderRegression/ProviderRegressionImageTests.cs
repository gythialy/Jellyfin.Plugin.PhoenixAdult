using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Entities;
using Xunit;
using Xunit.Sdk;

namespace Jellyfin.Plugin.PhoenixAdult.Tests.ProviderRegression;

/// <summary>
/// 图片回归：执行 ProviderCases.json 中 kind=images 的用例。
/// 走 MovieImageProvider.GetImages（Jellyfin 实际调用入口），其内部串联
/// ImageHelper.GetImagesSizeAndValidate：HEAD 可达 → 拉字节 → 解析尺寸（>100px）→ backdrop 1280 宽过滤。
/// 因此“返回 ≥1 张 Primary 且有尺寸”等价于“在 Jellyfin 里能下载到正确图片”。
/// 注意：使用 [Fact] + 内部循环而非 [Theory]，因为 xUnit 对空 MemberData 会直接判失败；
/// 目前图片校验主要由 search 用例的 chain 覆盖，本类为显式 sceneId 场景保留。
/// </summary>
[Trait("Category", "Online")]
public class ProviderRegressionImageTests
{
    [Fact(DisplayName = "Provider 图片可下载 回归")]
    public async Task Images_Cases()
    {
        var cases = ProviderCaseCatalog.OfKind(ProviderCaseKind.Images).ToList();
        if (cases.Count == 0)
        {
            Log("ℹ️ 目录中暂无 kind=images 用例（图片校验由 search+chain 覆盖）");
            return;
        }

        var failures = new List<string>();

        foreach (var c in cases)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));

            try
            {
                if (c.SceneId is null || c.SceneId.Length == 0)
                {
                    throw new InvalidOperationException($"{c.Id}: images 用例缺少 sceneId");
                }

                var images = await ProviderRegressionRunner.GetValidatedImagesAsync(c, c.SceneId, timeout.Token);
                var primaries = images.Where(i => i.Type == ImageType.Primary).ToList();

                Assert.True(primaries.Count > 0, $"{c.Id}: GetImages 未返回可下载的 Primary 图片（通过校验 {images.Count} 张）");
                Assert.True(primaries.Any(i => i.Width.HasValue && i.Height.HasValue), $"{c.Id}: Primary 图片缺少可解析尺寸");

                var top = primaries.First(i => i.Width.HasValue);
                Log($"✅ {c.Id} :: {images.Count} 张可下载（Primary {primaries.Count}）| 首图 {top.Width}x{top.Height} {Shorten(top.Url)}");
            }
            catch (Exception ex) when (ProviderRegressionRunner.IsEnvironmentSkip(ex))
            {
                Log($"ℹ️ {c.Id} :: SKIP 环境依赖（FlareSolverr 未配置）");
            }
            catch (Exception ex)
            {
                var message = $"{c.Id}: {ex.GetType().Name}: {ProviderRegressionRunner.FirstLine(ex.Message)}";
                Log($"❌ {message}");
                failures.Add(message);
            }
        }

        Assert.True(failures.Count == 0, $"图片回归失败 {failures.Count} 条:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    private static string Shorten(string? url)
        => string.IsNullOrEmpty(url) ? string.Empty : (url.Length > 90 ? url[..90] + "…" : url);

    private static void Log(string line) => ProviderRegressionReport.Line(line);
}
