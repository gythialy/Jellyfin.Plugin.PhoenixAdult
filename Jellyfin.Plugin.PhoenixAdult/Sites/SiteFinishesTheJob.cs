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
    public class SiteFinishesTheJob : IProviderBase
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
            var searchNodes = searchPageElements.SelectNodes("//div[contains(@class, 'scene')]");
            if (searchNodes != null)
            {
                foreach (var node in searchNodes)
                {
                    var titleNode = node.SelectSingleNode(".//h3[@itemprop='name']");
                    string titleNoFormatting = titleNode?.InnerText;
                    string curId = Helper.Encode(node.SelectSingleNode(".//a")?.GetAttributeValue("href", string.Empty));
                    string releaseDate = searchDate?.ToString("yyyy-MM-dd") ?? string.Empty;

                    var subSiteNode = node.SelectSingleNode(".//div[@class='card-footer']//a");
                    var match = Regex.Match(subSiteNode?.GetAttributeValue("href", string.Empty) ?? string.Empty, @"(?<=scene\/)(.*?)(?=\/)");
                    string subSiteName = string.Empty;
                    if (match.Success)
                    {
                        var subSiteNum = Database.SiteList.Sites.FirstOrDefault(s => s.Value.Values.Any(v => v[0] == match.Value)).Key;
                        subSiteName = Helper.GetSearchSiteName(new[] { subSiteNum, 0 });
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{releaseDate}" } },
                        Name = $"{titleNoFormatting} [{subSiteName}]",
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

            string sceneDate = providerIds.Length > 1 ? providerIds[1] : null;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneUrl;
            movie.Name = detailsPageElements.SelectSingleNode("//span[@itemprop='name']")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//p[@itemprop='description']")?.InnerText.Trim();
            movie.AddStudio("Finishes The Job");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddStudio(tagline);

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var actorNodes = detailsPageElements.SelectNodes("//h2[contains(., 'Starring')]//a");
            if (actorNodes != null)
            {
                foreach (var actor in actorNodes)
                {
                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actor.InnerText.Trim(), Type = PersonKind.Actor });
                }
            }

            var genreNodes = detailsPageElements.SelectNodes("//p[contains(., 'Categories')]//a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
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

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var posterNode = detailsPageElements.SelectSingleNode("//video");
            if (posterNode != null)
            {
                images.Add(new RemoteImageInfo { Url = Helper.GetSearchBaseURL(siteNum) + posterNode.GetAttributeValue("poster", string.Empty), Type = ImageType.Primary });
            }

            var imageNodes = detailsPageElements.SelectNodes("//div[contains(@class, 'first-set')]//img");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    if (img.GetAttributeValue("alt", string.Empty).Equals(item.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        images.Add(new RemoteImageInfo { Url = img.GetAttributeValue("src", string.Empty) });
                    }
                }
            }

            return images;
        }
    }
}
