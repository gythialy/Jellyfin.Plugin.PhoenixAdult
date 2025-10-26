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
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class SiteColette : IProviderBase
    {
        private static readonly Dictionary<string, string> galleryFix = new Dictionary<string, string>
        {
            { "The_Perfect_Threesome", "The_Perfect_Threesome_or_Pussy_Galore" },
        };

        private readonly Dictionary<string, string> _cookies = new Dictionary<string, string> { { "_warning", "True" } };

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string directUrl = $"{Helper.GetSearchSearchURL(siteNum)}{searchTitle.Replace(' ', '_')}";
            var searchResults = new List<string> { directUrl };

            var googleResults = await Search.GetSearchResults(searchTitle, siteNum, cancellationToken);
            searchResults.AddRange(googleResults.Where(u => u.Contains("/videos/")));

            foreach (var sceneUrl in searchResults.Distinct())
            {
                var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken, null, _cookies);
                if (httpResult.IsOK && !httpResult.ResponseUrl.AbsoluteUri.Contains("/join/"))
                {
                    var detailsPageElements = HTML.ElementFromString(httpResult.Content);
                    string titleNoFormatting = detailsPageElements.SelectSingleNode("//div[@class='row info']/div/h1")?.InnerText.Trim();
                    string subSite = Helper.GetSearchSiteName(siteNum);
                    string curId = Helper.Encode(sceneUrl);
                    string releaseDate = string.Empty;
                    var dateNode = detailsPageElements.SelectSingleNode("//h2");
                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }
                    else if (searchDate.HasValue)
                    {
                        releaseDate = searchDate.Value.ToString("yyyy-MM-dd");
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}" } },
                        Name = $"{titleNoFormatting} [{subSite}]",
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
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            string sceneDate = providerIds.Length > 2 ? providerIds[2] : null;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken, null, _cookies);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//div[@class='row info']/div/h1")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectNodes("//div[contains(@class, 'info')]/p").LastOrDefault()?.InnerText.Trim();
            movie.AddStudio("Colette");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }
            else
            {
                var dateNode = detailsPageElements.SelectSingleNode("//h2");
                if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out parsedDate))
                {
                    movie.PremiereDate = parsedDate;
                    movie.ProductionYear = parsedDate.Year;
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//div[contains(@class, 'info')]/h2/a");
            if (actorNodes != null)
            {
                if (actorNodes.Count == 3)
                {
                    movie.AddGenre("Threesome");
                }

                if (actorNodes.Count == 4)
                {
                    movie.AddGenre("Foursome");
                }

                if (actorNodes.Count > 4)
                {
                    movie.AddGenre("Orgy");
                }

                foreach (var actor in actorNodes)
                {
                    string actorName = actor.InnerText.Trim();
                    string actorPageUrl = actor.GetAttributeValue("href", string.Empty);
                    string actorPhotoUrl = string.Empty;
                    var actorHttp = await HTTP.Request(actorPageUrl, HttpMethod.Get, cancellationToken, null, _cookies);
                    if (actorHttp.IsOK)
                    {
                        var actorPage = HTML.ElementFromString(actorHttp.Content);
                        var interchange = actorPage.SelectSingleNode("//img[@class='info-img']")?.GetAttributeValue("data-interchange", string.Empty);
                        if (interchange != null)
                        {
                            actorPhotoUrl = interchange.Replace("[", string.Empty).Replace("]", string.Empty).Replace(", (small)", string.Empty).Replace(", (medium)", string.Empty).Replace(", (large)", string.Empty).Split(',').Last().Trim();
                        }
                    }

                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken, null, _cookies);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var imageNodes = detailsPageElements.SelectNodes("//div[contains(@class, 'gallery-item')]/a/img | //div[contains(@class, 'video-tour')]//a/img");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    images.Add(new RemoteImageInfo { Url = img.GetAttributeValue("src", string.Empty) });
                }
            }

            var interchangeNodes = detailsPageElements.SelectNodes("//div[contains(@class, 'widescreen')]//img | //div[contains(@class, 'columns')]/img");
            if (interchangeNodes != null)
            {
                foreach (var img in interchangeNodes)
                {
                    var interchange = img.GetAttributeValue("data-interchange", string.Empty);
                    if (interchange != null)
                    {
                        images.Add(new RemoteImageInfo { Url = interchange.Replace("[", string.Empty).Replace("]", string.Empty).Replace(", (small)", string.Empty).Replace(", (medium)", string.Empty).Replace(", (large)", string.Empty).Split(',').Last().Trim() });
                    }
                }
            }

            if (images.Any())
            {
                images.First().Type = ImageType.Primary;
            }

            return images;
        }
    }
}
