using System;
using System.Collections.Generic;
using System.Linq;
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
    public class NetworkWowNetwork : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{Uri.EscapeDataString(searchTitle)}";

            var pages = new List<string> { searchUrl };
            var http = await HTTP.Request(searchUrl, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                var pageNodes = doc.DocumentNode.SelectNodes(@"//div[@class=""pagination""]/ul/li/a");
                if (pageNodes != null)
                {
                    foreach (var page in pageNodes)
                    {
                        var pageUrl = page.GetAttributeValue("href", string.Empty);
                        if (!string.IsNullOrEmpty(pageUrl) && !pages.Contains(pageUrl))
                        {
                            pages.Add(pageUrl);
                        }
                    }
                }
            }

            foreach (var page in pages)
            {
                http = await HTTP.Request(page, cancellationToken);
                if (http.IsOK)
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(http.Content);

                    foreach (var searchResult in doc.DocumentNode.SelectNodes(@"//main//article[contains(@class,""thumb-block"")]"))
                    {
                        var titleNode = searchResult.SelectSingleNode(".//a");
                        var titleNoFormatting = titleNode?.GetAttributeValue("title", string.Empty).Trim();
                        var sceneURL = titleNode?.GetAttributeValue("href", string.Empty);
                        var curID = Helper.Encode(sceneURL);
                        var image = Helper.Encode(searchResult.SelectSingleNode(".//img").GetAttributeValue("src", string.Empty));

                        var item = new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}|{image}" } },
                            Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}]",
                            SearchProviderName = Plugin.Instance.Name,
                        };
                        result.Add(item);
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
            var movie = (Movie)result.Item;
            var providerIds = sceneID[0].Split('|');
            var sceneURL = Helper.Decode(providerIds[0]);
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);

            movie.Name = doc.DocumentNode.SelectSingleNode(@"//h1[@class=""entry-title""]")?.InnerText.Trim();
            movie.AddStudio("WowNetwork");
            var tagline = Helper.GetSearchSiteName(siteNum);
            movie.Tagline = tagline;
            movie.AddTag(tagline);

            var dateNode = doc.DocumentNode.SelectSingleNode(@"//div[@id=""video-date""]");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Replace("Date:", string.Empty).Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            foreach (var genreLink in doc.DocumentNode.SelectNodes(@"//div[@class=""tags-list""]/a//i[@class=""fa fa-folder-open""]/.."))
            {
                var genreName = genreLink.InnerText.Replace("Movies", string.Empty).Trim();
                if (!string.IsNullOrEmpty(genreName))
                {
                    movie.AddGenre(genreName);
                }
            }

            foreach (var actorLink in doc.DocumentNode.SelectNodes(@"//div[@id=""video-actors""]//a"))
            {
                var actorName = actorLink.InnerText.Trim();
                if (!string.IsNullOrEmpty(actorName))
                {
                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
                }
            }

            return result;
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var providerIds = sceneID[0].Split('|');
            if (providerIds.Length > 2)
            {
                var imageUrl = Helper.Decode(providerIds[2]);
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    images.Add(new RemoteImageInfo { Url = imageUrl, Type = ImageType.Primary });
                }
            }

            return Task.FromResult<IEnumerable<RemoteImageInfo>>(images);
        }
    }
}
