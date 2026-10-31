using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
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
    public class NetworkJavBus : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null)
            {
                return result;
            }

            string searchJAVID = null;
            string directJAVID = null;
            if (JavId.TryParse(searchTitle, out var javId))
            {
                var javIdParts = JavId.Split(javId);
                directJAVID = javId;
                if (javIdParts.Length == 2)
                {
                    searchJAVID = $"{javIdParts[0]}%2B{javIdParts[1]}";
                }
            }

            string encodedSearchTitle = searchJAVID ?? Uri.EscapeDataString(searchTitle);

            foreach (var searchType in new[] { "Censored", "Uncensored" })
            {
                string url = (searchType == "Uncensored")
                    ? $"{Helper.GetSearchSearchURL(siteNum)}uncensored/search/{encodedSearchTitle}"
                    : $"{Helper.GetSearchSearchURL(siteNum)}search/{encodedSearchTitle}";

                var searchResultsNode = await HTML.ElementFromURL(url, cancellationToken, null, this.GetCookies());
                if (searchResultsNode == null)
                {
                    continue;
                }

                var searchResults = searchResultsNode.SelectNodes("//a[@class='movie-box']");
                if (searchResults == null)
                {
                    continue;
                }

                foreach (var searchResult in searchResults)
                {
                    string titleNoFormatting = ParseSearchTitle(searchResult);
                    string javid = searchResult.SelectSingleNode(".//date[1]")?.InnerText.Trim();
                    string sceneURL = searchResult.GetAttributeValue("href", string.Empty);
                    string curID = Helper.Encode(sceneURL);

                    // <date>[1] is the code, <date>[2] the release date.
                    // PremiereDate drives the dialog date and result ordering.
                    var searchDateValue = searchResult.SelectSingleNode(".//date[2]")?.InnerText.Trim();

                    // MovieProvider adds the site prefix; keep the type and the code here.
                    var searchResultItem = new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = $"[{searchType}] {javid} {titleNoFormatting}",
                        SearchProviderName = Plugin.Instance.Name,
                    };

                    if (!string.IsNullOrEmpty(searchDateValue)
                        && DateTime.TryParseExact(searchDateValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var searchDateObj))
                    {
                        searchResultItem.PremiereDate = searchDateObj;
                        searchResultItem.ProductionYear = searchDateObj.Year;
                    }

                    // The card thumbnail also needs a Referer, so the dialog URL must go through the proxy.
                    var thumbSrc = searchResult.SelectSingleNode(".//div[contains(@class, 'photo-frame')]//img")?.GetAttributeValue("src", string.Empty)
                        ?? searchResult.SelectSingleNode(".//img")?.GetAttributeValue("src", string.Empty);
                    if (!string.IsNullOrEmpty(thumbSrc))
                    {
                        if (thumbSrc.StartsWith("//", StringComparison.Ordinal))
                        {
                            thumbSrc = "https:" + thumbSrc;
                        }
                        else if (!thumbSrc.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            thumbSrc = Helper.GetSearchBaseURL(siteNum) + thumbSrc;
                        }

                        searchResultItem.ImageUrl = ImageProxyUrl.ForBrowser(thumbSrc);
                    }

                    result.Add(searchResultItem);
                }
            }

            // Search occasionally misses codes whose detail page still works: try the detail page,
            // but only when both searches returned nothing and the page really is a title page
            // (it has the info panel or bigImage), or a 404 title would become a result.
            if (!string.IsNullOrEmpty(directJAVID) && result.Count == 0)
            {
                string sceneURL = Helper.GetSearchSearchURL(siteNum) + directJAVID;
                var searchResultNode = await HTML.ElementFromURL(sceneURL, cancellationToken, null, this.GetCookies());
                if (searchResultNode != null
                    && (searchResultNode.SelectSingleNode("//div[@class='col-md-3 info']") != null
                        || searchResultNode.SelectSingleNode("//a[@class='bigImage']") != null))
                {
                    string javTitle = searchResultNode.SelectSingleNode("//head/title")?.InnerText.Trim().Replace(" - JavBus", string.Empty);
                    if (!string.IsNullOrEmpty(javTitle))
                    {
                        string curID = Helper.Encode(sceneURL);
                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, curID } },
                            Name = $"[Direct][{directJAVID}] {javTitle}",
                            SearchProviderName = Plugin.Instance.Name,
                        });
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Clean title of a search card.
        /// The card span also holds a button, line breaks and two date nodes,
        /// which would otherwise repeat the code and date inside the title.
        /// </summary>
        /// <returns>Title collapsed to single spaces.</returns>
        internal static string ParseSearchTitle(HtmlNode searchResult)
        {
            var span = searchResult?.SelectSingleNode(".//span[1]");
            if (span == null)
            {
                return string.Empty;
            }

            var clone = span.CloneNode(true);
            foreach (var node in clone.SelectNodesSafe(".//date|.//button|.//br|.//script"))
            {
                node.Remove();
            }

            var text = Regex.Replace(clone.InnerText.Replace("\t", " ").Replace("\r", " ").Replace("\n", " "), @"\s{2,}", " ").Trim();

            // Drop the leftover "/" between the two date nodes.
            return text.TrimEnd(' ', '/', '\u00a0');
        }

        /// <summary>Parse a card title from its markup, the shape a regression case carries.</summary>
        /// <remarks>
        /// The node overload cannot be used from the test assembly: ILRepack internalizes
        /// HtmlAgilityPack into the plugin assembly, so the merged HtmlNode is a different type and
        /// the call fails with MissingMethodException in release builds.
        /// </remarks>
        /// <param name="cardHtml">Card markup.</param>
        /// <returns>Title collapsed to single spaces, or an empty string.</returns>
        internal static string ParseSearchTitleFromHtml(string cardHtml)
        {
            if (string.IsNullOrWhiteSpace(cardHtml))
            {
                return string.Empty;
            }

            var document = new HtmlDocument();
            document.LoadHtml(cardHtml);
            var card = document.DocumentNode.SelectSingleNode("//a");

            return card == null ? string.Empty : ParseSearchTitle(card);
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem>()
            {
                Item = new Movie(),
                People = new List<PersonInfo>(),
            };

            string sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken, null, this.GetCookies());
            if (detailsPageElements == null)
            {
                return result;
            }

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneURL;
            string javStudio = detailsPageElements.SelectSingleNode("//p/a[contains(@href, '/studio/')]")?.InnerText.Trim();
            string javTitle = detailsPageElements.SelectSingleNode("//head/title")?.InnerText.Trim().Replace(" - JavBus", string.Empty);
            movie.Name = javTitle;
            movie.AddStudio(javStudio);

            string label = detailsPageElements.SelectSingleNode("//p/a[contains(@href, '/label/')]")?.InnerText.Trim();
            string series = detailsPageElements.SelectSingleNode("//p/a[contains(@href, '/series/')]")?.InnerText.Trim();
            if (!string.IsNullOrEmpty(label))
            {
                movie.AddStudio(label);
            }
            else
            {
                movie.AddStudio(javStudio);
            }

            if (!string.IsNullOrEmpty(series))
            {
                movie.AddStudio(series);
            }

            var dateNode = detailsPageElements.SelectSingleNode("//div[@class='col-md-3 info']/p[2]");
            if (dateNode != null)
            {
                // Labels are localised per request language, so read the date itself
                // with a regex instead of keeping a label list per language.
                var dateMatch = Regex.Match(dateNode.InnerText, @"[0-9]{4}-[0-9]{2}-[0-9]{2}");
                if (dateMatch.Success
                    && dateMatch.Value != "0000-00-00"
                    && DateTime.TryParseExact(dateMatch.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                {
                    movie.PremiereDate = parsedDate;
                    movie.ProductionYear = parsedDate.Year;
                }
            }

            var genreLinks = detailsPageElements.SelectNodes("//span[@class='genre']//a[contains(@href, '/genre/')]");
            if (genreLinks != null)
            {
                foreach (var genre in genreLinks)
                {
                    movie.AddGenre(genre.InnerText.ToLower().Trim());
                }
            }

            var actorLinks = detailsPageElements.SelectNodes("//a[@class='avatar-box']");
            if (actorLinks != null)
            {
                foreach (var actorLink in actorLinks)
                {
                    string actorName = actorLink.SelectSingleNode("./div/img")?.GetAttributeValue("title", string.Empty);
                    string actorPhotoURL = actorLink.SelectSingleNode("./div[@class='photo-frame']/img")?.GetAttributeValue("src", string.Empty);
                    if (!string.IsNullOrEmpty(actorPhotoURL) && !actorPhotoURL.StartsWith("http"))
                    {
                        actorPhotoURL = Helper.GetSearchBaseURL(siteNum) + actorPhotoURL;
                    }

                    if (actorPhotoURL?.EndsWith("nowprinting.gif") == true)
                    {
                        actorPhotoURL = string.Empty;
                    }

                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoURL });
                }
            }

            var directorLink = detailsPageElements.SelectSingleNode("//p/a[contains(@href, '/director/')]");
            if (directorLink != null)
            {
                ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = directorLink.InnerText.Trim(), Type = PersonKind.Director });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken, null, this.GetCookies());
            if (detailsPageElements == null)
            {
                return images;
            }

            // Only sample-box links are screenshots; the cover would collide with Primary
            // and be deduplicated away.
            var posterNodes = detailsPageElements.SelectNodes("//a[@class='sample-box' or contains(@href, '/sample/')]");
            if (posterNodes != null)
            {
                foreach (var poster in posterNodes)
                {
                    string posterUrl = poster.GetAttributeValue("href", string.Empty);
                    if (!posterUrl.StartsWith("http"))
                    {
                        posterUrl = Helper.GetSearchBaseURL(siteNum) + posterUrl;
                    }

                    images.Add(new RemoteImageInfo { Url = posterUrl, Type = ImageType.Backdrop });
                }
            }

            var coverImageNode = detailsPageElements.SelectSingleNode("//a[contains(@href, '/cover/')]");
            if (coverImageNode != null)
            {
                string coverImageUrl = coverImageNode.GetAttributeValue("href", string.Empty);

                // Uncensored pages have no thumb directory (the derived path 404s),
                // so the cover itself is used as Primary.
                // Censored pages do have /pics/thumb/<code>.jpg.
                string primaryUrl = coverImageUrl.StartsWith("/imgs/", StringComparison.OrdinalIgnoreCase)
                    ? coverImageUrl
                    : BuildCoverThumbUrl(coverImageUrl);

                if (string.IsNullOrEmpty(primaryUrl))
                {
                    return images;
                }

                if (!primaryUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    primaryUrl = Helper.GetSearchBaseURL(siteNum) + primaryUrl;
                }

                images.Insert(0, new RemoteImageInfo { Url = primaryUrl, Type = ImageType.Primary });
            }

            return images;
        }

        /// <summary>
        /// Derive the thumbnail from the cover URL: /pics/cover/x_b.jpg -> /pics/thumb/x.jpg.
        /// Returns null when the path cannot be derived.
        /// </summary>
        private static string BuildCoverThumbUrl(string coverImageUrl)
        {
            if (string.IsNullOrEmpty(coverImageUrl))
            {
                return null;
            }

            var parts = coverImageUrl.Split('/');
            if (parts.Length < 3)
            {
                return null;
            }

            var coverImageCode = parts[parts.Length - 1].Split('.')[0].Split('_')[0];
            if (string.IsNullOrEmpty(coverImageCode))
            {
                return null;
            }

            var imageHost = string.Join("/", parts.Take(parts.Length - 2));
            var thumbUrl = $"{imageHost}/thumb/{coverImageCode}.jpg";
            if (thumbUrl.Contains("/images."))
            {
                thumbUrl = thumbUrl.Replace("thumb", "thumbs");
            }

            return thumbUrl;
        }

        private IDictionary<string, string> GetCookies()
        {
            // age=verified: without it the search page returns only the age gate.
            return new Dictionary<string, string> { { "existmag", "all" }, { "dv", "1" }, { "age", "verified" } };
        }
    }
}
