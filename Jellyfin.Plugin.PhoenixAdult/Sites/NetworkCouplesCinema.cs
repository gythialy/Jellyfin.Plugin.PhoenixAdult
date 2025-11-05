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
    public class NetworkCouplesCinema : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string sceneId = null;
            if (searchTitle.Split(' ').FirstOrDefault() != null && int.TryParse(searchTitle.Split(' ').First(), out _))
            {
                sceneId = searchTitle.Split(' ').First();
                searchTitle = searchTitle.Replace(sceneId, string.Empty).Trim();
            }

            if (sceneId != null)
            {
                string sceneUrl = $"{Helper.GetSearchBaseURL(siteNum)}/post/details/{sceneId}";
                var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var detailsPageElements = HTML.ElementFromString(httpResult.Content);
                    string curId = Helper.Encode(sceneUrl);
                    string titleNoFormatting = detailsPageElements.SelectSingleNode("//div[contains(@class, 'mediaHeader')]//span[contains(@class, 'title')]")?.InnerText.Trim();
                    string studio = detailsPageElements.SelectSingleNode("//span[contains(@class, 'type')]")?.InnerText.Split('|')[0].Trim();
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}" } },
                        Name = $"{titleNoFormatting} [{studio}]",
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }
            }
            else
            {
                string searchUrl = Helper.GetSearchSearchURL(siteNum) + searchTitle.Replace(" ", "+");
                var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var searchPageElements = HTML.ElementFromString(httpResult.Content);
                    var searchNodes = searchPageElements.SelectNodes("//div[contains(@class, 'post')]");
                    if (searchNodes != null)
                    {
                        foreach (var node in searchNodes)
                        {
                            string titleNoFormatting = node.SelectSingleNode(".//span[contains(@class, 'title')]")?.InnerText.Trim();
                            string sceneUrl = node.SelectSingleNode(".//a[contains(@class, 'media')]")?.GetAttributeValue("href", string.Empty);
                            string studio = node.SelectSingleNode(".//span[contains(@class, 'source')]")?.InnerText.Trim();
                            string sceneCover = Helper.Encode(node.SelectSingleNode(".//a[contains(@class, 'media')]//img[contains(@class, 'image')]")?.GetAttributeValue("src", string.Empty));
                            string releaseDate = searchDate?.ToString("yyyy-MM-dd") ?? string.Empty;
                            string curId = Helper.Encode(sceneUrl);

                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, $"{curId}|{releaseDate}|{sceneCover}" } },
                                Name = $"{titleNoFormatting} [{studio}]",
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
            string sceneUrl = Helper.Decode(providerIds[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            string searchDate = providerIds.Length > 1 ? providerIds[1] : string.Empty;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//div[contains(@class, 'mediaHeader')]//span[contains(@class, 'title')]")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//span[contains(@class, 'description')]")?.InnerText.Trim();
            movie.AddStudio("Couples Cinema");

            string tagline = detailsPageElements.SelectSingleNode("//span[contains(@class, 'type')]")?.InnerText.Split('|')[0].Trim();
            movie.AddTag(tagline);
            movie.AddCollection(tagline);

            if (!string.IsNullOrEmpty(searchDate) && DateTime.TryParse(searchDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }
            else
            {
                string year = detailsPageElements.SelectSingleNode("//span[contains(@class, 'type')]")?.InnerText.Split('|')[1].Trim();
                if (int.TryParse(year, out var parsedYear))
                {
                    movie.ProductionYear = parsedYear;
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//div[contains(@class, 'cast')]/a");
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
            string[] providerIds = sceneID[0].Split('|');
            string sceneUrl = Helper.Decode(providerIds[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            string sceneCover = providerIds.Length > 2 ? Helper.Decode(providerIds[2]) : string.Empty;

            if (!string.IsNullOrEmpty(sceneCover))
            {
                images.Add(new RemoteImageInfo { Url = sceneCover });
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
                string imageUrl = posterNode.GetAttributeValue("poster", string.Empty);
                if (!imageUrl.StartsWith("http"))
                {
                    imageUrl = Helper.GetSearchBaseURL(siteNum) + imageUrl;
                }

                images.Add(new RemoteImageInfo { Url = imageUrl });
            }

            if (images.Any())
            {
                images.First().Type = ImageType.Primary;
            }

            return images;
        }
    }
}
