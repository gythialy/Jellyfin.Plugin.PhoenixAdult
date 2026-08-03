using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Jellyfin.Data.Enums;
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
    public class NetworkNubiles : IProviderBase
    {
        private readonly IDictionary<string, string> _cookies = new Dictionary<string, string> { { "18-plus-modal", "hidden" } };
        private readonly IDictionary<string, string> _headers = new Dictionary<string, string> { { "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8" } };

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            if (searchDate.HasValue)
            {
                var url = $"{Helper.GetSearchSearchURL(siteNum)}date/{searchDate.Value:yyyy-MM-dd}/{searchDate.Value:yyyy-MM-dd}";
                var data = await HTML.ElementFromURL(url, cancellationToken, _headers, _cookies);
                if (data == null)
                {
                    return result;
                }

                var searchResults = data.SelectNodes("//div[contains(@class, 'content-grid-item')]");
                if (searchResults != null)
                {
                    foreach (var searchResult in searchResults)
                    {
                        var titleLink = searchResult.SelectSingleNode(".//span[@class='title']/a");
                        if (titleLink == null)
                        {
                            continue;
                        }

                        string rawTitle = titleLink.InnerText.Trim();
                        var titleParts = rawTitle.Split('-');
                        string titleNoFormatting = titleParts.Length > 1 ? $"{titleParts[0].Trim()} - {titleParts[1].Trim()}" : titleParts[0].Trim();

                        string href = titleLink.GetAttributeValue("href", string.Empty);
                        var hrefParts = href.Split('/');
                        string curID = hrefParts.Length > 3 ? hrefParts[3] : href;

                        var dateNode = searchResult.SelectSingleNode(".//span[@class='date']");
                        string releaseDate = dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate) ? parsedDate.ToString("yyyy-MM-dd") : string.Empty;
                        var poster = searchResult.SelectSingleNode(".//picture//img")?.GetAttributeValue("src", string.Empty);

                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, curID } },
                            Name = string.IsNullOrEmpty(releaseDate)
                                ? $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}]"
                                : $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                            SearchProviderName = Plugin.Instance.Name,
                            ImageUrl = poster,
                        });
                    }
                }
            }
            else if (int.TryParse(searchTitle.Split(' ')[0], out var sceneNum))
            {
                var url = $"{Helper.GetSearchBaseURL(siteNum)}/video/watch/{sceneNum}";
                var detailsPageElements = await HTML.ElementFromURL(url, cancellationToken, _headers, _cookies);
                if (detailsPageElements != null)
                {
                    var titleNode = detailsPageElements.SelectSingleNode("//div[contains(@class, 'content-pane-title')]//h2 | //div[contains(@class, 'content-pane-title')]//h1 | //h1 | //h2 | //title");
                    string titleNoFormatting = titleNode?.InnerText.Trim();
                    var dateNode = detailsPageElements.SelectSingleNode("//div[contains(@class, 'content-pane')]//span[@class='date'] | //span[@class='date']");
                    string releaseDate = dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate) ? parsedDate.ToString("yyyy-MM-dd") : string.Empty;
                    var posterNode = detailsPageElements.SelectSingleNode("//video");
                    var poster = posterNode?.GetAttributeValue("poster", string.Empty);
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{sceneNum}" } },
                        Name = string.IsNullOrEmpty(releaseDate)
                            ? $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}]"
                            : $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name,
                        ImageUrl = poster,
                    });
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

            if (siteNum == null || sceneID == null || sceneID.Length == 0 || string.IsNullOrEmpty(sceneID[0]))
            {
                return result;
            }

            string sceneURL;
            if (sceneID[0].StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = sceneID[0];
            }
            else
            {
                string decoded = Helper.Decode(sceneID[0]);
                if (decoded.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    sceneURL = decoded;
                }
                else
                {
                    sceneURL = $"{Helper.GetSearchBaseURL(siteNum)}/video/watch/{sceneID[0]}";
                }
            }

            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken, _headers, _cookies);
            if (sceneData == null)
            {
                return result;
            }

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneURL;

            var titleNode = sceneData.SelectSingleNode("//div[contains(@class, 'content-pane-title')]//h2 | //div[contains(@class, 'content-pane-title')]//h1 | //h1 | //h2 | //title");
            if (titleNode != null && !string.IsNullOrWhiteSpace(titleNode.InnerText))
            {
                var titleParts = titleNode.InnerText.Trim().Split('-');
                movie.Name = titleParts.Length > 1 ? $"{titleParts[0].Trim()} - {titleParts[1].Trim()}" : titleParts[0].Trim();
            }

            var descriptionNode = sceneData.SelectSingleNode("//div[@class='col-12 content-pane-column']/div | //div[contains(@class, 'content-pane-column')]/div | //div[contains(@class, 'content-pane-column')]");
            string description = descriptionNode?.InnerText;
            if (string.IsNullOrWhiteSpace(description))
            {
                var paragraphs = sceneData.SelectNodes("//div[contains(@class, 'content-pane-column')]//p | //div[contains(@class, 'content-pane')]//p");
                if (paragraphs != null)
                {
                    description = string.Join("\n\n", paragraphs.Select(p => p.InnerText.Trim()).Where(p => !string.IsNullOrWhiteSpace(p)));
                }
            }

            movie.Overview = description?.Trim();

            movie.AddStudio("Nubiles");
            movie.AddStudio(Helper.GetSearchSiteName(siteNum));

            var sceneDateNode = sceneData.SelectSingleNode("//div[contains(@class, 'content-pane')]//span[@class='date'] | //span[@class='date']");
            if (sceneDateNode != null && DateTime.TryParse(sceneDateNode.InnerText.Trim(), out var sceneDateObj))
            {
                movie.PremiereDate = sceneDateObj;
                movie.ProductionYear = sceneDateObj.Year;
            }

            var genreNode = sceneData.SelectNodes("//div[@class='categories']/a | //div[contains(@class, 'categories')]/a");
            if (genreNode != null)
            {
                foreach (var genreLink in genreNode)
                {
                    string genre = genreLink.InnerText.Trim();
                    if (!string.IsNullOrEmpty(genre))
                    {
                        movie.AddGenre(genre);
                    }
                }
            }

            var actorsNode = sceneData.SelectNodes("//div[contains(@class, 'content-pane-performer')]/a | //div[contains(@class, 'performers')]/a | //div[contains(@class, 'models')]/a");
            if (actorsNode != null)
            {
                foreach (var actorLink in actorsNode)
                {
                    string actorName = actorLink.InnerText.Trim();
                    if (string.IsNullOrEmpty(actorName))
                    {
                        continue;
                    }

                    string actorPhotoURL = null;
                    string actorHref = actorLink.GetAttributeValue("href", string.Empty);
                    if (!string.IsNullOrEmpty(actorHref))
                    {
                        string actorPageURL = actorHref.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                            ? actorHref
                            : Helper.GetSearchBaseURL(siteNum) + actorHref;
                        var actorPage = await HTML.ElementFromURL(actorPageURL, cancellationToken, _headers, _cookies);
                        var actorImgNode = actorPage?.SelectSingleNode("//div[contains(@class, 'model-profile')]//img | //div[contains(@class, 'profile')]//img");
                        if (actorImgNode != null)
                        {
                            string src = actorImgNode.GetAttributeValue("src", string.Empty);
                            if (!string.IsNullOrEmpty(src))
                            {
                                actorPhotoURL = src.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? src : "http:" + src;
                            }
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
            if (siteNum == null || sceneID == null || sceneID.Length == 0 || string.IsNullOrEmpty(sceneID[0]))
            {
                return result;
            }

            string sceneURL;
            if (sceneID[0].StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = sceneID[0];
            }
            else
            {
                string decoded = Helper.Decode(sceneID[0]);
                if (decoded.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    sceneURL = decoded;
                }
                else
                {
                    sceneURL = $"{Helper.GetSearchBaseURL(siteNum)}/video/watch/{sceneID[0]}";
                }
            }

            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken, _headers, _cookies);
            if (sceneData == null)
            {
                return result;
            }

            var posterNode = sceneData.SelectSingleNode("//video");
            string poster = posterNode?.GetAttributeValue("poster", string.Empty);
            if (string.IsNullOrEmpty(poster))
            {
                var posterAttrNode = sceneData.SelectSingleNode("//video/@poster");
                poster = posterAttrNode?.GetAttributeValue("poster", string.Empty);
                if (string.IsNullOrEmpty(poster))
                {
                    poster = posterAttrNode?.InnerText;
                }
            }

            if (!string.IsNullOrEmpty(poster) && !poster.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                poster = "http:" + poster;
            }

            if (!string.IsNullOrEmpty(poster) && poster != "http:")
            {
                result.Add(new RemoteImageInfo { Url = poster, Type = ImageType.Primary });
            }

            string galleryURL = string.Empty;
            var photoLink = sceneData.SelectSingleNode("//div[contains(@class, 'content-pane-related-links')]/a[contains(., 'Pic')]");
            if (photoLink != null)
            {
                galleryURL = Helper.GetSearchBaseURL(siteNum) + photoLink.GetAttributeValue("href", string.Empty);
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
                var photoPage = await HTML.ElementFromURL(galleryURL, cancellationToken, _headers, _cookies);
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
                                    posterURL = "http:" + posterURL;
                                }

                                if (posterURL != "http:")
                                {
                                    result.Add(new RemoteImageInfo { Url = posterURL, Type = ImageType.Backdrop });
                                }
                            }
                        }
                    }
                }
            }

            return result;
        }
    }
}

