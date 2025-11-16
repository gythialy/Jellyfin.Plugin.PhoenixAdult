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
    public class SiteHollyRandall : IProviderBase
    {
        private const string JoinStr = "https://join.hollyrandall.com/";

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
            var searchNodes = searchPageElements.SelectNodes("//div[contains(@class, 'latestUpdateB')]");
            if (searchNodes != null)
            {
                foreach (var node in searchNodes)
                {
                    var titleNode = node.SelectSingleNode(".//h4[@class='link_bright']/a");
                    string titleNoFormatting = titleNode?.InnerText.Trim();
                    string sceneUrl = titleNode?.GetAttributeValue("href", string.Empty);
                    Logger.Info($"[SiteHollyRandall] sceneUrl: {sceneUrl}");
                    if (sceneUrl?.StartsWith(JoinStr) == false)
                    {
                        string curId = Helper.Encode(sceneUrl);
                        string releaseDate = string.Empty;
                        var dateNode = node.SelectSingleNode("./div[@class='timeDate']");
                        if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Split('|')[1].Trim(), out var parsedDate))
                        {
                            releaseDate = parsedDate.ToString("yyyy-MM-dd");
                        }

                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, $"{curId}|{Helper.Encode(titleNoFormatting)}|{releaseDate}" } },
                            Name = $"{titleNoFormatting} {releaseDate} [{Helper.GetSearchSiteName(siteNum)}]",
                            SearchProviderName = Plugin.Instance.Name,
                            ImageUrl = node.SelectSingleNode(".//img")?.GetAttributeValue("src0_1x", string.Empty),
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
            movie.AddStudio("Holly Randall Productions");
            movie.Name = Helper.Decode(providerIds[1]).Trim();

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);

            var genreNodes = detailsPageElements.SelectNodes("//ul[@class='tags']/li/a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            if (DateTime.TryParse(providerIds[2], out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var actorNodes = detailsPageElements.SelectSingleNode("//div[@class='info']/p")?.InnerText.Split('\n')[3].Replace("Featuring:", string.Empty).Split(',');
            if (actorNodes != null)
            {
                foreach (var actor in actorNodes)
                {
                    result.People.Add(new PersonInfo { Name = actor.Trim(), Type = PersonKind.Actor });
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

            var imageNodes = detailsPageElements.SelectNodes("//img[contains(@class, 'update_thumb')]");
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
