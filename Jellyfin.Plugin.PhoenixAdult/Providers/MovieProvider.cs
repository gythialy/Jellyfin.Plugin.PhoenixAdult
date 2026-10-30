using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;
using PhoenixAdult.Sites;

#if __EMBY__
using MediaBrowser.Common.Net;
#else
using System.IO;
using System.Net.Http;
#endif

namespace PhoenixAdult.Providers
{
    public class MovieProvider : IRemoteMetadataProvider<Movie, MovieInfo>
    {
        /// <summary>识别对话框补缩略图的上限（每条结果一次 GetImages，避免搜索结果过多时打爆站点）。</summary>
        internal const int MaxThumbnailFillCount = 10;

        private static readonly TimeSpan ThumbnailFillTimeout = TimeSpan.FromSeconds(8);

        public MovieProvider()
        {
            Database.LoadAll();
        }

        public string Name => Plugin.Instance.Name;

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
            => this.GetSearchResults(searchInfo, true, cancellationToken);

        /// <summary>
        /// 搜索结果主体。<paramref name="fillThumbnails"/> 为 true 时（识别对话框发起的搜索）会给缺图结果补缩略图；
        /// 自动识别（GetMetadata 内部调用）不补，避免扫描时为每个文件额外打一串请求。
        /// </summary>
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, bool fillThumbnails, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();

            if (searchInfo == null || string.IsNullOrEmpty(searchInfo.Name))
            {
                return result;
            }

            Logger.Info($"searchInfo.Name: {searchInfo.Name}");

            var title = string.Empty;
            (int[] siteNum, string siteName) site = (null, null);
            var explicitSite = false;

#if __EMBY__
#else
            if (!string.IsNullOrEmpty(searchInfo.Path) && Plugin.Instance.Configuration.UseFilePath)
            {
                Logger.Info($"searchInfo.Path: {searchInfo.Path}");
                var path = Path.Combine(Path.GetDirectoryName(searchInfo.Path), Path.GetFileNameWithoutExtension(searchInfo.Path));
                foreach (var name in path.Split(Path.DirectorySeparatorChar).Reverse())
                {
                    title = $"{name} {title}";
                    site = Helper.GetSiteFromTitle(name);
                    if (site.siteNum != null)
                    {
                        explicitSite = true;
                        break;
                    }
                }
            }
#endif

            if (site.siteNum == null)
            {
                title = Helper.ReplaceAbbrieviation(searchInfo.Name);
                site = Helper.GetSiteFromTitle(title);
                explicitSite = site.siteNum != null;
            }

