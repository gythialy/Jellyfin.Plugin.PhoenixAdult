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
    public class NetworkCzechAV : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string sceneId = "0";
            var parts = searchTitle.Split(' ');
            if (int.TryParse(parts[0], out var id))
            {
                sceneId = parts[0];
                searchTitle = string.Join(" ", parts.Skip(1));
            }

            string searchUrl = Helper.GetSearchSearchURL(siteNum) + searchTitle;
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var searchPageElements = HTML.ElementFromString(httpResult.Content);
            HtmlNodeCollection searchNodes;
            if (siteNum[0] == 1583)
            {
                searchNodes = searchPageElements.SelectNodes("//div[@class='girl']");
            }
            else
            {
                searchNodes = searchPageElements.SelectNodes("//div[@class='episode__title']");
            }

            if (searchNodes != null)
            {
                foreach (var node in searchNodes)
                {
                    string titleNoFormatting = string.Empty;
                    string curId = string.Empty;
                    string releaseDate = string.Empty;
                    string subSite = searchPageElements.SelectSingleNode("//head/title")?.InnerText.Trim();

                    if (siteNum[0] == 1583)
                    {
                        titleNoFormatting = $"{node.SelectSingleNode(".//span[@class='name']")?.InnerText.Trim()} {node.SelectSingleNode(".//span[@class='age']")?.InnerText.Trim()}";
                        string sceneUrl = node.SelectSingleNode("./div/a")?.GetAttributeValue("href", string.Empty);
                        if (!sceneUrl.StartsWith("http"))
                        {
                            sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
                        }

                        curId = Helper.Encode(sceneUrl);
                        var dateNode = node.SelectSingleNode(".//span[@class='updated']");
                        if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                        {
                            releaseDate = parsedDate.ToString("yyyy-MM-dd");
                        }
                    }
                    else
                    {
                        titleNoFormatting = Helper.ParseTitle(node.SelectSingleNode("./a/h1|./a/h2")?.InnerText.Trim(), siteNum);
                        curId = Helper.Encode(node.SelectSingleNode("./a")?.GetAttributeValue("href", string.Empty));
                        if (searchDate.HasValue)
                        {
                            releaseDate = searchDate.Value.ToString("yyyy-MM-dd");
                        }
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}" } },
                        Name = $"{titleNoFormatting} [CzechAV/{subSite}] {releaseDate}",
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
            if (siteNum[0] == 1583)
            {
                movie.Name = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//span[@class='name']")?.InnerText.Trim(), siteNum);
            }
            else
            {
                movie.Name = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//h1[@class='nice-title']|//h2[@class='nice-title']")?.InnerText.Split(':').Last().Trim(), siteNum);
            }

            var descriptionNode = detailsPageElements.SelectSingleNode("//div[contains(@class, 'desc')]//p");
            if (descriptionNode != null)
            {
                if (siteNum[0] == 1583)
                {
                    movie.Overview = detailsPageElements.SelectNodes("//div[contains(@class, 'desc')]//p").LastOrDefault()?.InnerText.Trim();
                }
                else
                {
                    movie.Overview = descriptionNode.InnerText.Trim();
                }
            }

            movie.AddStudio("Czech Authentic Videos");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//ul[@class='tags']/li");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            if (siteNum[0] == 1583)
            {
                string actorName = $"{movie.Name} {detailsPageElements.SelectSingleNode("//span[@class='age']")?.InnerText.Trim()}";
                string actorPhotoUrl = detailsPageElements.SelectSingleNode("//div[contains(@class, 'gallery')]//@href")?.GetAttributeValue("href", string.Empty);
                result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
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

            var imageNodes = detailsPageElements.SelectNodes("//meta[@property='og:image'] | //img[@class='thumb'] | //div[contains(@class, 'gallery')]//a");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    string imageUrl = img.GetAttributeValue("content", string.Empty) ?? img.GetAttributeValue("src", string.Empty) ?? img.GetAttributeValue("href", string.Empty);
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
