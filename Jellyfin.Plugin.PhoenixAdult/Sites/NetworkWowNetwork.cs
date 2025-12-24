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
using Jellyfin.Data.Enums;

namespace PhoenixAdult.Sites
{
    public class NetworkWowNetwork : IProviderBase
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

            var searchResults = HTML.ElementFromString(httpResult.Content);
            var pages = new List<string> { searchUrl };
            var pageNodes = searchResults.SelectNodes("//div[@class='pagination']/ul/li/a");
            if (pageNodes != null)
            {
                pages.AddRange(pageNodes.Select(p => p.GetAttributeValue("href", string.Empty)));
            }

            foreach (var page in pages)
            {
                var pageHttp = await HTTP.Request(page, HttpMethod.Get, cancellationToken);
                if (pageHttp.IsOK)
                {
                    var pageResults = HTML.ElementFromString(pageHttp.Content);
                    var searchNodes = pageResults.SelectNodes("//main//article[contains(@class,'thumb-block')]");
                    if (searchNodes != null)
                    {
                        foreach (var node in searchNodes)
                        {
                            string siteName = Helper.GetSearchSiteName(siteNum);
                            string titleNoFormatting = node.SelectSingleNode(".//a").GetAttributeValue("title", string.Empty).Trim();
                            string curId = Helper.Encode(node.SelectSingleNode(".//a").GetAttributeValue("href", string.Empty));
                            string image = Helper.Encode(node.SelectSingleNode(".//img").GetAttributeValue("src", string.Empty));

                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, $"{curId}|{image}" } },
                                Name = $"{titleNoFormatting} [{siteName}]",
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

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneUrl;
            movie.Name = detailsPageElements.SelectNodes("//h1[@class='entry-title']").Last().InnerText.Trim();
            movie.AddStudio("WowNetwork");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddStudio(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//div[@id='video-date']");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Replace("Date:", string.Empty).Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//div[@class='tags-list']/a//i[@class='fa fa-folder-open']/..");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Replace("Movies", string.Empty).Trim());
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//div[@id='video-actors']//a");
            if (actorNodes != null)
            {
                foreach (var actor in actorNodes)
                {
                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actor.InnerText.Trim(), Type = PersonKind.Actor });
                }
            }

            return result;
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string image = Helper.Decode(sceneID[0].Split('|')[1]);
            if (!string.IsNullOrEmpty(image))
            {
                images.Add(new RemoteImageInfo { Url = image });
            }

            if (images.Any())
            {
                images.First().Type = ImageType.Primary;
            }

            return Task.FromResult<IEnumerable<RemoteImageInfo>>(images);
        }
    }
}
