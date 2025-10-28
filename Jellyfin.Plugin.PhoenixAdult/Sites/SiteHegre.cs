using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    public class SiteHegre : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            string sceneUrl = $"https://www.hegre.com/films/{searchTitle.Replace(' ', '-').ToLower()}";
            var req = await HTTP.Request(sceneUrl, cancellationToken);
            if (req.IsOK)
            {
                var searchResult = HTML.ElementFromString(req.Content);
                string curID = Helper.Encode(sceneUrl);
                string titleNoFormatting = searchResult.SelectSingleNode("//h1")?.InnerText.Trim();
                string date = searchResult.SelectSingleNode("//span[@class='date']")?.InnerText.Trim();
                string releaseDate = DateTime.Parse(date).ToString("yyyy-MM-dd");
                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, curID } },
                    Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                    SearchProviderName = Plugin.Instance.Name,
                });
            }
            else
            {
                string searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{Uri.EscapeDataString(searchTitle)}";
                if (searchDate.HasValue)
                {
                    searchUrl += $"&year={searchDate.Value.Year}";
                }

                var data = await HTML.ElementFromURL(searchUrl, cancellationToken);
                var searchResults = data?.SelectNodes("//div[contains(@class, 'item')]");
                if (searchResults != null)
                {
                    foreach (var searchResult in searchResults)
                    {
                        var sceneURLNode = searchResult.SelectSingleNode(".//a");
                        if (sceneURLNode == null)
                        {
                            continue;
                        }

                        string sceneURL = sceneURLNode.GetAttributeValue("href", string.Empty);
                        if (sceneURL.Contains("/films/") || sceneURL.Contains("/massage/"))
                        {
                            string curID = Helper.Encode(sceneURL);
                            string sceneName = searchResult.SelectSingleNode(".//img")?.GetAttributeValue("alt", string.Empty);
                            string scenePoster = searchResult.SelectSingleNode(".//img")?.GetAttributeValue("data-src", string.Empty);
                            string dateStr = searchResult.SelectSingleNode(".//div[@class='details']/span[last()]")?.InnerText.Trim();
                            string releaseDate = string.Empty;
                            if (!string.IsNullOrEmpty(dateStr))
                            {
                                dateStr = dateStr.Replace("nd", string.Empty).Replace("th", string.Empty).Replace("rd", string.Empty).Replace("st", string.Empty);
                                if (DateTime.TryParseExact(dateStr, "MMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
                                {
                                    releaseDate = sceneDateObj.ToString("yyyy-MM-dd");
                                }
                            }

                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, curID } },
                                Name = $"{sceneName} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                                ImageUrl = scenePoster,
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

            string sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (sceneData == null)
            {
                return result;
            }

            var movie = (Movie)result.Item;
            movie.Name = sceneData.SelectSingleNode("//meta[@property='og:title']")?.GetAttributeValue("content", string.Empty).Trim();
            string summary = sceneData.SelectSingleNode("//div[@class='record-description-content record-box-content']")?.InnerText.Trim();
            if (!string.IsNullOrEmpty(summary))
            {
                movie.Overview = summary.Substring(0, summary.IndexOf("Runtime", StringComparison.OrdinalIgnoreCase)).Trim();
            }

            movie.AddStudio("Hegre");
            movie.AddTag(Helper.GetSearchSiteName(siteNum));

            var date = sceneData.SelectSingleNode("//span[@class='date']")?.InnerText.Trim();
            if (DateTime.TryParse(date, out var sceneDateObj))
            {
                movie.PremiereDate = sceneDateObj;
                movie.ProductionYear = sceneDateObj.Year;
            }

            var genreNode = sceneData.SelectNodes("//a[@class='tag']");
            if (genreNode != null)
            {
                foreach (var genreLink in genreNode)
                {
                    movie.AddGenre(genreLink.InnerText.Trim().ToLower());
                }
            }

            var actorsNode = sceneData.SelectNodes("//a[@class='record-model']");
            if (actorsNode != null)
            {
                if (actorsNode.Count == 3)
                {
                    movie.AddGenre("Threesome");
                }

                if (actorsNode.Count == 4)
                {
                    movie.AddGenre("Foursome");
                }

                if (actorsNode.Count > 4)
                {
                    movie.AddGenre("Orgy");
                }

                foreach (var actorLink in actorsNode)
                {
                    result.People.Add(new PersonInfo
                    {
                        Name = actorLink.GetAttributeValue("title", string.Empty).Trim(),
                        ImageUrl = actorLink.SelectSingleNode(".//img")?.GetAttributeValue("src", string.Empty).Replace("240x", "480x"),
                        Type = PersonKind.Actor,
                    });
                }
            }

            result.People.Add(new PersonInfo { Name = "Petter Hegre", ImageUrl = "https://img.discogs.com/TafxhnwJE2nhLodoB6UktY6m0xM=/fit-in/180x264/filters:strip_icc():format(jpeg):mode_rgb():quality(90)/discogs-images/A-2236724-1305622884.jpeg.jpg", Type = PersonKind.Director });

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            string sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (sceneData == null)
            {
                return result;
            }

            var posterUrl = sceneData.SelectSingleNode("//meta[@name='twitter:image']")?.GetAttributeValue("content", string.Empty);
            if (!string.IsNullOrEmpty(posterUrl))
            {
                result.Add(new RemoteImageInfo { Url = posterUrl.Replace("board-image", "poster-image").Replace("1600x", "640x"), Type = ImageType.Primary });
                result.Add(new RemoteImageInfo { Url = posterUrl.Replace("1600x", "1920x"), Type = ImageType.Backdrop });
            }

            return result;
        }
    }
}
