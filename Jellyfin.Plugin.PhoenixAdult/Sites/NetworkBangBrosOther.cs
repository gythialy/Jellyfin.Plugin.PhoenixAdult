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
    public class NetworkBangBrosOther : IProviderBase
    {
        private static readonly Dictionary<string, List<string>> sceneActorsDB = new Dictionary<string, List<string>>
        {
            { "102939", new List<string> { "Peter Green", "Violet Gems" } },
        };

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
            var searchNodes = searchPageElements.SelectNodes("//div[@class='videos-list']/article");
            if (searchNodes != null)
            {
                foreach (var node in searchNodes)
                {
                    var titleNode = node.SelectSingleNode(".//@title");
                    if (titleNode != null)
                    {
                        string titleNoFormatting = titleNode.GetAttributeValue("title", string.Empty).Split('(')[0].Split('–').Last().Trim();
                        string sceneUrl = node.SelectSingleNode(".//@href").GetAttributeValue("href", string.Empty);
                        string curId = Helper.Encode(sceneUrl);
                        string releaseDate = searchDate?.ToString("yyyy-MM-dd") ?? string.Empty;

                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, $"{curId}|{releaseDate}" } },
                            Name = $"{titleNoFormatting} [BangBros]",
                            SearchProviderName = Plugin.Instance.Name,
                        });
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
            string sceneDate = providerIds.Length > 1 ? providerIds[1] : null;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            string sceneId = detailsPageElements.SelectSingleNode("//article")?.GetAttributeValue("id", string.Empty).Split('-').Last();

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//h1")?.InnerText.Split('(')[0].Split('–').Last().Trim();

            var summaryNode = detailsPageElements.SelectSingleNode("//div[@class='video-description']//strong[contains(., 'Description')]");
            if (summaryNode != null)
            {
                movie.Overview = summaryNode.NextSibling.InnerText.Trim();
            }

            movie.AddStudio("Bang Bros");

            var tagline = detailsPageElements.SelectSingleNode("//span[@class='fn']")?.InnerText;
            if (tagline != null)
            {
                movie.AddTag(tagline);
            }

            var dateNode = detailsPageElements.SelectSingleNode("//div[@class='video-description']//strong[contains(., 'Date')]");
            if (dateNode != null && DateTime.TryParse(dateNode.NextSibling.InnerText.Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }
            else if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//*[.//@class='fa fa-tag']");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//*[.//@class='fa fa-star']");
            if (actorNodes != null)
            {
                foreach (var actor in actorNodes)
                {
                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actor.InnerText.Trim(), Type = PersonKind.Actor });
                }
            }

            if (sceneId != null && sceneActorsDB.ContainsKey(sceneId))
            {
                foreach (var actorName in sceneActorsDB[sceneId])
                {
                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var posterNode = detailsPageElements.SelectSingleNode("//meta[@itemprop='thumbnailUrl']");
            if (posterNode != null)
            {
                string imageUrl = posterNode.GetAttributeValue("content", string.Empty).Replace("-320x180", string.Empty);
                images.Add(new RemoteImageInfo { Url = imageUrl, Type = ImageType.Primary });
            }

            return images;
        }
    }
}
