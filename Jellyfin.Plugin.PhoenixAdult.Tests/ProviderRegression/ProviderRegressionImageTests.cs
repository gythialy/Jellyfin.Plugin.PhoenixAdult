using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Entities;
using Xunit;
using Xunit.Sdk;

namespace Jellyfin.Plugin.PhoenixAdult.Tests.ProviderRegression;

/// <summary>
/// Image cases (kind=images) run through MovieImageProvider.GetImages, which chains
/// ImageHelper.GetImagesSizeAndValidate: HEAD, bytes, size, backdrop width filter.
/// A Fact with a loop is used because xUnit fails on empty MemberData;
/// the search chain covers most of the image checks.
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
