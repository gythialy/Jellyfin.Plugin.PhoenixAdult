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
    public class NetworkFullPornNetwork : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var modelResultsUrls = new List<string>();
            var searchResultsUrls = new List<string>();
            var googleResults = await GoogleSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);

            string directSearch = searchTitle.Contains(" ") ? searchTitle.Replace(' ', '-').ToLower() : searchTitle;
            modelResultsUrls.Add($"{Helper.GetSearchBaseURL(siteNum)}/models/{directSearch}.html");

            foreach (var modelResultUrl in googleResults)
            {
                if (!modelResultsUrls.Contains(modelResultUrl) && modelResultUrl.Contains("/models/") && !modelResultUrl.Contains("models_") && !modelResultUrl.Contains("join"))
                {
                    modelResultsUrls.Add(modelResultUrl);
                }
            }

            foreach (var searchResultUrl in googleResults)
            {
                if (!searchResultsUrls.Contains(searchResultUrl) && searchResultUrl.Contains("/trailers/"))
                {
                    searchResultsUrls.Add(searchResultUrl);
                }
            }

            foreach (var sceneUrl in searchResultsUrls)
            {
                var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var detailsPageElements = HTML.ElementFromString(httpResult.Content);
                    string titleNoFormatting = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//h1[contains(@class, 'title_bar')]")?.InnerText.Split(':').Last().Trim());
                    string curId = Helper.Encode(sceneUrl);
                    string releaseDate = string.Empty;
                    var dateNode = detailsPageElements.SelectSingleNode("//div[@class='video-info']//p");
                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }
                    else if (searchDate.HasValue)
                    {
                        releaseDate = searchDate.Value.ToString("yyyy-MM-dd");
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}" } },
                        Name = $"{titleNoFormatting} [FPN/{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }
            }

            foreach (var modelUrl in modelResultsUrls)
            {
                var httpResult = await HTTP.Request(modelUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var modelPageElements = HTML.ElementFromString(httpResult.Content);
                    var sceneNodes = modelPageElements.SelectNodes("//div[contains(@class, 'latest-updates')]//div[@data-setid]");
                    if (sceneNodes != null)
                    {
                        foreach (var sceneNode in sceneNodes)
                        {
                            string sceneLink = sceneNode.SelectSingleNode(".//a[@class='updateimg']")?.GetAttributeValue("href", string.Empty);
                            if (!searchResultsUrls.Contains(sceneLink))
                            {
                                string titleNoFormatting = sceneNode.InnerText.Split(':').Last().Trim();
                                string curId = Helper.Encode(sceneLink);
                                string releaseDate = searchDate?.ToString("yyyy-MM-dd") ?? string.Empty;
                                result.Add(new RemoteSearchResult
                                {
                                    ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}" } },
                                    Name = $"{titleNoFormatting} [FPN/{Helper.GetSearchSiteName(siteNum)}]",
                                    SearchProviderName = Plugin.Instance.Name,
                                });
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

            string sceneDate = providerIds.Length > 2 ? providerIds[2] : null;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.Name = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//h1[contains(@class, 'title_bar')]")?.InnerText.Split(':').Last().Trim());
            movie.Overview = detailsPageElements.SelectSingleNode("//div[contains(@class, 'video-description')]/p[@class='description-text']")?.InnerText.Trim();
            movie.AddStudio("Full Porn Network");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//div[@class='video-info']//p");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }
            else if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//div[contains(@class, 'video-info')]//a[contains(@href, '/categories/')]");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//div[contains(@class, 'video-info')]//a[contains(@href, '/models/')]");
            if (actorNodes != null)
            {
                foreach (var actor in actorNodes)
                {
                    string actorName = actor.InnerText.Trim();
                    string actorLink = actor.GetAttributeValue("href", string.Empty);
                    var actorHttp = await HTTP.Request(actorLink, HttpMethod.Get, cancellationToken);
                    if (actorHttp.IsOK)
                    {
                        var actorPage = HTML.ElementFromString(actorHttp.Content);
                        string actorPhotoUrl = actorPage.SelectSingleNode("//img[@alt='model']")?.GetAttributeValue("src0_3x", string.Empty);
                        result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                    }
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

            var posterNode = detailsPageElements.SelectSingleNode("//video");
            if (posterNode != null)
            {
                string imageUrl = posterNode.GetAttributeValue("poster", string.Empty);
                if (!imageUrl.StartsWith("http"))
                {
                    imageUrl = Helper.GetSearchBaseURL(siteNum) + imageUrl;
                }

                images.Add(new RemoteImageInfo { Url = imageUrl.Replace("-1x.jpg", "-3x.jpg"), Type = ImageType.Primary });
            }

            return images;
        }
    }
}
