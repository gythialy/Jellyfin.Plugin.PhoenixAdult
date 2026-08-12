using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
#if !__EMBY__
using Jellyfin.Data.Enums;
#endif
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Newtonsoft.Json.Linq;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    public class NetworkModelCentro : IProviderBase
    {
        private const string Query = "content.load?_method=content.load&tz=1&limit=512&transitParameters[v1]=OhUOlmasXD&transitParameters[v2]=OhUOlmasXD&transitParameters[preset]=videos";

        private const string UpdateQuery = "content.load?_method=content.load&tz=1&filter[id][fields][0]=id&filter[id][values][0]={0}&limit=1&transitParameters[v1]=ykYa8ALmUD&transitParameters[preset]=scene";

        private const string ModelQuery = "model.getModelContent?_method=model.getModelContent&tz=1&limit=25&transitParameters[contentId]=";

        private static readonly Dictionary<int, string> ActorDb = new Dictionary<int, string>
        {
            { 1024, "Aletta Ocean" }, { 1025, "Eva Lovia" }, { 1026, "Romi Rain" }, { 1030, "Dani Daniels" },
            { 1031, "Chloe Toy" }, { 1033, "Katya Clover" }, { 1035, "Lisey Sweet" }, { 1037, "Gina Gerson" },
            { 1038, "Valentina Nappi" }, { 1039, "Vina Sky" }, { 1058, "Vicki Valkyrie" }, { 1075, "Dillion Harper" },
            { 1191, "Lilu Moon" },
        };

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            int? sceneId = null;
            if (int.TryParse(searchTitle.Split(' ').FirstOrDefault() ?? string.Empty, out var id))
            {
                sceneId = id;
            }

            string apiUrl = await this.GetApiUrl(siteNum, "/videos/", cancellationToken);
            if (apiUrl == null)
            {
                // /videos/ 页面改版拿不到 ah/aet token（2026-08-12）→ 分页浏览兜底（TPDB 同款）
                return await this.SearchByPagination(siteNum, searchTitle, cancellationToken);
            }

            var searchResults = await this.GetJsonFromApi($"{Helper.GetSearchSearchURL(siteNum)}{Uri.EscapeDataString(apiUrl)}{Query}", cancellationToken);
            if (searchResults != null)
            {
                // Logger.Info($"[NetworkModelCentro] results: {searchResults.ToString()}");
                foreach (var searchResult in searchResults)
                {
                    string titleNoFormatting = Helper.ParseTitle(searchResult["title"].ToString(), siteNum);
                    int curId = (int)searchResult["id"];
                    string releaseDate = string.Empty;
                    if (DateTime.TryParse(searchResult["sites"]["collection"][curId.ToString()]["publishDate"].ToString(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    string artObj = Helper.Encode(searchResult["_resources"]["base"].ToString());
                    var primaryImage = searchResult["_resources"]["primary"] as JArray;
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{Helper.Encode(titleNoFormatting)}|{artObj}" } },
                        Name = $"{titleNoFormatting} {releaseDate} [{Helper.GetSearchSiteName(siteNum)}]",
                        SearchProviderName = Plugin.Instance.Name,
                        ImageUrl = primaryImage?.Any() == true ? primaryImage[0]["url"].ToString() : string.Empty,
                    });
                }
            }

            return result;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem>()
            {
                Item = new Movie(),
                People = new List<PersonInfo>(),
            };

            string[] providerIds = sceneID[0].Split('|');
            string sceneId = providerIds[0];
            string title = Helper.Decode(providerIds[1]).Trim();

            string apiUrl = await this.GetApiUrl(siteNum, $"/scene/{sceneId}/{Uri.EscapeDataString(title)}", cancellationToken);
            if (apiUrl == null)
            {
                return result;
            }

            var detailsPageElements = (await this.GetJsonFromApi($"{Helper.GetSearchSearchURL(siteNum)}{apiUrl}{string.Format(UpdateQuery, sceneId)}", cancellationToken))?.FirstOrDefault();
            if (detailsPageElements == null)
            {
                return result;
            }

            Logger.Info($"[NetworkModelCentro] details: {detailsPageElements.ToString()}");
            var movie = (Movie)result.Item;
            movie.Name = Helper.ParseTitle(detailsPageElements["title"].ToString().Capitalize(), siteNum);
            movie.Overview = detailsPageElements["description"].ToString();
            movie.AddStudio(Helper.GetSearchSiteName(siteNum));

            if (DateTime.TryParse(detailsPageElements["sites"]["collection"][sceneId]["publishDate"].ToString(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var tags = detailsPageElements.SelectToken("tags.collection");
            if (tags is JObject genres)
            {
                foreach (var genre in genres)
                {
                    var genreName = genre.Value["alias"].ToString();
                    if (!string.IsNullOrEmpty(genreName))
                    {
                        movie.AddGenre(genreName);
                    }
                }
            }

            var actors = await this.GetJsonFromApi($"{Helper.GetSearchSearchURL(siteNum)}{apiUrl}{ModelQuery}{sceneId}", cancellationToken);
            var actorCollectionToken = actors?.SelectToken("modelId.collection");
            if (actorCollectionToken is JObject actorCollection)
            {
                foreach (var actor in actorCollection)
                {
                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actor.Value["stageName"].ToString(), Type = PersonKind.Actor });
                }
            }

            if (ActorDb.ContainsKey(siteNum[0]))
            {
                ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = ActorDb[siteNum[0]], Type = PersonKind.Actor });
            }

            return result;
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var artObj = JArray.Parse(Helper.Decode(sceneID[0].Split('|')[2]));
            foreach (var img in artObj)
            {
                images.Add(new RemoteImageInfo { Url = img["url"].ToString() });
            }

            if (images.Any())
            {
                images.First().Type = ImageType.Primary;
            }

            return Task.FromResult<IEnumerable<RemoteImageInfo>>(images);
        }

        private async Task<string> GetApiUrl(int[] siteNum, string path, CancellationToken cancellationToken)
        {
            string url = $"{Helper.GetSearchBaseURL(siteNum)}{path}";
            var httpResult = await HTTP.Request(url, HttpMethod.Get, cancellationToken);
            if (httpResult.IsOK)
            {
                var ahMatch = Regex.Match(httpResult.Content, "\"ah\".?:.?\"([0-9a-zA-Z\\(\\)\\[\\]\\@\\:\\,\\/\\!\\+\\-\\.\\$_\\[\\]=\\\\'']*)\"");
                var aetMatch = Regex.Match(httpResult.Content, "\"aet\".?:([0-9]*)");
                if (ahMatch.Success && aetMatch.Success)
                {
                    string ah = new string(ahMatch.Groups[1].Value.Reverse().ToArray());
                    string aet = aetMatch.Groups[1].Value;
                    return $"{ah}/{aet}/";
                }
            }

            return null;
        }

        private async Task<JToken> GetJsonFromApi(string url, CancellationToken cancellationToken)
        {
            var httpResult = await HTTP.Request(url, HttpMethod.Get, cancellationToken);
            if (httpResult.IsOK)
            {
                return JObject.Parse(httpResult.Content)["response"]["collection"];
            }

            return null;
        }

        /// <summary>
        /// SAPI token 拿不到时的分页浏览兜底：翻页收集 /trailers/ 链接 + 标题分词过滤。
        /// </summary>
        private async Task<List<RemoteSearchResult>> SearchByPagination(int[] siteNum, string searchTitle, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var titleTokens = searchTitle.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            for (var page = 1; page <= 5; page++)
            {
                var pageUrl = $"{Helper.GetSearchBaseURL(siteNum)}/porn-categories/Movies/?page={page}";
                var pageResult = await HTTP.Request(pageUrl, HttpMethod.Get, cancellationToken).ConfigureAwait(false);
                if (!pageResult.IsOK)
                {
                    break;
                }

                var pageDoc = HTML.ElementFromString(pageResult.Content);
                var cards = pageDoc.SelectNodesSafe("//a[contains(@href, '/trailers/')]");
                if (!cards.Any())
                {
                    break;
                }

                foreach (var card in cards)
                {
                    var href = card.GetAttributeValue("href", string.Empty);
                    if (string.IsNullOrEmpty(href))
                    {
                        continue;
                    }

                    var cardTitle = card.InnerText.Trim();
                    var haystack = string.IsNullOrEmpty(cardTitle) ? href : cardTitle;
                    if (!titleTokens.All(t => haystack.ToLowerInvariant().Contains(t, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var sceneUrl = href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? href : Helper.GetSearchBaseURL(siteNum) + href;
                    var curId = Helper.Encode(sceneUrl);
                    if (result.Any(r => r.ProviderIds.First().Value == curId))
                    {
                        continue;
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curId } },
                        Name = $"{cardTitle} [{Helper.GetSearchSiteName(siteNum)}]",
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }

                if (result.Count >= 10)
                {
                    break;
                }
            }

            return result;
        }
    }
}
