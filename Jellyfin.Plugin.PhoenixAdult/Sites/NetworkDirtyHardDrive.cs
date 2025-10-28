using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
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
    public class NetworkDirtyHardDrive : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var googleResults = await WebSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            var searchResults = googleResults.Where(u => u.Contains("/tour1/") && u.EndsWith(".html"));

            foreach (var sceneUrl in searchResults)
            {
                var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var detailsPageElements = HTML.ElementFromString(httpResult.Content);
                    string titleNoFormatting = detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim();
                    string curId = Helper.Encode(sceneUrl);
                    string releaseDate = searchDate?.ToString("yyyy-MM-dd") ?? string.Empty;

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{releaseDate}" } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}]",
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

            string[] providerIds = sceneID[0].Split('|');
            string sceneUrl = Helper.Decode(providerIds[0]);
            string sceneDate = providerIds.Length > 1 ? providerIds[1] : null;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//div[@id='video-page-desc']")?.InnerText.Trim();
            movie.AddStudio("Dirty Hard Drive");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var actorNode = detailsPageElements.SelectNodes("//div[@id='video-specs']//span")?.LastOrDefault();
            if (actorNode != null)
            {
                string actorName = actorNode.InnerText.Trim();
                string actorPhotoUrl = string.Empty;
                var actorPageUrlNode = actorNode.SelectSingleNode("./a");
                if (string.IsNullOrEmpty(actorName) && actorPageUrlNode != null)
                {
                    string actorPageUrl = actorPageUrlNode.GetAttributeValue("href", string.Empty);
                    actorName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(actorPageUrl.Split('/').Last().Replace(".html", string.Empty).Replace("pornstar_", string.Empty).Replace("_", " "));
                    if (!actorPageUrl.StartsWith("http"))
                    {
                        actorPageUrl = Helper.GetSearchBaseURL(siteNum) + actorPageUrl;
                    }

                    var actorHttp = await HTTP.Request(actorPageUrl, HttpMethod.Get, cancellationToken);
                    if (actorHttp.IsOK)
                    {
                        var actorPage = HTML.ElementFromString(actorHttp.Content);
                        actorPhotoUrl = actorPage.SelectSingleNode("//div[@id='global-model-img']//img")?.GetAttributeValue("src", string.Empty);
                    }
                }

                result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var match = Regex.Match(httpResult.Content, "'playlistfile': '(.+playlist\\.xml)'");
            if (match.Success)
            {
                string playListUrl = match.Groups[1].Value;
                var xmlHttp = await HTTP.Request(playListUrl, HttpMethod.Get, cancellationToken);
                if (xmlHttp.IsOK)
                {
                    var xmlDoc = new XmlDocument();
                    xmlDoc.LoadXml(xmlHttp.Content);
                    var posterNode = xmlDoc.SelectSingleNode("//channel/item/media:group/media:thumbnail", new XmlNamespaceManager(xmlDoc.NameTable));
                    if (posterNode != null)
                    {
                        images.Add(new RemoteImageInfo { Url = posterNode.Attributes["url"].Value, Type = ImageType.Primary });
                    }
                }
            }
            else
            {
                match = Regex.Match(httpResult.Content, "'image': '(.+bookend\\.jpg)'");
                if (match.Success)
                {
                    images.Add(new RemoteImageInfo { Url = match.Groups[1].Value, Type = ImageType.Primary });
                }
            }

            return images;
        }
    }
}
