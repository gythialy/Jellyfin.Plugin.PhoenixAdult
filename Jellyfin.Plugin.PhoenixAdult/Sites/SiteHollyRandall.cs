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

                        var image = Helper.GetSearchBaseURL(siteNum) + node.SelectSingleNode(".//img")?.GetAttributeValue("src0_2x", string.Empty);
                        Logger.Info($"[SiteHollyRandall] image: {image}");
                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, curId } },
                            Name = $"{titleNoFormatting} {releaseDate} [{Helper.GetSearchSiteName(siteNum)}]",
                            SearchProviderName = Plugin.Instance.Name,
                            ImageUrl = image,
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

            string sceneUrl = Helper.Decode(sceneID[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            Logger.Info($"[SiteHollyRandall] Update isOK: {httpResult.IsOK}");
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneUrl;
            movie.AddStudio("Holly Randall Productions");

            var title = detailsPageElements.SelectSingleNode("//div[@class='vidImgTitle']/h4").InnerText.Trim();
            Logger.Info($"[SiteHollyRandall] Update title: {title}");
            movie.Name = title;

            string tagline = Helper.GetSearchSiteName(siteNum);
            Logger.Info($"[SiteHollyRandall] Update tagline: {tagline}");
            movie.AddStudio(tagline);

            var genreNodes = detailsPageElements.SelectNodes("//div[@class='blogTags']//a");
            if (genreNodes != null)
            {
                Logger.Info($"[SiteHollyRandall] Update genreNodes: {genreNodes.Count}");
                foreach (var genre in genreNodes)
                {
                    Logger.Info($"[SiteHollyRandall] Update genre: {genre.InnerText.Trim()}");
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var dateNode = detailsPageElements.SelectSingleNode("//li[@class='text_med']");
            Logger.Info($"[SiteHollyRandall] Update dateNode: {dateNode.InnerText.Trim()}");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var actorNodes = detailsPageElements.SelectNodes("//p[@class='link_light']/a");
            if (actorNodes != null)
            {
                Logger.Info($"[SiteHollyRandall] Update actorNodes: {actorNodes.Count}");
                foreach (var actor in actorNodes)
                {
                    Logger.Info($"[SiteHollyRandall] Update actor: {actor.InnerText.Trim()}");
                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actor.InnerText.Trim(), Type = PersonKind.Actor });
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
