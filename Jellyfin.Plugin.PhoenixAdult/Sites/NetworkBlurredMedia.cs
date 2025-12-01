using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class NetworkBlurredMedia : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string searchUrl = Helper.GetSearchSearchURL(siteNum) + searchTitle.Replace(" ", "+");
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var searchPageElements = HTML.ElementFromString(httpResult.Content);
            var searchNodes = searchPageElements.SelectNodes("//article[contains(@class, 'video grid-element')]");
            if (searchNodes != null)
            {
                foreach (var node in searchNodes)
                {
                    string titleNoFormatting = node.SelectSingleNode(".//h3[contains(@class, 'video__title')]")?.InnerText.Trim();
                    string curId = Helper.Encode(node.SelectSingleNode(".//a")?.GetAttributeValue("href", string.Empty).Trim());
                    var dateNode = node.SelectSingleNode(".//p[@class='video__stats']");
                    string releaseDate = string.Empty;
                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Split('|')[0].Trim(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curId } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name,
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

            string sceneUrl = Helper.Decode(sceneID[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var jsonNode = detailsPageElements.SelectSingleNode("//script[@id='__NEXT_DATA__']");
            if (jsonNode == null)
            {
                return result;
            }

            var json = Newtonsoft.Json.Linq.JObject.Parse(jsonNode.InnerText);
            var videoData = json["props"]?["pageProps"]?["dehydratedState"]?["queries"]?[0]?["state"]?["data"]?["video"];
            if (videoData == null)
            {
                return result;
            }

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneUrl;
            movie.Name = videoData["title"]?.ToString();
            movie.Overview = videoData["description"]?.ToString();
            movie.AddStudio(Helper.GetSearchSiteName(siteNum));
            movie.AddCollection(Helper.GetSearchSiteName(siteNum));

            if (DateTime.TryParse(videoData["release_date"]?.ToString(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genres = videoData["tags"]?.Select(g => g["name"]?.ToString()).ToList() ?? new List<string>();
            foreach (var genre in genres)
            {
                movie.AddGenre(genre);
            }

            var actors = videoData["models"]?.Select(a => a["name"]?.ToString()).ToList() ?? new List<string>();
            foreach (var actor in actors)
            {
                ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actor, Type = PersonKind.Actor });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var jsonNode = detailsPageElements.SelectSingleNode("//script[@id='__NEXT_DATA__']");
            if (jsonNode == null)
            {
                return images;
            }

            var json = Newtonsoft.Json.Linq.JObject.Parse(jsonNode.InnerText);
            var videoData = json["props"]?["pageProps"]?["dehydratedState"]?["queries"]?[0]?["state"]?["data"]?["video"];
            if (videoData == null)
            {
                return images;
            }

            var posterUrl = videoData["mainPhoto"]?.ToString();
            if (!string.IsNullOrEmpty(posterUrl))
            {
                images.Add(new RemoteImageInfo { Url = posterUrl, Type = ImageType.Primary });
            }

            var backgroundUrls = videoData["previewSprites"]?.Select(s => s["sprite"]?.ToString()).ToList() ?? new List<string>();
            foreach (var backgroundUrl in backgroundUrls)
            {
                if (!string.IsNullOrEmpty(backgroundUrl))
                {
                    images.Add(new RemoteImageInfo { Url = backgroundUrl, Type = ImageType.Backdrop });
                }
            }

            return images;
        }
    }
}
