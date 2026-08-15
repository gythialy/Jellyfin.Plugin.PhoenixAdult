using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
using Jellyfin.Data.Enums;

namespace PhoenixAdult.Sites
{
    public class NetworkNubiles : IProviderBase
    {
        private static readonly IDictionary<string, string> Headers = new Dictionary<string, string>
        {
            { "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8" },
            { "Accept-Language", "en-US,en;q=0.9" },
            { "Sec-Ch-Ua", "\"Microsoft Edge\";v=\"107\", \"Chromium\";v=\"107\", \"Not=A?Brand\";v=\"24\"" },
            { "Sec-Ch-Ua-Mobile", "?0" },
            { "Sec-Ch-Ua-Platform", "\"Windows\"" },
            { "Sec-Fetch-Dest", "document" },
            { "Sec-Fetch-Mode", "navigate" },
            { "Sec-Fetch-Site", "none" },
            { "Sec-Fetch-User", "?1" },
            { "Upgrade-Insecure-Requests", "1" },
        };

        private static async Task<IDictionary<string, string>> GetCookies(int[] siteNum, CancellationToken cancellationToken)
        {
            var cookies = new Dictionary<string, string> { { "18-plus-modal", "hidden" } };
            var verifiedCookies = await CaptchaHelper.NCookies(siteNum, cancellationToken).ConfigureAwait(false);
            if (verifiedCookies != null)
            {
                foreach (var kvp in verifiedCookies)
                {
                    cookies[kvp.Key] = kvp.Value;
                }
            }

            return cookies;
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            var cookies = await GetCookies(siteNum, cancellationToken).ConfigureAwait(false);

            if (searchDate.HasValue)
            {
                var url = $"{Helper.GetSearchSearchURL(siteNum)}date/{searchDate.Value:yyyy-MM-dd}/{searchDate.Value:yyyy-MM-dd}";
                var data = await HTML.ElementFromURL(url, cancellationToken, Headers, cookies).ConfigureAwait(false);
                if (data == null)
                {
                    return result;
                }

                var searchResults = data.SelectNodes("//div[contains(@class, 'content-grid-item')]");
                if (searchResults != null)
                {
                    foreach (var searchResult in searchResults)
                    {
                        var titleParts = searchResult.SelectSingleNode(".//span[@class='title']/a")?.InnerText.Split('-');
                        string titleNoFormatting = titleParts != null && titleParts.Length > 1 ? $"{titleParts[0].Trim()} - {titleParts[1].Trim()}" : titleParts?[0].Trim() ?? string.Empty;
                        string curID = searchResult.SelectSingleNode(".//span[@class='title']/a")?.GetAttributeValue("href", string.Empty).Split('/')[3];
                        var dateStr = searchResult.SelectSingleNode(".//span[@class='date']")?.InnerText.Trim();
                        string releaseDate = DateTime.TryParse(dateStr, out var parsedDate) ? parsedDate.ToString("yyyy-MM-dd") : string.Empty;
                        var poster = searchResult.SelectSingleNode(".//picture//img")?.GetAttributeValue("src", string.Empty);

                        if (!string.IsNullOrEmpty(titleNoFormatting))
                        {
                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, curID } },
                                Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}".Trim(),
                                SearchProviderName = Plugin.Instance.Name,
                                ImageUrl = poster,
                            });
                        }
                    }
                }
            }
            else if (int.TryParse(searchTitle.Split(' ')[0], out var sceneNum))
            {
                var url = $"{Helper.GetSearchBaseURL(siteNum)}/video/watch/{sceneNum}";
                var detailsPageElements = await HTML.ElementFromURL(url, cancellationToken, Headers, cookies).ConfigureAwait(false);
                if (detailsPageElements != null)
                {
                    var titleNode = detailsPageElements.SelectSingleNode("//div[contains(@class, 'content-pane-title')]//h2")
                        ?? detailsPageElements.SelectSingleNode("//h2");
                    string titleNoFormatting = string.Empty;
                    if (titleNode != null)
                    {
                        var titleParts = titleNode.InnerText.Trim().Split('-');
                        titleNoFormatting = titleParts.Length > 1 ? $"{Helper.ParseTitle(titleParts[0].Trim(), siteNum)} - {titleParts[1].Trim()}" : Helper.ParseTitle(titleParts[0].Trim(), siteNum);
                    }

                    var dateNode = detailsPageElements.SelectSingleNode("//div[contains(@class, 'content-pane')]//span[@class='date']")
                        ?? detailsPageElements.SelectSingleNode("//span[@class='date']");
                    string releaseDate = string.Empty;
                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    var posterNode = detailsPageElements.SelectSingleNode("//video")
                        ?? detailsPageElements.SelectSingleNode("//picture//img")
                        ?? detailsPageElements.SelectSingleNode("//img[contains(@class, 'cover')]");
                    var poster = posterNode?.GetAttributeValue("poster", string.Empty);
                    if (string.IsNullOrEmpty(poster))
                    {
                        poster = posterNode?.GetAttributeValue("src", string.Empty);
                    }

                    if (!string.IsNullOrEmpty(poster) && !poster.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        poster = "https:" + poster;
                    }

                    if (!string.IsNullOrEmpty(titleNoFormatting))
                    {
                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, $"{sceneNum}" } },
                            Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}".Trim(),
                            SearchProviderName = Plugin.Instance.Name,
                            ImageUrl = poster,
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

            var cookies = await GetCookies(siteNum, cancellationToken).ConfigureAwait(false);
            string sceneURL = $"{Helper.GetSearchBaseURL(siteNum)}/video/watch/{sceneID[0]}";
            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken, Headers, cookies).ConfigureAwait(false);
            if (sceneData == null)
            {
                return result;
            }

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneURL;
            var titleNode = sceneData.SelectSingleNode("//div[contains(@class, 'content-pane-title')]//h2")
                ?? sceneData.SelectSingleNode("//h2");
            if (titleNode != null)
            {
                var titleParts = titleNode.InnerText.Trim().Split('-');
                movie.Name = titleParts.Length > 1 ? $"{Helper.ParseTitle(titleParts[0].Trim(), siteNum)} - {titleParts[1].Trim()}" : Helper.ParseTitle(titleParts[0].Trim(), siteNum);
            }

            var descriptionNode = sceneData.SelectSingleNode("//div[contains(@class, 'content-pane-column')]/div")
                ?? sceneData.SelectSingleNode("//div[contains(@class, 'content-pane-description')]");
            string description = descriptionNode?.InnerText;
            if (string.IsNullOrEmpty(description))
            {
                var paragraphs = sceneData.SelectNodes("//div[contains(@class, 'content-pane-column')]//p");
                if (paragraphs != null)
                {
                    description = string.Join("\n\n", paragraphs.Select(p => p.InnerText.Trim()));
                }
            }

            movie.Overview = description?.Trim();

            movie.AddStudio("Nubiles");
            movie.AddStudio(Helper.GetSearchSiteName(siteNum));

            var sceneDateNode = sceneData.SelectSingleNode("//div[contains(@class, 'content-pane')]//span[@class='date']")
                ?? sceneData.SelectSingleNode("//span[@class='date']");
            if (sceneDateNode != null && DateTime.TryParse(sceneDateNode.InnerText.Trim(), out var sceneDateObj))
            {
                movie.PremiereDate = sceneDateObj;
                movie.ProductionYear = sceneDateObj.Year;
            }

            var genreNode = sceneData.SelectNodes("//div[contains(@class, 'categories')]/a | //div[contains(@class, 'categories')]//a");
            if (genreNode != null)
            {
                foreach (var genreLink in genreNode)
                {
                    string genreName = genreLink.InnerText.Trim();
                    if (!string.IsNullOrEmpty(genreName))
                    {
                        movie.AddGenre(genreName);
                    }
                }
            }

            var actorsNode = sceneData.SelectNodes("//div[contains(@class, 'content-pane-performers')]//a | //div[contains(@class, 'content-pane-performer')]//a | //a[contains(@class, 'content-pane-performer')]");
            if (actorsNode != null)
            {
                foreach (var actorLink in actorsNode)
                {
                    string actorName = actorLink.InnerText.Trim();
                    if (string.IsNullOrEmpty(actorName))
                    {
                        continue;
                    }

                    string actorHref = actorLink.GetAttributeValue("href", string.Empty);
                    string actorPhotoURL = string.Empty;
                    if (!string.IsNullOrEmpty(actorHref))
                    {
                        string actorPageURL = actorHref.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? actorHref : Helper.GetSearchBaseURL(siteNum) + actorHref;
                        var actorPage = await HTML.ElementFromURL(actorPageURL, cancellationToken, Headers, cookies).ConfigureAwait(false);
                        var actorImgNode = actorPage?.SelectSingleNode("//div[contains(@class, 'model-profile')]//img")
                            ?? actorPage?.SelectSingleNode("//img[contains(@class, 'model-profile')]");
                        var imgSrc = actorImgNode?.GetAttributeValue("src", string.Empty);
                        if (!string.IsNullOrEmpty(imgSrc))
                        {
                            actorPhotoURL = imgSrc.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? imgSrc : "https:" + imgSrc;
                        }
                    }

                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, ImageUrl = actorPhotoURL, Type = PersonKind.Actor });
                }
            }

            // Add male actors from summary
            if (!string.IsNullOrEmpty(movie.Overview))
            {
                var maleActors = new[] { "Logan Long", "Patrick Delphia", "Seth Gamble", "Alex D.", "Lucas Frost", "Van Wylde", "Tyler Nixon", "Logan Pierce", "Johnny Castle", "Damon Dice", "Scott Carousel", "Dylan Snow", "Michael Vegas", "Xander Corvus", "Chad White" };
                foreach (var actor in maleActors)
                {
                    if (movie.Overview.Contains(actor, StringComparison.OrdinalIgnoreCase))
                    {
                        ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actor, Type = PersonKind.Actor });
                    }
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            var cookies = await GetCookies(siteNum, cancellationToken).ConfigureAwait(false);
            string sceneURL = $"{Helper.GetSearchBaseURL(siteNum)}/video/watch/{sceneID[0]}";
            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken, Headers, cookies).ConfigureAwait(false);
            if (sceneData == null)
            {
                return result;
            }

            var posterNode = sceneData.SelectSingleNode("//video")
                ?? sceneData.SelectSingleNode("//picture//img")
                ?? sceneData.SelectSingleNode("//img[contains(@class, 'cover')]");
            var poster = posterNode?.GetAttributeValue("poster", string.Empty);
            if (string.IsNullOrEmpty(poster))
            {
                poster = posterNode?.GetAttributeValue("src", string.Empty);
            }

            if (!string.IsNullOrEmpty(poster) && !poster.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                poster = "https:" + poster;
            }

            if (!string.IsNullOrEmpty(poster))
            {
                result.Add(new RemoteImageInfo { Url = poster, Type = ImageType.Primary });
            }

            string galleryURL = string.Empty;
            var photoLink = sceneData.SelectSingleNode("//div[contains(@class, 'content-pane-related-links')]//a[contains(., 'Pic')] | //a[contains(@class, 'related-link')][contains(., 'Pic')]");
            if (photoLink != null)
            {
                var href = photoLink.GetAttributeValue("href", string.Empty);
                galleryURL = href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? href : Helper.GetSearchBaseURL(siteNum) + href;
            }
            else if (!string.IsNullOrEmpty(poster))
            {
                var match = new Regex(@"(?<=videos\/).*(?=\/sample)").Match(poster);
                if (match.Success)
                {
                    galleryURL = $"{Helper.GetSearchBaseURL(siteNum)}/galleries/{match.Value}/screenshots";
                }
            }

            if (!string.IsNullOrEmpty(galleryURL))
            {
                var photoPage = await HTML.ElementFromURL(galleryURL, cancellationToken, Headers, cookies).ConfigureAwait(false);
                if (photoPage != null)
                {
                    var sceneImages = photoPage.SelectNodes("//div[@class='img-wrapper']//picture/source[1] | //div[contains(@class, 'img-wrapper')]//img");
                    if (sceneImages != null)
                    {
                        foreach (var sceneImage in sceneImages)
                        {
                            string posterURL = sceneImage.GetAttributeValue("srcset", string.Empty);
                            if (string.IsNullOrEmpty(posterURL))
                            {
                                posterURL = sceneImage.GetAttributeValue("src", string.Empty);
                            }

                            if (!string.IsNullOrEmpty(posterURL))
                            {
                                if (!posterURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                                {
                                    posterURL = "https:" + posterURL;
                                }

                                result.Add(new RemoteImageInfo { Url = posterURL, Type = ImageType.Backdrop });
                            }
                        }
                    }
                }
            }

            return result;
        }
    }
}
