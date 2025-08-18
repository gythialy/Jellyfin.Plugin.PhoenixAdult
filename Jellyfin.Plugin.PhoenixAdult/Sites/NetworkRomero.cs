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
    public class NetworkRomero : IProviderBase
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
            var searchNodes = searchPageElements.SelectNodes("//div[contains(@class, 'half')]|//article[contains(@class, 'post')]");
            if (searchNodes != null)
            {
                foreach (var node in searchNodes)
                {
                    var titleNode = node.SelectSingleNode(".//h2");
                    string titleNoFormatting = titleNode?.InnerText.Trim();
                    var sceneUrlNode = node.SelectSingleNode(".//a");
                    string sceneUrl = sceneUrlNode?.GetAttributeValue("href", string.Empty);
                    if (!sceneUrl.StartsWith("http"))
                    {
                        sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
                    }

                    string curId = Helper.Encode(sceneUrl);

                    var dateNode = node.SelectSingleNode(".//h2[2]") ?? node.SelectSingleNode(".//div[@class='entry-date']");
                    string releaseDate = string.Empty;
                    if(dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim().Split(new[] { "&nbsp" }, StringSplitOptions.None).Last(), out var parsedDate))
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

            string[] providerIds = sceneID[0].Split('|');
            string sceneUrl = Helper.Decode(providerIds[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            string sceneDate = providerIds.Length > 2 ? providerIds[2] : null;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.Name = (detailsPageElements.SelectSingleNode("//meta[@itemprop='name']")?.GetAttributeValue("content", string.Empty) ?? detailsPageElements.SelectSingleNode("//h1")?.InnerText)?.Split('|')[0].Split(new[] { "- Free Video" }, StringSplitOptions.None)[0].Trim();

            var summaryNodes = (siteNum[0] >= 1797 && siteNum[0] <= 1798)
                ? detailsPageElements.SelectNodes("//div[@id='fullstory']/p")
                : detailsPageElements.SelectNodes("//div[@class='cont']/p|//div[@class='cont']//div[@id='fullstory']/p|//div[@class='zapdesc']//div[not(contains(., 'Including'))][.//br]");
            if(summaryNodes != null)
            {
                movie.Overview = string.Join("\n", summaryNodes.Select(s => s.InnerText.Trim()).Where(s => !string.IsNullOrEmpty(s) && s != "\u00a0"));
            }

            movie.AddStudio("Romero Multimedia");
            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//meta[@property='article:published_time']");
            if (dateNode != null && DateTime.TryParse(dateNode.GetAttributeValue("content", string.Empty).Split('T')[0].Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }
            else if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//div[@class='Cats']//a|//div[@class='zapdesc']/div/div/div[contains(., 'Including:')]");
            if(genreNodes != null)
            {
                foreach(var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var actorXPath = siteNum[0] == 896 ? "//div[contains(@class, 'tagsmodels')]//a" : "//div[contains(@class, 'tagsmodels')][./img[@alt='model icon']]//a";
            var actorNodes = detailsPageElements.SelectNodes(actorXPath);
            if(actorNodes != null)
            {
                foreach(var actor in actorNodes)
                {
                    result.People.Add(new PersonInfo { Name = actor.InnerText.Trim(), Type = PersonKind.Actor });
                }
            }

            var directorNode = detailsPageElements.SelectSingleNode("//div[contains(@class, 'director')]//a");
            if(directorNode != null)
            {
                result.People.Add(new PersonInfo { Name = directorNode.InnerText.Trim(), Type = PersonKind.Director });
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

            var imageNodes = detailsPageElements.SelectNodes("//img[(contains(@class, 'alignnone') and contains(@class, 'size-full') or contains(@class, 'size-medium')) and (not(contains(@class, 'wp-image-4512') or contains(@class, 'wp-image-492')))] | //div[@class='iehand']/a | //a[contains(@class, 'colorbox-cats')] | //div[@class='gallery']//a");
            if(imageNodes != null)
            {
                foreach(var img in imageNodes)
                {
                    string imageUrl = img.GetAttributeValue("src", string.Empty) ?? img.GetAttributeValue("href", string.Empty);
                    if(imageUrl.Contains("?src="))
                    {
                        imageUrl = System.Web.HttpUtility.ParseQueryString(new Uri(imageUrl).Query).Get("src");
                    }

                    images.Add(new RemoteImageInfo { Url = imageUrl });
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
