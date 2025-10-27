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
    public class NetworkCzechVR : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string sceneId = null;
            if (int.TryParse(searchTitle.Split(' ').FirstOrDefault() ?? string.Empty, out _))
            {
                sceneId = searchTitle.Split(' ').First();
                searchTitle = searchTitle.Replace(sceneId, string.Empty).Trim();
            }

            string searchUrl = Helper.GetSearchSearchURL(siteNum) + searchTitle.Replace(' ', '-');
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var searchPageElements = HTML.ElementFromString(httpResult.Content);
            var searchNodes = searchPageElements.SelectNodes("//div[contains(@class, 'postTag')]");
            if (searchNodes != null)
            {
                foreach (var node in searchNodes)
                {
                    var titleNode = node.SelectSingleNode(".//div[@class='nazev']//h2//a");
                    string titleNoFormatting = titleNode?.InnerText;
                    string curSceneId = titleNoFormatting?.Split('-')[0].Trim();
                    string curId = Helper.Encode(node.SelectSingleNode(".//a")?.GetAttributeValue("href", string.Empty));
                    string releaseDate = string.Empty;
                    var dateNode = node.SelectSingleNode(".//div[@class='datum']");
                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    var actorNodes = node.SelectNodes(".//div[@class='nazev']//div[@class='featuring']//a");
                    string actorList = string.Join(", ", actorNodes?.Select(a => a.InnerText.Trim()) ?? new string[0]);

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                        Name = $"{actorList} in {titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
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

            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
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
            movie.Name = detailsPageElements.SelectSingleNode("//head/title")?.InnerText
                .Replace("Czech VR Network", string.Empty).Replace(" - Czech VR Fetish Porn Videos", string.Empty).Replace("Czech VR Fetish", string.Empty).Replace("Czech VR Casting", string.Empty).Replace("Czech VR", string.Empty).Trim();

            var summaryNode = detailsPageElements.SelectSingleNode("//div[@class='text'] ?? //div[@class='textDetail']");
            if (summaryNode != null)
            {
                movie.Overview = summaryNode.InnerText.Trim();
            }

            movie.AddStudio("CzechVR");
            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);
            movie.AddCollection(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//div[contains(@class, 'nazev')]//div[@class='datum']");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//div[@class='tag new']//a | //div[@class='tag']//a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.ToLower().Trim());
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//div[@class='modelky']//a | (//div[contains(@class, 'nazev')])[1]//div[@class='featuring']//a");
            if (actorNodes != null)
            {
                foreach (var actor in actorNodes)
                {
                    result.People.Add(new PersonInfo { Name = actor.InnerText.Trim(), Type = PersonKind.Actor });
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

            var imageNodes = detailsPageElements.SelectNodes("//div[@class='foto']//dl8-video | //div[@class='galerka']//a");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    string imageUrl = (img.GetAttributeValue("poster", string.Empty) ?? img.GetAttributeValue("href", string.Empty)).Substring(1);
                    if (!imageUrl.StartsWith("http"))
                    {
                        imageUrl = Helper.GetSearchBaseURL(siteNum) + imageUrl;
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
