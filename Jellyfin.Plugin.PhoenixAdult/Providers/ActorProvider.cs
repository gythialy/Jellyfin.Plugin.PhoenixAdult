using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Configuration;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

#if __EMBY__
using MediaBrowser.Common.Net;
#else
using System.Net.Http;
#endif

namespace PhoenixAdult.Providers
{
    public class ActorProvider : IRemoteMetadataProvider<Person, PersonLookupInfo>
    {
        /// <summary>
        /// 演员元数据来源，按优先级排列。Freeones 字段最全，Babepedia 覆盖日系/欧系，
        /// ThePornDB 依赖 MetadataAPI Token（未配置时返回空，不产生请求失败）。
        /// </summary>
        private static readonly string[] ActorProviders = { "Freeones", "Babepedia", "AV-LEAGUE", "ThePornDB" };

        public string Name => Plugin.Instance.Name;

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(PersonLookupInfo searchInfo, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();

            if (searchInfo == null || searchInfo.ProviderIds.Any(o => !string.IsNullOrEmpty(o.Value)))
            {
                return result;
            }

            var searchName = searchInfo.Name;
            if (Plugin.Instance.Configuration.PreferedActorNameSource == PreferedActorNameSource.LocalDatabase)
            {
                searchName = Actors.ResolveWithJapaneseStyle(searchName);
            }

            var collected = new List<RemoteSearchResult>();

            foreach (var siteName in ActorProviders)
            {
                var title = $"{siteName} {searchName}";
                var site = Helper.GetSiteFromTitle(title);
                if (site.siteNum == null)
                {
                    continue;
                }

                string actorName = Helper.GetClearTitle(title, site.siteName);

                Logger.Info($"site: {site.siteNum[0]}:{site.siteNum[1]} ({site.siteName})");
                Logger.Info($"actorName: {actorName}");

                DateTime? searchDateObj = null;
                if (searchInfo.PremiereDate.HasValue)
                {
#if __EMBY__
                    searchDateObj = searchInfo.PremiereDate.Value.DateTime;
#else
                    searchDateObj = searchInfo.PremiereDate.Value;
#endif
                }

                var provider = Helper.GetProviderBySiteID(site.siteNum[0]);
                if (provider == null)
                {
                    continue;
                }

                Logger.Info($"provider: {provider}");

                List<RemoteSearchResult> providerResults;
                try
                {
                    providerResults = await provider.Search(site.siteNum, actorName, searchDateObj, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Logger.Error($"Actor Search error: \"{e}\"");

                    await Analytics.Send(
                        new AnalyticsExeption
                        {
                            Request = title,
                            SiteNum = site.siteNum,
                            SearchTitle = actorName,
                            ProviderName = provider.ToString(),
                            Exception = e,
                        }, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (providerResults == null || !providerResults.Any())
                {
                    continue;
                }

                foreach (var scene in providerResults)
                {
                    scene.ProviderIds[this.Name] = $"{site.siteNum[0]}#{site.siteNum[1]}#" + scene.ProviderIds[this.Name];
                    scene.Name = scene.Name.Trim();
                }

                collected.AddRange(providerResults);
            }

            // 多源合并：同名去重（保留先命中的来源），再按与查询名的相似度排序。
            // OrderBy 是稳定排序，因此同分时仍保持 ActorProviders 的优先级顺序。
            result = collected
                .GroupBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
                .Select(o => o.First())
                .OrderByDescending(o => 100 - LevenshteinDistance.Calculate(searchName, o.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return result;
        }

        public async Task<MetadataResult<Person>> GetMetadata(PersonLookupInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Person>()
            {
                HasMetadata = false,
                Item = new Person(),
            };

            if (info == null)
            {
                return result;
            }

            string[] curID = null;
            var sceneID = info.ProviderIds;
            if (sceneID.TryGetValue(this.Name, out var externalID))
            {
                curID = externalID.Split('#');
            }

            if ((!sceneID.ContainsKey(this.Name) || curID == null || curID.Length < 3) && !Plugin.Instance.Configuration.DisableAutoIdentify)
            {
                var searchResults = await this.GetSearchResults(info, cancellationToken).ConfigureAwait(false);
                if (searchResults.Any())
                {
                    sceneID = searchResults.First().ProviderIds;

                    sceneID.TryGetValue(this.Name, out externalID);
                    curID = externalID.Split('#');
                }
            }

            if (curID == null || curID.Length < 3)
            {
                return result;
            }

            var siteNum = new int[2] { int.Parse(curID[0], CultureInfo.InvariantCulture), int.Parse(curID[1], CultureInfo.InvariantCulture) };

            var provider = Helper.GetProviderBySiteID(siteNum[0]);
            if (provider != null)
            {
                Logger.Info($"PhoenixAdult Actor ID: {externalID}");

                MetadataResult<BaseItem> res = null;
                try
                {
                    res = await provider.Update(siteNum, curID.Skip(2).ToArray(), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Logger.Error($"Actor Update error: \"{e}\"");

                    await Analytics.Send(
                        new AnalyticsExeption
                        {
                            Request = externalID,
                            SearchTitle = info.Name,
                            ProviderName = provider.ToString(),
                            Exception = e,
                        }, cancellationToken).ConfigureAwait(false);
                }

                if (res != null)
                {
                    result.HasMetadata = true;
                    result.Item = (Person)res.Item;
                }

                if (result.HasMetadata)
                {
                    result.Item.ProviderIds.Update(this.Name, sceneID[this.Name]);
                    result.Item.ProviderIds.Update(this.Name + "URL", result.Item.ExternalId);

                    var canonicalName = info.Name;
                    if (Plugin.Instance.Configuration.PreferedActorNameSource == PreferedActorNameSource.LocalDatabase)
                    {
                        canonicalName = Actors.ResolveWithJapaneseStyle(info.Name);
                    }

                    result.Item.OriginalTitle = WebUtility.HtmlDecode(result.Item.OriginalTitle);
                    var aliases = (result.Item.OriginalTitle ?? string.Empty).Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    var newAliases = new List<string>();
                    foreach (var name in aliases)
                    {
                        var actorName = name.Trim();

                        if (!string.IsNullOrEmpty(actorName) && !newAliases.Contains(actorName, StringComparer.Ordinal))
                        {
                            newAliases.Add(actorName);
                        }
                    }

                    var bornPlaceList = new List<string>();
                    if (result.Item.ProductionLocations != null && result.Item.ProductionLocations.Any())
                    {
                        foreach (var bornPlace in result.Item.ProductionLocations[0].Split(","))
                        {
                            var location = bornPlace.Trim();

                            if (!string.IsNullOrEmpty(location))
                            {
                                bornPlaceList.Add(location);
                            }
                        }

                        result.Item.ProductionLocations = new string[] { string.Join(", ", bornPlaceList) };
                    }

                    if (!string.IsNullOrEmpty(canonicalName)
                        && !newAliases.Contains(canonicalName, StringComparer.OrdinalIgnoreCase)
                        && !string.Equals(canonicalName, info.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        newAliases.Add(canonicalName);
                    }

                    if (!newAliases.Contains(info.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        result.HasMetadata = false;
                    }

                    result.Item.OriginalTitle = string.Join(", ", newAliases);

                    if (!string.IsNullOrEmpty(result.Item.Overview))
                    {
                        result.Item.Overview = HttpUtility.HtmlDecode(result.Item.Overview).Trim();
                    }

                    if (result.Item.PremiereDate.HasValue)
                    {
                        result.Item.ProductionYear = result.Item.PremiereDate.Value.Year;
                    }

                    if (Plugin.Instance.Configuration.TagActors)
                    {
                        result.Item.AddTag(Plugin.Instance.Configuration.TagActorValue);
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
    }
}
