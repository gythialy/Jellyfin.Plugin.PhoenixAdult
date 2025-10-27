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
using Newtonsoft.Json.Linq;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class NetworkBang : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var searchResults = new HashSet<string>();

            var googleResults = await WebSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            foreach (var sceneURL in googleResults)
            {
                var url = sceneURL.Split('?')[0];
                if (url.Contains("com/video/") && !url.Contains("index.php/"))
                {
                    searchResults.Add(url);
                }
            }

            var searchPageUrl = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(searchTitle);
            var searchPageHtml = await HTML.ElementFromURL(searchPageUrl, cancellationToken);
            if (searchPageHtml != null)
            {
                var searchResultNodes = searchPageHtml.SelectNodes("//div[contains(@class, 'movie-preview') or contains(@class, 'video_container')]");
                if (searchResultNodes != null)
                {
                    foreach (var searchResult in searchResultNodes)
                    {
                        var sceneUrlNode = searchResult.SelectSingleNode("./a[contains(@class, 'group')]");
                        if (sceneUrlNode == null) continue;

                        string sceneURL = sceneUrlNode.GetAttributeValue("href", string.Empty);
                        string titleNoFormatting = sceneURL.Contains("dvd") ?
                            Helper.ParseTitle(searchResult.SelectSingleNode("./a/div")?.InnerText.Trim(), siteNum) :
                            Helper.ParseTitle(searchResult.SelectSingleNode("./a/span")?.InnerText.Trim(), siteNum);

                        if (!sceneURL.StartsWith("http"))
                        {
                            sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
                        }

                        string curID = Helper.Encode(sceneURL);
                        string releaseDateStr = string.Empty;
                        var dateNode = searchResult.SelectSingleNode(".//span[@class='hidden xs:inline-block truncate']");
                        if (dateNode != null && DateTime.TryParseExact(dateNode.InnerText.Split('•').Last().Trim(), "MMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                        {
                            releaseDateStr = parsedDate.ToString("yyyy-MM-dd");
                        }
                        else if (searchDate.HasValue)
                        {
                            releaseDateStr = searchDate.Value.ToString("yyyy-MM-dd");
                        }

                        if (!searchResults.Contains(sceneURL))
                        {
                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}|{releaseDateStr}" } },
                                Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDateStr}",
                                SearchProviderName = Plugin.Instance.Name,
                            });
                        }
                    }
                }
            }

            foreach (var searchURL in searchResults)
            {
                var detailsPageHtml = await HTML.ElementFromURL(searchURL, cancellationToken);
                if (detailsPageHtml == null)
                {
                    continue;
                }

                var ldJsonNode = detailsPageHtml.SelectSingleNode("//script[@type='application/ld+json'][contains(., 'thumbnail')]");
                if (ldJsonNode == null)
                {
                    continue;
                }

                var videoPageElements = JObject.Parse(ldJsonNode.InnerText.Replace("\n", string.Empty).Trim());

                string titleNoFormatting = Helper.ParseTitle(HTML.Clean(videoPageElements["name"].ToString()), siteNum);
                string curID = Helper.Encode(searchURL);
                string releaseDateStr = string.Empty;

                if (DateTime.TryParse(videoPageElements["datePublished"].ToString(), out var releaseDate))
                {
                    releaseDateStr = releaseDate.ToString("yyyy-MM-dd");
                }
                else if (searchDate.HasValue)
                {
                    releaseDateStr = searchDate.Value.ToString("yyyy-MM-dd");
                }

                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}|{releaseDateStr}" } },
                    Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDateStr}",
                    SearchProviderName = Plugin.Instance.Name,
                });
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
            string sceneURL = Helper.Decode(providerIds[0]);
            int siteNumVal = int.Parse(providerIds[1]);
            string sceneDate = providerIds[2];

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (detailsPageElements == null)
            {
                return result;
            }

            var ldJsonNode = detailsPageElements.SelectSingleNode("//script[@type='application/ld+json'][contains(., 'thumbnail')]");
            if (ldJsonNode == null)
            {
                return result;
            }

            var videoPageElements = JObject.Parse(ldJsonNode.InnerText.Replace("\n", string.Empty).Trim());

            var movie = (Movie)result.Item;
            movie.Name = Helper.ParseTitle(HTML.Clean(videoPageElements["name"].ToString()), siteNum);
            movie.Overview = HTML.Clean(videoPageElements["description"].ToString());
            movie.AddStudio(Regex.Replace(Helper.ParseTitle(videoPageElements["productionCompany"]["name"].ToString().Trim(), siteNum), @"bang(?=(\s|$))(?!\!)", "Bang!", RegexOptions.IgnoreCase));

            string tagline = detailsPageElements.SelectSingleNode("//p[contains(., 'Series:')]/a[contains(@href, 'originals') or contains(@href, 'videos')]")?.InnerText.Trim();
            if (!string.IsNullOrEmpty(tagline))
            {
                movie.AddTag(Regex.Replace(Helper.ParseTitle(tagline, siteNum), @"bang(?=(\s|$))(?!\!)", "Bang!", RegexOptions.IgnoreCase));
                movie.AddCollection(Regex.Replace(Helper.ParseTitle(tagline, siteNum), @"bang(?=(\s|$))(?!\!)", "Bang!", RegexOptions.IgnoreCase));
            }
            else
            {
                movie.AddCollection(movie.Studios.First());
            }

            string dvdTitle = detailsPageElements.SelectSingleNode("//p[contains(., 'Movie')]/a[contains(@href, 'dvd')]")?.InnerText.Trim();
            if (!string.IsNullOrEmpty(dvdTitle) && siteNumVal == 1365)
            {
                movie.AddCollection(dvdTitle);
            }

            if (DateTime.TryParse(videoPageElements["datePublished"].ToString(), out var releaseDate))
            {
                movie.PremiereDate = releaseDate;
                movie.ProductionYear = releaseDate.Year;
            }
            else if (DateTime.TryParse(sceneDate, out var parsedSceneDate))
            {
                movie.PremiereDate = parsedSceneDate;
                movie.ProductionYear = parsedSceneDate.Year;
            }

            string actorXPath = (siteNumVal == 1365) ? "//div[contains(@class, 'clear-both')]//a[contains(@href, 'pornstar')]" : "//div[contains(@class, 'name')]/a[contains(@href, 'pornstar') and not(@aria-label)]";
            var actorNodes = detailsPageElements.SelectNodes(actorXPath);
            if (actorNodes != null)
            {
                foreach (var actorLink in actorNodes)
                {
                    string actorName;
                    string actorPhotoURL = string.Empty;
                    if (siteNumVal == 1365)
                    {
                        actorName = actorLink.InnerText;
                        string modelURL = Helper.GetSearchBaseURL(siteNum) + actorLink.GetAttributeValue("href", string.Empty);
                        var modelPage = await HTML.ElementFromURL(modelURL, cancellationToken);
                        if (modelPage != null)
                        {
                            var modelLdJson = modelPage.SelectSingleNode("//script[@type='application/ld+json'][contains(., 'Person')]");
                            if (modelLdJson != null)
                            {
                                var modelElements = JObject.Parse(modelLdJson.InnerText.Trim());
                                actorPhotoURL = modelElements["image"].ToString().Split('?')[0].Trim();
                            }
                        }
                    }
                    else
                    {
                        actorName = actorLink.SelectSingleNode(".//span").InnerText;
                        string img = actorLink.SelectSingleNode("../..//img").GetAttributeValue("src", string.Empty).Split('?')[0];
                        if (!img.Contains("placeholder"))
                        {
                            actorPhotoURL = img;
                        }
                    }

                    if (!string.IsNullOrEmpty(actorName))
                    {
                        result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoURL });
                    }
                }
            }

            var genreNodes = detailsPageElements.SelectNodes("//div[@class='actions']/a | //a[@class='genres']");
            if (genreNodes != null)
            {
                foreach (var genreLink in genreNodes)
                {
                    movie.AddGenre(genreLink.InnerText);
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();

            string[] providerIds = sceneID[0].Split('|');
            string sceneURL = Helper.Decode(providerIds[0]);

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (detailsPageElements == null)
            {
                return images;
            }

            var ldJsonNode = detailsPageElements.SelectSingleNode("//script[@type='application/ld+json'][contains(., 'thumbnail')]");
            if (ldJsonNode == null)
            {
                return images;
            }

            var videoPageElements = JObject.Parse(ldJsonNode.InnerText.Replace("\n", string.Empty).Trim());

            var imageUrls = new List<string>();
            string thumbnailUrl = videoPageElements["thumbnailUrl"].ToString();
            if (thumbnailUrl.Contains("covers"))
            {
                imageUrls.Add(thumbnailUrl);
            }
            else
            {
                var match = Regex.Match(thumbnailUrl, @"(?<=shots/)\d+");
                if (match.Success)
                {
                    string movieID = match.Groups[0].Value;
                    imageUrls.Add($"https://i.bang.com/covers/{movieID}/front.jpg");
                }

                imageUrls.Add(thumbnailUrl);
            }

            if (videoPageElements["trailer"] != null)
            {
                foreach (var img in videoPageElements["trailer"])
                {
                    imageUrls.Add(img["thumbnailUrl"].ToString());
                }
            }

            bool first = true;
            foreach (var imageUrl in imageUrls.Distinct())
            {
                var imageInfo = new RemoteImageInfo { Url = imageUrl.Split('?')[0] };
                if (first)
                {
                    imageInfo.Type = ImageType.Primary;
                    first = false;
                }
                else
                {
                    imageInfo.Type = ImageType.Backdrop;
                }

                images.Add(imageInfo);
            }

            return images;
        }
    }
}
