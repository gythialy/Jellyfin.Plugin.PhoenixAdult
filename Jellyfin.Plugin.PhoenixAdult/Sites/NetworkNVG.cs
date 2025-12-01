using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Newtonsoft.Json.Linq;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class NetworkNVG : IProviderBase
    {
        private async Task<JToken> GetPageData(int[] siteNum, int sceneId, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string> { { "Referer", Helper.GetSearchBaseURL(siteNum) } };
            var httpResult = await HTTP.Request("https://netvideogirls.com/page-data/home/page-data.json", HttpMethod.Get, null, null, headers, cancellationToken);
            if (httpResult.IsOK)
            {
                var data = JObject.Parse(httpResult.Content);
                foreach (var scene in data["result"]["data"]["allMysqlTourStats"]["edges"])
                {
                    if ((int)scene["node"]["tour_thumbs"]["updates"]["mysqlId"] == sceneId)
                    {
                        return scene["node"]["tour_thumbs"];
                    }
                }
            }

            return null;
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            int? sceneId = null;
            if (int.TryParse(searchTitle.Split(' ').FirstOrDefault() ?? string.Empty, out var id))
            {
                sceneId = id;
                searchTitle = searchTitle.Replace(id.ToString(), string.Empty).Trim();
            }

            var googleResults = await WebSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            var searchResults = googleResults.Where(u => !u.Contains("/tag/") && !u.Contains("/category/"));

            if (!searchResults.Any() && sceneId.HasValue)
            {
                var pageData = await GetPageData(siteNum, sceneId.Value, cancellationToken);
                if (pageData != null)
                {
                    string titleNoFormatting = pageData["updates"]["short_title"].ToString();
                    string curId = pageData["updates"]["mysqlId"].ToString();
                    string releaseDate = string.Empty;
                    if (DateTime.TryParse(pageData["updates"]["release_date"].ToString(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{releaseDate}|{Helper.Encode(searchTitle)}" } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }
            }
            else
            {
                foreach (var sceneUrl in searchResults)
                {
                    var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                    if (httpResult.IsOK)
                    {
                        var detailsPageElements = HTML.ElementFromString(httpResult.Content);
                        var videoIdMatch = Regex.Match(detailsPageElements.SelectSingleNode("//source")?.GetAttributeValue("src", string.Empty) ?? string.Empty, @"(?=\d).*(?=-)");
                        if (videoIdMatch.Success)
                        {
                            var pageData = await GetPageData(siteNum, int.Parse(videoIdMatch.Value), cancellationToken);
                            string titleNoFormatting = pageData?.SelectToken("updates.short_title")?.ToString() ?? detailsPageElements.SelectSingleNode("//h2")?.InnerText;
                            string curId = pageData?.SelectToken("updates.mysqlId")?.ToString() ?? videoIdMatch.Value;
                            string releaseDate = string.Empty;
                            if (pageData != null && DateTime.TryParse(pageData["updates"]["release_date"].ToString(), out var parsedDate))
                            {
                                releaseDate = parsedDate.ToString("yyyy-MM-dd");
                            }
                            else if (DateTime.TryParse(detailsPageElements.SelectSingleNode("//meta")?.GetAttributeValue("content", string.Empty), out parsedDate))
                            {
                                releaseDate = parsedDate.ToString("yyyy-MM-dd");
                            }

                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, $"{curId}|{releaseDate}|{Helper.Encode(searchTitle)}|{Helper.Encode(sceneUrl)}" } },
                                Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                                SearchProviderName = Plugin.Instance.Name,
                            });
                        }
                    }
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
            int sceneId = int.Parse(providerIds[0]);
            string sceneDate = providerIds[1];
            string actors = Helper.Decode(providerIds[2]);
            string netUrl = providerIds.Length > 3 ? Helper.Decode(providerIds[3]) : null;

            var detailsPageElements = await GetPageData(siteNum, sceneId, cancellationToken);

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements["updates"]["short_title"].ToString();

            if (netUrl != null)
            {
                var httpResult = await HTTP.Request(netUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    movie.Overview = (HTML.ElementFromString(httpResult.Content)).SelectSingleNode("//div[@class='the-content']/p")?.InnerText.Trim();
                }
            }

            movie.AddStudio("NVG Network");
            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddStudio(tagline);

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            foreach (var actor in actors.Split(new[] { " and " }, StringSplitOptions.None))
            {
                ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actor.Trim(), Type = PersonKind.Actor });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            int sceneId = int.Parse(sceneID[0].Split('|')[0]);
            var detailsPageElements = await GetPageData(siteNum, sceneId, cancellationToken);
            string imageUrl = $"{Helper.GetSearchBaseURL(siteNum)}{detailsPageElements["localFile"]["childImageSharp"]["fluid"]["src"]}";
            images.Add(new RemoteImageInfo { Url = imageUrl, Type = ImageType.Primary });
            return images;
        }
    }
}
