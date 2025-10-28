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
    public class SiteDorcelClub : IProviderBase
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
            var sceneNodes = searchPageElements.SelectNodes("//div[@class='scenes list']/div[@class='items']/div[@class='scene thumbnail ']");
            if (sceneNodes != null)
            {
                foreach (var node in sceneNodes)
                {
                    var titleNode = node.SelectSingleNode(".//div[@class='textual']/a");
                    string titleNoFormatting = titleNode?.InnerText.Trim();
                    string curId = Helper.Encode(titleNode?.GetAttributeValue("href", string.Empty));
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curId } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}]",
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }
            }

            var movieNodes = searchPageElements.SelectNodes("//div[@class='movies list']/div[@class='items']/a[@class='movie thumbnail']");
            if (movieNodes != null)
            {
                foreach (var node in movieNodes)
                {
                    string titleNoFormatting = node.SelectSingleNode("./h2")?.InnerText.Trim();
                    string movieLink = node.GetAttributeValue("href", string.Empty);
                    string curId = Helper.Encode(movieLink);
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curId } },
                        Name = $"{titleNoFormatting} - Full Movie [{Helper.GetSearchSiteName(siteNum)}]",
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

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("///span[@class='full']")?.InnerText.Trim();
            movie.AddStudio("Marc Dorcel");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//span[@class='publish_date']") ?? detailsPageElements.SelectSingleNode("//span[@class='out_date']");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Replace("Year :", string.Empty).Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            movie.AddGenre("French porn");
            var movieNameNode = detailsPageElements.SelectSingleNode("//span[@class='movie']/a");
            if (movieNameNode != null)
            {
                movie.AddGenre("Blockbuster Movie");
            }

            var actorNodes = detailsPageElements.SelectNodes("//div[@class='actress']/a") ?? detailsPageElements.SelectNodes("//div[@class='actor thumbnail ']/a/div[@class='name']");
            if (actorNodes != null)
            {
                if (!sceneUrl.Contains("porn-movie"))
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
                }

                foreach (var actor in actorNodes)
                {
                    result.People.Add(new PersonInfo { Name = actor.InnerText.Trim(), Type = PersonKind.Actor });
                }
            }

            var directorNode = detailsPageElements.SelectSingleNode("//span[@class='director']");
            if (directorNode != null)
            {
                result.People.Add(new PersonInfo { Name = directorNode.InnerText.Replace("Director :", string.Empty).Trim(), Type = PersonKind.Director });
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

            var imageNodes = detailsPageElements.SelectNodes("//div[contains(@class, 'photos')]//source");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    string imageUrl = img.GetAttributeValue("data-srcset", string.Empty).Split(',').Last().Split(' ').First();
                    string trash = $"_{imageUrl.Split('_').Last().Split('.').First()}";
                    imageUrl = imageUrl.Replace(trash, string.Empty);
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
