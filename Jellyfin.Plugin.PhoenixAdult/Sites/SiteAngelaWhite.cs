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
using Jellyfin.Data.Enums;

namespace PhoenixAdult.Sites
{
    public class SiteAngelaWhite : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string searchUrl = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(searchTitle);
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var searchPageElements = HTML.ElementFromString(httpResult.Content);
            var searchNodes = searchPageElements.SelectNodes("//div[@class='videodetails']");
            if (searchNodes != null)
            {
                foreach (var node in searchNodes)
                {
                    var titleNode = node.SelectSingleNode("./div[contains(@class, 'videocontent')]");
                    string titleNoFormatting = titleNode?.GetAttributeValue("data-title", string.Empty);
                    string sceneUrl = node.SelectSingleNode("./div[contains(@class, 'videocontent')]/h2/a")?.GetAttributeValue("href", string.Empty);
                    string curId = Helper.Encode(sceneUrl);
                    string releaseDate = string.Empty;
                    var dateNode = node.SelectSingleNode("./div[contains(@class, 'videocontent')]/h2/span");
                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{Helper.Encode(titleNoFormatting)}|{releaseDate}" } },
                        Name = $"{titleNoFormatting} {releaseDate} [{Helper.GetSearchSiteName(siteNum)}]",
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

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneUrl;
            movie.AddStudio("Angela White");
            movie.Name = Helper.Decode(providerIds[1]).Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//div[@class='desc']/p")?.InnerText.Trim();

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddStudio(tagline);

            var genreNodes = detailsPageElements.SelectSingleNode("//meta[@name='keywords']")?.GetAttributeValue("content", string.Empty).Replace(".", string.Empty).Split(',');
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.Trim());
                }
            }

            if (DateTime.TryParse(Helper.Decode(providerIds[2]), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            string actorStr = Regex.Replace(movie.Name.Replace("BTS", string.Empty), @"\d", string.Empty).Trim();
            var actors = actorStr.Split(new[] { " X " }, StringSplitOptions.None);
            foreach (var actor in actors)
            {
                ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actor.Trim(), Type = PersonKind.Actor });
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

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var imageNodes = detailsPageElements.SelectNodes("//img[contains(@class, 'tour-area-thumb')]");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    images.Add(new RemoteImageInfo { Url = Helper.GetSearchBaseURL(siteNum) + img.GetAttributeValue("data-src", string.Empty) });
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
