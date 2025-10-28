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
    public class NetworkFAKings : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{searchTitle.Replace(' ', '-')}";

            var searchUrls = new List<string> { searchUrl, searchUrl.Replace("/en", string.Empty) };

            foreach (var url in searchUrls)
            {
                var httpResult = await HTTP.Request(url, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var searchPageElements = HTML.ElementFromString(httpResult.Content);
                    var searchNodes = searchPageElements.SelectNodes("//div[@class='zona-listado2']");
                    if (searchNodes != null)
                    {
                        foreach (var node in searchNodes)
                        {
                            var titleNode = node.SelectSingleNode(".//h3");
                            string titleNoFormatting = Helper.ParseTitle(titleNode?.InnerText.Trim(), siteNum);
                            string curId = Helper.Encode(node.SelectSingleNode(".//@href")?.GetAttributeValue("href", string.Empty));
                            string subSite = Helper.ParseTitle(node.SelectSingleNode(".//strong/a")?.InnerText.Trim(), siteNum);
                            string releaseDate = string.Empty;
                            var dateNode = node.SelectSingleNode(".//p[@class='txtmininfo calen sinlimite']");
                            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                            {
                                releaseDate = parsedDate.ToString("yyyy-MM-dd");
                            }

                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, $"{curId}|{releaseDate}" } },
                                Name = $"{titleNoFormatting.Substring(0, 20)}... [FAKings/{subSite}] {releaseDate}",
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
            string sceneDate = providerIds.Length > 1 ? providerIds[1] : null;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.Name = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim(), siteNum);
            movie.Overview = detailsPageElements.SelectSingleNode("//span[@class='grisoscuro']")?.InnerText.Trim();
            movie.AddStudio("FAKings");

            string tagline = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//strong[contains(., 'Serie')]//following-sibling::a")?.InnerText.Trim(), siteNum);
            movie.AddTag(tagline);

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//strong[contains(., 'Categori')]//following-sibling::a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//strong[contains(., 'Actr')]//following-sibling::a");
            if (actorNodes != null)
            {
                foreach (var actor in actorNodes)
                {
                    string actorName = actor.InnerText.Trim();
                    string modelUrl = actor.GetAttributeValue("href", string.Empty);
                    string actorPhotoUrl = string.Empty;
                    var actorHttp = await HTTP.Request(modelUrl, HttpMethod.Get, cancellationToken);
                    if (actorHttp.IsOK)
                    {
                        var actorPage = HTML.ElementFromString(actorHttp.Content);
                        actorPhotoUrl = actorPage.SelectSingleNode("//div[@class='zona-imagen']//img")?.GetAttributeValue("src", string.Empty).Trim();
                    }

                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);

            var actorNodes = (await HTML.ElementFromURL(sceneUrl, cancellationToken))?.SelectNodes("//strong[contains(., 'Actr')]//following-sibling::a");
            if (actorNodes != null)
            {
                foreach (var actor in actorNodes)
                {
                    string modelUrl = actor.GetAttributeValue("href", string.Empty);
                    var actorHttp = await HTTP.Request(modelUrl, HttpMethod.Get, cancellationToken);
                    if (actorHttp.IsOK)
                    {
                        var actorPage = HTML.ElementFromString(actorHttp.Content);
                        var sceneNode = actorPage.SelectNodes("//div[@class='zona-listado2']")?.FirstOrDefault(s => s.SelectSingleNode(".//@href")?.GetAttributeValue("href", string.Empty) == sceneUrl);
                        if (sceneNode != null)
                        {
                            images.Add(new RemoteImageInfo { Url = sceneNode.SelectSingleNode(".//img")?.GetAttributeValue("src", string.Empty).Trim() });
                        }
                    }
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
