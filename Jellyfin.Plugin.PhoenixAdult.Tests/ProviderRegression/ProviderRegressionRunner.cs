using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;
using PhoenixAdult.Providers;
using PhoenixAdult.Sites;

namespace Jellyfin.Plugin.PhoenixAdult.Tests.ProviderRegression;

/// <summary>Search 执行结果（含实际命中的搜索词与回退序列）。</summary>
public sealed record SearchOutcome(List<RemoteSearchResult> Results, string UsedTerm, IReadOnlyList<string> Terms);

/// <summary>MovieProvider 全链路执行结果。</summary>
public sealed record E2eOutcome(int SearchCount, IReadOnlyList<RemoteSearchResult> SearchResults, MovieInfo Info, MetadataResult<Movie>? Meta);

/// <summary>
/// Provider 统一回归执行器：解析 handler、按多级搜索词回退执行 Search、
/// 以场景 URL 或 sceneID 执行 Update，并统一识别环境依赖（FlareSolverr 未配置）。
/// </summary>
internal static class ProviderRegressionRunner
{
    /// <summary>单个搜索词的超时（秒）；多级回退时每个词独立计时。</summary>
    internal const int PerTermTimeoutSeconds = 60;

    /// <summary>解析用例对应的 handler 实现；失败即抛错（用例本身有问题）。</summary>
    internal static IProviderBase ResolveProvider(ProviderCase c)
    {
        TestInitializer.Init();
        return Helper.GetBaseSiteByName(c.Handler)
            ?? throw new InvalidOperationException($"handler 无实现类: {c.Handler} ({c.Id})");
    }

    /// <summary>
    /// 按 <see cref="ProviderCase.SearchTerms"/> 顺序搜索，命中即停；全部为 0 时返回最后一次结果。
    /// </summary>
    internal static async Task<SearchOutcome> SearchAsync(IProviderBase provider, ProviderCase c, CancellationToken cancellationToken = default)
    {
        var terms = c.SearchTerms.Length > 0 ? c.SearchTerms : new[] { c.SiteName };
        var results = new List<RemoteSearchResult>();
        var used = terms[0];

        foreach (var term in terms)
        {
            used = term;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(PerTermTimeoutSeconds));
            results = await provider.Search(c.SiteNum, term, null, cts.Token).ConfigureAwait(false);
            if (results.Count > 0)
            {
                break;
            }
        }

