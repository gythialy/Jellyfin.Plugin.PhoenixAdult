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
    public class NetworkCouplesCinema : IProviderBase
    {
        private readonly Dictionary<string, string> _cookies = new Dictionary<string, string> { { "WarningModal", "true" } };

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
                var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken, cookies: _cookies);
                if (httpResult.IsOK)
                {
                    var detailsPageElements = HTML.ElementFromString(httpResult.Content);
                    string curId = Helper.Encode(sceneUrl);
                    string titleNoFormatting = detailsPageElements.SelectSingleNode("//span[contains(@class, 'gqTitle')]")?.InnerText.Trim();
                    string studio = detailsPageElements.SelectSingleNode("//div[contains(@class, 'gqProducer')]//a")?.InnerText.Trim();
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
                bool hasNextPage = true;
                while (hasNextPage)
                {
                    hasNextPage = false;
                    var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken, cookies: _cookies);
                    if (!httpResult.IsOK)
                    {
                        break;
                    }

                    var searchPageElements = HTML.ElementFromString(httpResult.Content);
                    var searchNodes = searchPageElements.SelectNodes("//div[contains(@class, 'gqPostContainer')]");
                    if (searchNodes != null)
                    {
                        foreach (var node in searchNodes)
                        {
                            string sceneUrl = node.SelectSingleNode(".//a[contains(@class, 'gqPost')]")?.GetAttributeValue("href", string.Empty);
                            if (string.IsNullOrEmpty(sceneUrl))
                            {
                                continue;
                            }

                            string curId = Helper.Encode(sceneUrl);
                            string coverImage = node.GetAttributeValue("data-img", string.Empty);
                            var idRegex = new Regex(@"/(\d+)$");
                            var match = idRegex.Match(sceneUrl);
                            string id = match.Success ? match.Groups[1].Value : "0";

                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, curId } },
                                Name = $"Scene {id}",
                                ImageUrl = coverImage,
                                SearchProviderName = Plugin.Instance.Name,
                            });
                        }
                    }

                    var nextPageNode = searchPageElements.SelectSingleNode("//a[@class='pageBtn gqPage' and text()='>']");
                    if (nextPageNode != null)
                    {
                        string nextPageLink = nextPageNode.GetAttributeValue("href", string.Empty);
                        if (!string.IsNullOrEmpty(nextPageLink))
                        {
                            if (!nextPageLink.StartsWith("http"))
                            {
                                nextPageLink = new Uri(new Uri(Helper.GetSearchBaseURL(siteNum)), nextPageLink).ToString();
                            }

                            if (nextPageLink != searchUrl)
                            {
                                searchUrl = nextPageLink;
                                hasNextPage = true;
                            }
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

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken, cookies: _cookies);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//span[contains(@class, 'gqTitle')]")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//span[contains(@class, 'gqDescription')]")?.InnerText.Trim();
            movie.AddStudio("Couples Cinema");

            string tagline = detailsPageElements.SelectSingleNode("//div[contains(@class, 'gqProducer')]//a")?.InnerText.Trim();
            movie.AddTag(tagline);
            movie.AddCollection(tagline);

            var actorNodes = detailsPageElements.SelectNodes("//div[contains(@class, 'gqModels')]//a");
            if (actorNodes != null)
            {
                foreach (var actor in actorNodes)
                {
                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actor.InnerText.Trim(), Type = PersonKind.Actor });
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

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken, cookies: _cookies);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var style = detailsPageElements.SelectSingleNode("//div[contains(@class, 'gqTop')]")?.GetAttributeValue("style", string.Empty);
            if (!string.IsNullOrEmpty(style))
            {
                var urlRegex = new Regex(@"url\(([^)]+)\)");
                var match = urlRegex.Match(style);
                if (match.Success)
                {
                    images.Add(new RemoteImageInfo { Url = match.Groups[1].Value, Type = ImageType.Primary });
                }
            }

            return images;
        }
    }
}
