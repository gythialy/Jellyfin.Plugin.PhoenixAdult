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
        /// <summary>Max dialog thumbnails (one GetImages per result).</summary>
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
        /// Search body. <paramref name="fillThumbnails"/> enables dialog thumbnails; automatic matching skips them.
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

            // Code lookup: query every source of the code family in parallel and merge, unless a site is pinned.
            var multiSearchConfig = Plugin.Instance.Configuration;
            var hasJavId = Helper.TryExtractJavId(searchInfo.Name, out var detectedId);
            if (multiSearchConfig.MultiSourceSearch && hasJavId && (!explicitSite || IsIdLabelMatchingSite(detectedId, site.siteName)))
            {
                // The detected site name may be the brand inside the code; keep it as a candidate but query the others too.
                var sources = SearchRouter.GetSources(detectedId).ToList();
                if (explicitSite && site.siteNum != null
                    && !sources.Any(o => o.Group == site.siteNum[0] && o.Site == site.siteNum[1]))
                {
                    sources.Insert(0, (site.siteNum[0], site.siteNum[1]));
                }

                if (sources.Count > 0)
                {
                    var timeoutSeconds = Math.Clamp(multiSearchConfig.MultiSourceSearchTimeoutSeconds, 5, 120);
                    result = await this.SearchMultipleSourcesAsync(sources, searchTitle, searchDateObj, searchInfo.Name, detectedId, fillThumbnails, timeoutSeconds, cancellationToken).ConfigureAwait(false);
                    if (fillThumbnails)
                    {
                        AddSourcePrefix(result);
                    }

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

                if (fillThumbnails)
                {
                    AddSourcePrefix(result);
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
        /// The dialog renders Name and ProductionYear only, so the date is prepended to the display name.
        /// </summary>
        /// <summary>
        /// Prefix each dialog result with its source site; the stored title is unaffected.
        /// </summary>
        /// <param name="results">Search results.</param>
        internal static void AddSourcePrefix(IList<RemoteSearchResult> results)
        {
            if (results == null)
            {
                return;
            }

            foreach (var scene in results)
            {
                if (scene == null || string.IsNullOrWhiteSpace(scene.Name) || scene.ProviderIds == null)
                {
                    continue;
                }

                if (!scene.ProviderIds.TryGetValue(Plugin.Instance.Name, out var externalId) || string.IsNullOrEmpty(externalId))
                {
                    continue;
                }

                var parts = externalId.Split('#');
                if (parts.Length < 2
                    || !int.TryParse(parts[0], CultureInfo.InvariantCulture, out var group)
                    || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out var site))
                {
                    continue;
                }

                var siteName = Helper.GetSearchSiteName(new[] { group, site });
                if (string.IsNullOrWhiteSpace(siteName))
                {
                    continue;
                }

                var label = $"[{siteName}] ";
                if (!scene.Name.Contains(label, StringComparison.OrdinalIgnoreCase))
                {
                    scene.Name = label + scene.Name;
                }
            }
        }

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
        /// Fetch a Primary image for dialog results that have none.
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

                // ProviderIds look like group#site#<scene id parts>.
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

                // Complete protocol relative URLs.
                var thumbSource = image.Url.StartsWith("//", StringComparison.Ordinal) ? "https:" + image.Url : image.Url;

                // The dialog loads images from the browser, so referer protected URLs must use the same-origin proxy.
                if (ImageProxyUrl.NeedsProxy(thumbSource) && !ImageProxyUrl.IsAvailable)
                {
                    return;
                }

                // Probe once and leave the card text-only when the image is gone.
                if (!await HTTP.IsUrlLoadable(ImageProxyUrl.ForDownload(thumbSource), cts.Token).ConfigureAwait(false))
                {
                    return;
                }

                result.ImageUrl = ImageProxyUrl.ForBrowser(thumbSource);
            }
            catch (Exception e)
            {
                Logger.Debug($"Search thumbnail fill skipped: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>
        /// Whether a detected site name is actually the brand inside the code.
        /// </summary>
        private static bool IsIdLabelMatchingSite(string detectedId, string siteName)
        {
            if (string.IsNullOrEmpty(detectedId) || string.IsNullOrEmpty(siteName))
            {
                return false;
            }

            var label = JavId.GetLabel(detectedId);
            return !string.IsNullOrEmpty(label) && string.Equals(label, siteName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Query several sites for one code in parallel; a slow site does not affect the others.
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