        return new SearchOutcome(results, used, terms);
    }

    /// <summary>
    /// 复跑识别对话框的补图路径（MovieProvider.FillMissingThumbnails），返回补图后的结果。
    /// 用于验证"列表页没有海报"的站点在对话框里仍然有图。
    /// </summary>
    internal static async Task<List<RemoteSearchResult>> FillDialogImagesAsync(int[] siteNum, List<RemoteSearchResult> results, CancellationToken cancellationToken)
    {
        TestInitializer.Init();

        // 补图依赖 ProviderIds 的 "group#site#<handler 自己的 sceneID>" 格式；
        // 直接调 provider.Search 时没有这层前缀（MovieProvider.GetSearchResults 才加），这里补上。
        // 注意：必须操作副本——用例后面的 chain（Update/GetImages）依赖原始的 sceneID 分段。
        var copies = results.Select(r => new RemoteSearchResult
        {
            Name = r.Name,
            ImageUrl = r.ImageUrl,
            PremiereDate = r.PremiereDate,
            ProductionYear = r.ProductionYear,
            IndexNumber = r.IndexNumber,
            IndexNumberEnd = r.IndexNumberEnd,
            ParentIndexNumber = r.ParentIndexNumber,
            SearchProviderName = r.SearchProviderName,
            Overview = r.Overview,
            ProviderIds = new Dictionary<string, string>(r.ProviderIds),
        }).ToList();

        var providerName = global::PhoenixAdult.Plugin.Instance.Name;
        foreach (var result in copies)
        {
            if (result.ProviderIds.TryGetValue(providerName, out var externalId) && !string.IsNullOrEmpty(externalId)
                && !externalId.StartsWith($"{siteNum[0]}#{siteNum[1]}#", StringComparison.Ordinal))
            {
                result.ProviderIds[providerName] = $"{siteNum[0]}#{siteNum[1]}#{externalId}";
            }
        }

        await MovieProvider.FillMissingThumbnails(siteNum, copies, cancellationToken).ConfigureAwait(false);
        return copies;
    }

    /// <summary>用 Search 结果里的 sceneID 执行 Update（全链路用例）。</summary>
    internal static Task<MetadataResult<BaseItem>> UpdateWithSceneIdAsync(IProviderBase provider, ProviderCase c, string[] sceneId, CancellationToken cancellationToken = default)
        => provider.Update(c.SiteNum, sceneId, cancellationToken);

    /// <summary>用场景 URL（Helper.Encode 后）直接执行 Update。</summary>
    internal static Task<MetadataResult<BaseItem>> UpdateByUrlAsync(IProviderBase provider, ProviderCase c, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(c.SceneUrl))
        {
            throw new InvalidOperationException($"{c.Id}: update 用例缺少 sceneUrl");
        }

        return provider.Update(c.SiteNum, new[] { Helper.Encode(c.SceneUrl) }, cancellationToken);
    }

    /// <summary>执行 GetImages 并返回图片数。</summary>
    internal static async Task<int> CountImagesAsync(IProviderBase provider, ProviderCase c, string[] sceneId, BaseItem item, CancellationToken cancellationToken = default)
        => (await provider.GetImages(c.SiteNum, sceneId, item, cancellationToken).ConfigureAwait(false)).Count();

    /// <summary>
    /// 经 MovieImageProvider 取图并做“真实可下载”校验：
    /// 流程与 Jellyfin 自动下载一致（HEAD 可达 → 拉字节 → 解析尺寸 → backdrop 宽过滤）。
    /// 返回通过校验的图片列表；Primary 数量与尺寸可用于断言。
    /// </summary>
    internal static async Task<List<RemoteImageInfo>> GetValidatedImagesAsync(ProviderCase c, string[] sceneId, CancellationToken cancellationToken = default)
    {
        var providerIds = new[] { c.Group.ToString(CultureInfo.InvariantCulture), c.Site.ToString(CultureInfo.InvariantCulture) }
            .Concat(sceneId);

        var movie = new Movie();
        movie.ProviderIds[global::PhoenixAdult.Plugin.Instance.Name] = string.Join("#", providerIds);
        movie.Name = c.SiteName;

        var images = await new MovieImageProvider().GetImages(movie, cancellationToken).ConfigureAwait(false);

        // 复跑插件官方校验，确保结论与 Jellyfin 自动下载完全一致
        return await ImageHelper.GetImagesSizeAndValidate(images, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 从 Search 结果提取 provider 约定的 sceneID。
    /// 约定：ProviderIds 值以 '#' 分隔（与历史探针测试一致）。
    /// </summary>
    internal static string[] SceneIdFrom(RemoteSearchResult result)
        => result.ProviderIds.Values.First().Split('#');

    /// <summary>经 MovieProvider 执行 Search→GetMetadata 全链路。</summary>
    internal static async Task<E2eOutcome> RunE2eAsync(ProviderCase c, CancellationToken cancellationToken = default)
    {
        TestInitializer.Init();
        if (string.IsNullOrEmpty(c.FileName))
        {
            throw new InvalidOperationException($"{c.Id}: e2e 用例缺少 fileName");
        }

        var provider = new MovieProvider();
        var info = new MovieInfo { Name = c.FileName };
        var search = (await provider.GetSearchResults(info, cancellationToken).ConfigureAwait(false)).ToList();
        if (search.Count == 0)
        {
            return new E2eOutcome(0, search, info, null);
        }

        var first = search[0];
        info.ProviderIds = first.ProviderIds;
        info.PremiereDate = first.PremiereDate;
        var meta = await provider.GetMetadata(info, cancellationToken).ConfigureAwait(false);
        return new E2eOutcome(search.Count, search, info, meta);
    }

    /// <summary>
    /// 识别“环境依赖”类失败（FlareSolverr 未配置 / 8191 端口拒绝），此类失败不计入回归失败。
    /// </summary>
    internal static bool IsEnvironmentSkip(Exception exception)
    {
        for (Exception? e = exception; e != null; e = e.InnerException)
        {
            var message = e.Message;
            if (string.IsNullOrEmpty(message))
            {
                continue;
            }

            if (message.Contains("FlareSolverr", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (message.Contains("8191", StringComparison.OrdinalIgnoreCase)
                && (message.Contains("refused", StringComparison.OrdinalIgnoreCase) || message.Contains("localhost", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>取异常消息首行并按上限截断，便于断言信息阅读。</summary>
    internal static string FirstLine(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var line = text.Split('\n')[0].Trim();
        return line.Length > 160 ? line[..160] : line;
    }
}
