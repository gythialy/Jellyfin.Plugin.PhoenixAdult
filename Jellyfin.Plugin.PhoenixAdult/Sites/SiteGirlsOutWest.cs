using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
#if !__EMBY__
using Jellyfin.Data.Enums;
#endif
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    public class SiteGirlsOutWest : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            // 站内搜索不可用（无搜索接口），改分页浏览 + 标题过滤（TPDB 同款模式）
            var titleTokens = searchTitle.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (var page = 1; page <= 5; page++)
            {
                var pageUrl = $"https://tour.girlsoutwest.com/categories/Movies_{page}_d.html";
                var httpResult = await HTTP.Request(pageUrl, HttpMethod.Get, cancellationToken);
                if (!httpResult.IsOK)
                {
                    break;
                }

                var pageDoc = HTML.ElementFromString(httpResult.Content);
                var cards = pageDoc.SelectNodesSafe("//a[contains(@href, '/trailers/')]");
                if (!cards.Any())
                {
                    break;
                }

                foreach (var card in cards)
                {
                    var href = card.GetAttributeValue("href", string.Empty);
                    if (string.IsNullOrEmpty(href))
                    {
                        continue;
                    }

                    var cardTitle = card.InnerText.Trim();
                    var haystack = string.IsNullOrEmpty(cardTitle) ? href : cardTitle;
                    if (!titleTokens.All(t => haystack.ToLowerInvariant().Contains(t, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var sceneUrl = href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? href : Helper.GetSearchBaseURL(siteNum) + href;
                    if (result.Any(r => r.ProviderIds.First().Value == Helper.Encode(sceneUrl)))
                    {
                        continue;
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, Helper.Encode(sceneUrl) } },
                        Name = $"{cardTitle} [GirlsOutWest]",
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }

                if (result.Count >= 10)
                {
                    break;
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
            movie.ExternalId = sceneUrl;
            movie.Name = detailsPageElements.SelectSingleNode("//meta[@name='twitter:title']")?.GetAttributeValue("content", string.Empty).Trim();
            movie.AddStudio("GirlsOutWest");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddStudio(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//div[@class='trailer topSpace']/div[2]/p");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Split('\\')[1].Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            movie.AddGenre("Amateur");
            movie.AddGenre("Australian");

            var actorNodes = detailsPageElements.SelectNodes("//div[@class='trailer topSpace']/div[2]/p/a");
            if (actorNodes != null)
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

                foreach (var actor in actorNodes)
                {
                    string actorName = actor.InnerText.Trim();
                    string actorPageUrl = actor.GetAttributeValue("href", string.Empty);
                    string actorPhotoUrl = string.Empty;
                    var actorHttp = await HTTP.Request(actorPageUrl, HttpMethod.Get, cancellationToken);
                    if (actorHttp.IsOK)
                    {
                        var actorPage = HTML.ElementFromString(actorHttp.Content);
                        actorPhotoUrl = Helper.GetSearchBaseURL(siteNum) + actorPage.SelectSingleNode("//div[@class='profilePic']/img")?.GetAttributeValue("src0_3x", string.Empty);
                    }

                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
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

            var imageNodes = detailsPageElements.SelectNodes("//div[@class='videoplayer']/img");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    images.Add(new RemoteImageInfo { Url = Helper.GetSearchBaseURL(siteNum) + img.GetAttributeValue("src0_3x", string.Empty) });
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
