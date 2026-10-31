using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
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
    public class SiteDorcelClub : IProviderBase
    {
        // Images come from <source data-srcset>; the hash in the file name is a required CDN signature,
        // removing it makes the CDN answer 500.
        private static readonly Regex ImageSizeRegex = new Regex(@"_(\d{2,4})_(\d{2,4})_", RegexOptions.Compiled);

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();

            // Site search ANDs the words, so the full SEO title returns nothing;
            // search the last four words, which are the title.
            searchTitle = Helper.GetSearchTitle(searchTitle, 4, fromEnd: true);

            string searchUrl = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(searchTitle);
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var searchPageElements = HTML.ElementFromString(httpResult.Content);
            var sceneNodes = searchPageElements.SelectNodes("//div[@class='scenes list']/div[@class='items']/div[@class='scene thumbnail ']");
            if (sceneNodes != null)
            {
                foreach (var node in sceneNodes)
                {
                    var titleNode = node.SelectSingleNode(".//div[@class='textual']/a");
                    string titleNoFormatting = titleNode?.InnerText.Trim();
                    string curId = Helper.Encode(titleNode?.GetAttributeValue("href", string.Empty));
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curId } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}]",
                        ImageUrl = GetLargestImage(node),
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }
            }

            var movieNodes = searchPageElements.SelectNodes("//div[@class='movies list']/div[@class='items']/a[@class='movie thumbnail']");
            if (movieNodes != null)
            {
                foreach (var node in movieNodes)
                {
                    string titleNoFormatting = node.SelectSingleNode("./h2")?.InnerText.Trim();
                    string movieLink = node.GetAttributeValue("href", string.Empty);
                    string curId = Helper.Encode(movieLink);
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curId } },
                        Name = $"{titleNoFormatting} - Full Movie [{Helper.GetSearchSiteName(siteNum)}]",
                        ImageUrl = GetLargestImage(node),
                        SearchProviderName = Plugin.Instance.Name,
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
            movie.Name = detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//span[@class='full']")?.InnerText.Trim();
            movie.AddStudio("Marc Dorcel");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddStudio(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//span[@class='publish_date']") ?? detailsPageElements.SelectSingleNode("//span[@class='out_date']");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Replace("Year :", string.Empty).Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            movie.AddGenre("French porn");
            var movieNameNode = detailsPageElements.SelectSingleNode("//span[@class='movie']/a");
            if (movieNameNode != null)
            {
                movie.AddGenre("Blockbuster Movie");
            }

            var actorNodes = detailsPageElements.SelectNodes("//div[@class='actress']/a") ?? detailsPageElements.SelectNodes("//div[@class='actor thumbnail ']/a/div[@class='name']");
            if (actorNodes != null)
            {
                if (!sceneUrl.Contains("porn-movie"))
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
                }

                foreach (var actor in actorNodes)
                {
                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actor.InnerText.Trim(), Type = PersonKind.Actor });
                }
            }

            var directorNode = detailsPageElements.SelectSingleNode("//span[@class='director']");
            if (directorNode != null)
            {
                ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = directorNode.InnerText.Replace("Director :", string.Empty).Trim(), Type = PersonKind.Director });
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

            // Main cover: the largest image in player_container, used as Primary.
            var coverUrl = GetLargestImage(detailsPageElements.SelectSingleNode("//div[contains(@class, 'player_container')]"));

            // Gallery photos exist in six sizes; keep the widest to avoid duplicates.
            var photoUrls = new List<string>();
            var photoNodes = detailsPageElements.SelectNodes("//div[contains(@class, 'photos')]//a[contains(@class, 'photo')]");
            if (photoNodes != null)
            {
                foreach (var photo in photoNodes)
                {
                    var photoUrl = GetLargestImage(photo);
                    if (!string.IsNullOrEmpty(photoUrl) && !photoUrls.Contains(photoUrl))
                    {
                        photoUrls.Add(photoUrl);
                    }
                }
            }

            if (!string.IsNullOrEmpty(coverUrl))
            {
                images.Add(new RemoteImageInfo { Url = coverUrl, Type = ImageType.Primary });
            }

            foreach (var photoUrl in photoUrls)
            {
                // Without a cover the first gallery photo becomes Primary, the rest Backdrops.
                images.Add(new RemoteImageInfo
                {
                    Url = photoUrl,
                    Type = images.Any() ? ImageType.Backdrop : ImageType.Primary,
                });
            }

            return images;
        }

        /// <summary>
        /// Widest image from the node's <source data-srcset>.
        /// The URL must be used verbatim: the hash is a CDN signature and editing it returns 500.
        /// </summary>
        private static string GetLargestImage(HtmlNode node)
        {
            var sources = node?.SelectNodes(".//source");
            if (sources == null)
            {
                return null;
            }

            string bestUrl = null;
            int bestWidth = -1;
            foreach (var source in sources)
            {
                var srcset = source.GetAttributeValue("data-srcset", string.Empty);
                if (string.IsNullOrEmpty(srcset))
                {
                    continue;
                }

                foreach (var candidate in srcset.Split(','))
                {
                    var imageUrl = candidate.Trim().Split(' ').FirstOrDefault();
                    if (string.IsNullOrEmpty(imageUrl) || !imageUrl.StartsWith("http", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var width = GetImageWidth(imageUrl);
                    if (width > bestWidth)
                    {
                        bestWidth = width;
                        bestUrl = imageUrl;
                    }
                }
            }

            return bestUrl;
        }

        /// <summary>Width parsed from a CDN file name; 0 when it cannot be parsed.</summary>
        private static int GetImageWidth(string imageUrl)
        {
            var fileName = imageUrl.Split('?').First().Split('/').Last();
            var match = ImageSizeRegex.Match(fileName);
            return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        }
    }
}