            if (site.siteNum == null)
            {
                string newTitle;
                if (!string.IsNullOrEmpty(Plugin.Instance.Configuration.DefaultSiteName))
                {
                    newTitle = $"{Plugin.Instance.Configuration.DefaultSiteName} {searchInfo.Name}";
                }
                else
                {
                    newTitle = Helper.GetSiteNameFromTitle(searchInfo.Name);
                }

                if (!string.IsNullOrEmpty(newTitle) && !newTitle.Equals(searchInfo.Name, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Info($"newTitle: {newTitle}");

                    title = Helper.ReplaceAbbrieviation(newTitle);
                    site = Helper.GetSiteFromTitle(title);
                }

                if (site.siteNum == null)
                {
                    return result;
                }
            }

            string searchTitle = Helper.GetClearTitle(title, site.siteName),
                   searchDate = string.Empty;
            DateTime? searchDateObj;
            var titleAfterDate = Helper.GetDateFromTitle(searchTitle);

            searchTitle = titleAfterDate.searchTitle;
            searchDateObj = titleAfterDate.searchDateObj;
            if (searchDateObj.HasValue)
            {
                searchDate = searchDateObj.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            else
            {
                if (searchInfo.PremiereDate.HasValue)
                {
#if __EMBY__
                    searchDateObj = searchInfo.PremiereDate.Value.DateTime;
#else
                    searchDateObj = searchInfo.PremiereDate.Value;
#endif
                    searchDate = searchInfo.PremiereDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
            }

            if (string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            // 番号检索的多源并发：用户没有显式指定站点时，按番号类型并发查多个源并合并，
            // 避免"一个番号只绑一个站点"（如 MDSR-0005 同时存在于 MadouQu / ModelMediaAsia 等）。
            var multiSearchConfig = Plugin.Instance.Configuration;
            if (!explicitSite && multiSearchConfig.MultiSourceSearch && Helper.TryExtractJavId(searchInfo.Name, out var detectedId))
            {
                var sources = SearchRouter.GetSources(detectedId);
                if (sources.Count > 0)
                {
                    var timeoutSeconds = Math.Clamp(multiSearchConfig.MultiSourceSearchTimeoutSeconds, 5, 120);
                    result = await this.SearchMultipleSourcesAsync(sources, searchTitle, searchDateObj, searchInfo.Name, detectedId, fillThumbnails, timeoutSeconds, cancellationToken).ConfigureAwait(false);
                    AddDatePrefix(result);
                    return result;
                }
            }

            Logger.Info($"site: {site.siteNum[0]}:{site.siteNum[1]} ({site.siteName})");
            Logger.Info($"searchTitle: {searchTitle}");
            Logger.Info($"searchDate: {searchDate}");

            var provider = Helper.GetProviderBySiteID(site.siteNum[0]);
            if (provider != null)
            {
                Logger.Info($"provider: {provider}");

                try
                {
                    result = await provider.Search(site.siteNum, searchTitle, searchDateObj, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Logger.Error($"Search error: \"{e}\"");

                    await Analytics.Send(
                        new AnalyticsExeption
                        {
                            Request = searchInfo.Name,
                            SiteNum = site.siteNum,
                            SearchTitle = searchTitle,
                            SearchDate = searchDateObj,
                            ProviderName = provider.ToString(),
                            Exception = e,
                        }, cancellationToken).ConfigureAwait(false);
                }

                if (result.Any())
                {
                    foreach (var scene in result)
                    {
                        scene.ProviderIds[this.Name] = $"{site.siteNum[0]}#{site.siteNum[1]}#" + scene.ProviderIds[this.Name];
                        scene.Name = scene.Name.Trim();
                        if (scene.PremiereDate.HasValue)
                        {
                            scene.ProductionYear = scene.PremiereDate.Value.Year;
                        }
                    }

                    if (result.Any(scene => scene.IndexNumber.HasValue))
                    {
                        result = result.OrderByDescending(o => o.IndexNumber.HasValue).ThenByDescending(o => o.IndexNumber).ToList();
                    }
                    else if (!string.IsNullOrEmpty(searchDate) && result.All(o => o.PremiereDate.HasValue) && result.Any(o => o.PremiereDate.Value != searchDateObj))
                    {
                        result = result.OrderBy(o => Math.Abs((searchDateObj - o.PremiereDate).Value.TotalDays)).ToList();
                    }
                    else
                    {
                        result = result.OrderByDescending(o => 100 - LevenshteinDistance.Calculate(searchTitle, Helper.GetClearTitle(o.Name), StringComparison.OrdinalIgnoreCase)).ToList();
                    }
                }

                if (fillThumbnails && result.Any())
                {
                    await FillMissingThumbnails(site.siteNum, result, cancellationToken).ConfigureAwait(false);
                }

                AddDatePrefix(result);
            }
            else
            {
                Logger.Error($"Failed to find provider for site {site.siteNum[0]}:{site.siteNum[1]}");
            }

            return result;
        }

        public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Movie>
            {
                HasMetadata = false,
                Item = new Movie(),
            };

            if (info == null)
            {
                return result;
            }

            DateTime? premiereDateObj = null;
            if (info.PremiereDate.HasValue)
            {
#if __EMBY__
                premiereDateObj = info.PremiereDate.Value.DateTime;
#else
                premiereDateObj = info.PremiereDate.Value;
#endif
            }

            string[] curID = null;
            var sceneID = info.ProviderIds;
            if (sceneID.TryGetValue(this.Name, out var externalID))
            {
                curID = externalID.Split('#');
            }

            if ((!sceneID.ContainsKey(this.Name) || curID == null || curID.Length < 3) && !Plugin.Instance.Configuration.DisableAutoIdentify)
            {
                var searchResults = await this.GetSearchResults(info, false, cancellationToken).ConfigureAwait(false);
                if (searchResults.Any())
                {
                    var first = searchResults.First();

                    sceneID = first.ProviderIds;

                    sceneID.TryGetValue(this.Name, out externalID);
                    curID = externalID.Split('#');

                    if (first.PremiereDate.HasValue)
                    {
#if __EMBY__
                        premiereDateObj = first.PremiereDate.Value.DateTime;
#else
                        premiereDateObj = first.PremiereDate.Value;
#endif
                    }
                }
            }

            if (curID == null)
            {
                return result;
            }

            var siteNum = new int[2] { int.Parse(curID[0], CultureInfo.InvariantCulture), int.Parse(curID[1], CultureInfo.InvariantCulture) };

            var provider = Helper.GetProviderBySiteID(siteNum[0]);
            if (provider != null)
            {
                Logger.Info($"PhoenixAdult ID: {externalID}");

                MetadataResult<BaseItem> res = null;
                try
                {
                    res = await provider.Update(siteNum, curID.Skip(2).ToArray(), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Logger.Error($"Update error: \"{e}\"");

                    await Analytics.Send(
                        new AnalyticsExeption
                        {
                            Request = string.Join("#", curID.Skip(2)),
                            SiteNum = siteNum,
                            SearchTitle = info.Name,
                            SearchDate = premiereDateObj,
                            ProviderName = provider.ToString(),
                            Exception = e,
                        }, cancellationToken).ConfigureAwait(false);
                }

                if (res != null)
                {
                    result.HasMetadata = true;
                    result.Item = (Movie)res.Item;
                    result.People = res.People;
                }

                if (result.HasMetadata)
                {
                    result.Item.OfficialRating = "XXX";

                    // Add null check before accessing sceneID
                    if (sceneID != null && sceneID.ContainsKey(this.Name))
                    {
                        result.Item.ProviderIds.Update(this.Name, sceneID[this.Name]);
                    }

                    if (result.Item.Name != null)
                    {
                        result.Item.Name = HttpUtility.HtmlDecode(result.Item.Name).Trim();
                    }

                    if (!string.IsNullOrEmpty(result.Item.Overview))
                    {
                        result.Item.Overview = HttpUtility.HtmlDecode(result.Item.Overview).Trim();
                    }

                    result.Item.AddStudio(Helper.GetSearchSiteName(siteNum));
                    var newStudios = new List<string>();
                    foreach (var studio in result.Item.Studios)
                    {
                        var studioName = studio.Trim();

                        if (studioName.All(char.IsLower))
                        {
                            studioName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(studioName);
                        }

                        if (!newStudios.Contains(studioName, StringComparer.OrdinalIgnoreCase))
                        {
                            newStudios.Add(studioName);
                        }
                    }

                    result.Item.Studios = newStudios.ToArray();

                    if (!result.Item.PremiereDate.HasValue)
                    {
                        result.Item.PremiereDate = premiereDateObj;
                    }

                    if (result.Item.PremiereDate.HasValue)
                    {
                        result.Item.ProductionYear = result.Item.PremiereDate.Value.Year;
                    }

                    if (result.People != null && result.People.Any())
                    {
                        result.People = Actors.Cleanup(result);
                    }

                    if (result.Item.Genres != null && result.Item.Genres.Any())
                    {
                        result.Item.Genres = Genres.Cleanup(result.Item.Genres, result.Item.Name, result.People.ToList());
                    }

                    if (!string.IsNullOrEmpty(result.Item.ExternalId))
                    {
                        result.Item.ProviderIds.Update(this.Name + "URL", result.Item.ExternalId);
                    }
                }
            }

            return result;
        }

#if __EMBY__
        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
#else
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
#endif
        {
            return Helper.GetImageResponse(url, cancellationToken);
        }

        /// <summary>
        /// Jellyfin/Emby 的识别对话框只渲染 Name + ProductionYear（客户端不渲染 PremiereDate），
        /// 为把日期显示到日，把日期拼进展示名（"2026-10-20 · 标题"）。
        /// 只在排序/补图之后调用：不影响匹配度排序，也不把日期带进补图用的搜索词。
        /// Apply 时条目标题仍由 Update 返回的站点标题决定，前缀只影响对话框显示。
        /// </summary>
        internal static void AddDatePrefix(IList<RemoteSearchResult> results)
        {
            if (results == null)
            {
                return;
            }

            foreach (var scene in results)
            {
                if (scene == null || !scene.PremiereDate.HasValue || string.IsNullOrWhiteSpace(scene.Name))
                {
                    continue;
                }

                var datePrefix = scene.PremiereDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " · ";
                if (!scene.Name.StartsWith(datePrefix, StringComparison.Ordinal))
                {
                    scene.Name = datePrefix + scene.Name;
                }
            }
        }

        /// <summary>
        /// 识别对话框只渲染 <see cref="RemoteSearchResult.ImageUrl"/>，但很多站点的 Search 结果里没有海报
        /// （图片只在 GetImages 里）。这里对缺图结果并发取一张 Primary 图兜底，让对话框不再出现无图卡片。
        /// 仅限用户发起的搜索（fillThumbnails=true）；自动识别走 GetMetadata 内部调用，不会触发。
        /// 回归测试也复用它（ProviderRegressionRunner.FillDialogImagesAsync）。
        /// </summary>
        internal static async Task FillMissingThumbnails(int[] siteNum, List<RemoteSearchResult> results, CancellationToken cancellationToken)
        {
            var provider = Helper.GetProviderBySiteID(siteNum[0]);
            if (provider == null)
            {
                return;
            }

            var targets = results.Where(o => string.IsNullOrEmpty(o.ImageUrl)).Take(MaxThumbnailFillCount).ToList();
            if (targets.Count == 0)
            {
                return;
            }

            var item = new Movie();
            await Task.WhenAll(targets.Select(o => FillThumbnail(provider, siteNum, o, item, cancellationToken))).ConfigureAwait(false);
        }

        private static async Task FillThumbnail(IProviderBase provider, int[] siteNum, RemoteSearchResult result, BaseItem item, CancellationToken cancellationToken)
        {
            try
            {
                if (!result.ProviderIds.TryGetValue(Plugin.Instance.Name, out var externalId) || string.IsNullOrEmpty(externalId))
                {
                    return;
                }

                // ProviderIds 形如 "group#site#<handler 自己的 sceneID 段>"
                var sceneId = externalId.Split('#').Skip(2).ToArray();
                if (sceneId.Length == 0)
                {
                    return;
                }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(ThumbnailFillTimeout);

                var images = await provider.GetImages(siteNum, sceneId, item, cts.Token).ConfigureAwait(false);
                var image = images?.FirstOrDefault(o => o.Type == ImageType.Primary) ?? images?.FirstOrDefault();
                if (image == null || string.IsNullOrEmpty(image.Url))
                {
                    return;
                }

                // 识别对话框里的图片由浏览器直连加载（不带 Referer），需要 Referer 的 CDN（如 phncdn）
                // 填进去只会显示破图，宁可不填，让客户端回退到标题卡片。
                if (Helper.GetImageReferer(image.Url) != null)
                {
                    return;
                }

                // 兜底图常常是各站的电影封面，部分标题的封面并不存在（实测 404）。
                // 这里用 HEAD 探一次，取不到就保持无图，避免对话框里出现破图。
                if (!await HTTP.IsUrlLoadable(image.Url, cts.Token).ConfigureAwait(false))
                {
                    return;
                }

                // 少数站点给的是协议相对地址（//host/path），补全成 https
                result.ImageUrl = image.Url.StartsWith("//", StringComparison.Ordinal) ? "https:" + image.Url : image.Url;
            }
            catch (Exception e)
            {
                Logger.Debug($"Search thumbnail fill skipped: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>
        /// 按番号并发检索多个站点并合并结果。
        /// 单站失败/超时不影晌其它站点；结果按"命中番号 → IndexNumber → 相似度"排序。
        /// </summary>
        private async Task<List<RemoteSearchResult>> SearchMultipleSourcesAsync(
            IReadOnlyList<(int Group, int Site)> sources,
            string searchTitle,
            DateTime? searchDate,
            string originalName,
            string detectedId,
            bool fillThumbnails,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            var perSource = await Task.WhenAll(sources.Select(async source =>
            {
                var siteNum = new[] { source.Group, source.Site };
                var provider = Helper.GetProviderBySiteID(source.Group);
                if (provider == null)
                {
                    return (SiteNum: siteNum, Results: new List<RemoteSearchResult>());
                }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                try
                {
                    Logger.Info($"multi-search {source.Group}:{source.Site} -> {detectedId}");
                    var results = await provider.Search(siteNum, detectedId, searchDate, cts.Token).ConfigureAwait(false)
                        ?? new List<RemoteSearchResult>();

                    foreach (var scene in results)
                    {
                        if (scene.ProviderIds.TryGetValue(this.Name, out var id) && !string.IsNullOrEmpty(id))
                        {
                            scene.ProviderIds[this.Name] = $"{source.Group}#{source.Site}#{id}";
                        }

                        scene.Name = (scene.Name ?? string.Empty).Trim();
                        if (scene.PremiereDate.HasValue)
                        {
                            scene.ProductionYear = scene.PremiereDate.Value.Year;
                        }

                        if (string.IsNullOrEmpty(scene.SearchProviderName))
                        {
                            scene.SearchProviderName = Helper.GetSearchSiteName(siteNum);
                        }
                    }

                    return (SiteNum: siteNum, Results: results);
                }
                catch (Exception e)
                {
                    Logger.Error($"multi-search failed {source.Group}:{source.Site}: {e.Message}");
                    await Analytics.Send(
                        new AnalyticsExeption
                        {
                            Request = originalName,
                            SiteNum = siteNum,
                            SearchTitle = detectedId,
                            SearchDate = searchDate,
                            ProviderName = provider.ToString(),
                            Exception = e,
                        }, cancellationToken).ConfigureAwait(false);

                    return (SiteNum: siteNum, Results: new List<RemoteSearchResult>());
                }
            })).ConfigureAwait(false);

            var merged = perSource
                .SelectMany(o => o.Results)
                .GroupBy(
                    o => $"{o.ProviderIds.GetValueOrDefault(this.Name)}|{o.Name}",
                    StringComparer.OrdinalIgnoreCase)
                .Select(o => o.First())
                .ToList();

            if (merged.Count == 0)
            {
                return merged;
            }

            merged = merged
                .OrderByDescending(o => o.Name != null && o.Name.Contains(detectedId, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(o => o.IndexNumber ?? 0)
                .ThenByDescending(o => 100 - LevenshteinDistance.Calculate(searchTitle, Helper.GetClearTitle(o.Name ?? string.Empty), StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (fillThumbnails)
            {
                foreach (var group in merged.GroupBy(o =>
                {
                    var split = (o.ProviderIds.GetValueOrDefault(this.Name) ?? string.Empty).Split('#');
                    return split.Length >= 2 ? $"{split[0]}#{split[1]}" : string.Empty;
                }))
                {
                    var parts = group.Key.Split('#');
                    if (parts.Length != 2
                        || !int.TryParse(parts[0], CultureInfo.InvariantCulture, out var grp)
                        || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out var siteId))
                    {
                        continue;
                    }

                    try
                    {
                        await FillMissingThumbnails(
                            new[] { grp, siteId },
                            group.Where(o => string.IsNullOrEmpty(o.ImageUrl)).ToList(),
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        Logger.Error($"multi-search thumbnail fill failed {group.Key}: {e.Message}");
                    }
                }
            }

            return merged;
        }
    }
}
