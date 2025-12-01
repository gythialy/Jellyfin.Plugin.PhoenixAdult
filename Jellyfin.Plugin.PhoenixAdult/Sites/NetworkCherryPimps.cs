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
    public class NetworkCherryPimps : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            for (int page = 1; page < 3; page++)
            {
                string searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{searchTitle.Replace(" ", "+")}&page={page}";
                var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
                if (!httpResult.IsOK)
                {
                    continue;
                }

                var searchPageElements = HTML.ElementFromString(httpResult.Content);
                var searchNodes = searchPageElements.SelectNodes("//div[contains(@class, 'video-thumb') or contains(@class, 'item-video')]");
                if (searchNodes != null)
                {
                    foreach (var node in searchNodes)
                    {
                        var titleNode = node.SelectSingleNode("(.//p[@class='text-thumb'] | .//div[@class='item-title'])/a");
                        string titleNoFormatting = Helper.ParseTitle(titleNode?.InnerText.Trim(), siteNum);
                        string curId = Helper.Encode(titleNode?.GetAttributeValue("href", string.Empty));
                        string subSite = node.SelectSingleNode(".//p[@class='text-thumb']/a[@class='badge'] | .//div[@class='item-sitename']/a")?.InnerText.Trim();
                        var dateNode = node.SelectSingleNode(".//span[@class='date'] | .//div[@class='item-date']");
                        string releaseDate = string.Empty;
                        if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Split('|').Last().Trim(), out var parsedDate))
                        {
                            releaseDate = parsedDate.ToString("yyyy-MM-dd");
                        }

                        var actorNodes = node.SelectNodes("(.//span[@class='category'] | .//div[@class='item-models'])//a");
                        string actorNames = string.Join(", ", actorNodes?.Select(a => a.InnerText.Trim()) ?? new string[0]);

                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, curId } },
                            Name = $"{actorNames} in {titleNoFormatting} [CherryPimps/{subSite}] {releaseDate}",
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
            movie.Name = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//*[@class='trailer-block_title'] | //h1")?.InnerText.Trim(), siteNum);
            movie.Overview = detailsPageElements.SelectSingleNode("//div[@class='info-block']//p[@class='text'] | //div[@class='update-info-block']//p")?.InnerText.Trim();
            movie.AddStudio("Cherry Pimps");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddStudio(tagline);
            movie.AddCollection(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//div[@class='info-block_data']//p[@class='text'] | //div[@class='update-info-row']");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Split('|')[0].Replace("Added", string.Empty).Replace(":", string.Empty).Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//div[@class='info-block']//a | //ul[@class='tags']//a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//div[@class='info-block_data']//a | //div[contains(@class, 'model-list-item')]//a");
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

                foreach (var actorNode in actorNodes)
                {
                    string actorName = actorNode.InnerText.Trim();
                    if (string.IsNullOrEmpty(actorName))
                    {
                        actorName = actorNode.SelectSingleNode("//span")?.InnerText.Trim();
                    }

                    string actorPhotoUrl = actorNode.SelectSingleNode("//img")?.GetAttributeValue("src0_1x", string.Empty);
                    if (string.IsNullOrEmpty(actorPhotoUrl))
                    {
                        string actorPageUrl = actorNode.GetAttributeValue("href", string.Empty);
                        if (!actorPageUrl.StartsWith("http"))
                        {
                            actorPageUrl = Helper.GetSearchBaseURL(siteNum) + "/" + actorPageUrl;
                        }

                        var actorHttp = await HTTP.Request(actorPageUrl, HttpMethod.Get, cancellationToken);
                        if (actorHttp.IsOK)
                        {
                            var actorPage = HTML.ElementFromString(actorHttp.Content);
                            var imgNode = actorPage.SelectSingleNode("//img[contains(@class, 'model_bio_thumb')]");
                            actorPhotoUrl = imgNode?.GetAttributeValue("src", string.Empty) ?? imgNode?.GetAttributeValue("src0_1x", string.Empty);
                            if (!string.IsNullOrEmpty(actorPhotoUrl) && !actorPhotoUrl.StartsWith("http"))
                            {
                                actorPhotoUrl = "https:" + actorPhotoUrl;
                            }
                        }
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

            var imageNodes = detailsPageElements.SelectNodes("//img[contains(@class, 'update_thumb')]");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    string imageUrl = img.GetAttributeValue("src", string.Empty) ?? img.GetAttributeValue("src0_1x", string.Empty);
                    if (imageUrl.StartsWith("http"))
                    {
                        images.Add(new RemoteImageInfo { Url = imageUrl });
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
