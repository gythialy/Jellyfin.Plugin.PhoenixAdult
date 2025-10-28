using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
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
using System.Net.Http;

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class SiteAdultEmpire : IProviderBase
    {
        private (string, string) GetReleaseDateAndDisplayDate(HtmlNode detailsPageElements, DateTime? searchDate)
        {
            var releaseDate = string.Empty;
            var displayDate = string.Empty;

            var dateNode = detailsPageElements?.SelectSingleNode("//li[contains(., 'Released:')]/text()");
            var dateStr = dateNode?.InnerText.Trim();

            if (!string.IsNullOrEmpty(dateStr) && !dateStr.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            {
                if (DateTime.TryParseExact(dateStr, "MMM d yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                {
                    releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    displayDate = releaseDate;
                }
            }
            else if (searchDate.HasValue)
            {
                releaseDate = searchDate.Value.ToString("yyyy-MM-dd");
            }

            return (releaseDate, displayDate);
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();
            var cookies = new Dictionary<string, string> { { "ageConfirmed", "true" } };
            var searchResults = new List<string>();
            var directId = false;
            var sceneId = searchTitle.Split(' ').FirstOrDefault();
            if (int.TryParse(sceneId, out var id) && id > 100)
            {
                searchTitle = searchTitle.Replace(sceneId, string.Empty).Trim();
                var movieUrl = $"{Helper.GetSearchBaseURL(siteNum)}/{sceneId}";
                searchResults.Add(movieUrl);
                directId = true;
            }

            if (!directId)
            {
                var encodedTitle = searchTitle.Replace("&", string.Empty).Replace("'", string.Empty).Replace(",", string.Empty).Replace("#", string.Empty).Replace(" ", "+");
                var searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{encodedTitle}";
                var searchHttp = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken, new Dictionary<string, string> { { "Referer", "http://www.data18.empirestores.co" } }, cookies);
                if (searchHttp.IsOK)
                {
                    var searchDoc = new HtmlDocument();
                    searchDoc.LoadHtml(searchHttp.Content);
                    var searchResultNodes = searchDoc.DocumentNode.SelectNodes("//div[@class='product-details__item-title']");
                    if (searchResultNodes != null)
                    {
                        foreach (var searchResult in searchResultNodes)
                        {
                            var resultType = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(searchResult.GetAttributeValue("href", string.Empty).Split('-').Last().Replace(".html", string.Empty).Replace("ray", "Blu-Ray"));
                            var urlId = searchResult.GetAttributeValue("href", string.Empty).Split('/')[1];
                            var movieUrl = $"{Helper.GetSearchBaseURL(siteNum)}/{urlId}";
                            if (!searchResults.Contains(movieUrl))
                            {
                                var titleNoFormatting = Helper.ParseTitle(searchResult.SelectSingleNode("./a").InnerText.Trim(), siteNum);
                                var curId = Helper.Encode(movieUrl);
                                var (releaseDate, displayDate) = GetReleaseDateAndDisplayDate(null, searchDate);
                                var detailHttp = await HTTP.Request(movieUrl, HttpMethod.Get, cancellationToken, null, cookies);
                                if (detailHttp.IsOK)
                                {
                                    var detailDoc = new HtmlDocument();
                                    detailDoc.LoadHtml(detailHttp.Content);
                                    (releaseDate, displayDate) = GetReleaseDateAndDisplayDate(detailDoc.DocumentNode, searchDate);
                                    var studioNode = detailDoc.DocumentNode.SelectSingleNode("//li[contains(., 'Studio:')]/a");
                                    var studio = studioNode?.InnerText.Trim() ?? string.Empty;
                                    results.Add(new RemoteSearchResult
                                    {
                                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}" } },
                                        Name = $"{titleNoFormatting} [{studio}] [{resultType}] {displayDate}",
                                        SearchProviderName = Plugin.Instance.Name,
                                    });
                                    var sceneNodes = detailDoc.DocumentNode.SelectNodes("//div[@class='row'][.//h3]");
                                    if (sceneNodes != null)
                                    {
                                        for (int i = 0; i < sceneNodes.Count; i++)
                                        {
                                            var actorNames = string.Join(", ", sceneNodes[i].SelectNodes(".//div/a").Select(a => a.InnerText.Trim()));
                                            var photoIdx = i * 2 - 1;
                                            results.Add(new RemoteSearchResult
                                            {
                                                ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}|{i + 1}|{photoIdx}" } },
                                                Name = $"{titleNoFormatting}[{resultType}]/#{i + 1}[{actorNames}][{studio}] {displayDate}",
                                                SearchProviderName = Plugin.Instance.Name,
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                var googleResults = await WebSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
                foreach (var movieUrl in googleResults)
                {
                    var cleanUrl = movieUrl.Substring(0, movieUrl.LastIndexOf('/'));
                    if (movieUrl.Contains("movies") && !movieUrl.EndsWith(".html") && !searchResults.Contains(cleanUrl))
                    {
                        searchResults.Add(cleanUrl);
                    }
                }
            }

            foreach (var movieUrl in searchResults)
            {
                var detailHttp = await HTTP.Request(movieUrl, HttpMethod.Get, cancellationToken, null, cookies);
                if (detailHttp.IsOK)
                {
                    var detailDoc = new HtmlDocument();
                    detailDoc.LoadHtml(detailHttp.Content);
                    var urlId = movieUrl.Split('/').Last();
                    var titleNoFormatting = Helper.ParseTitle(detailDoc.DocumentNode.SelectSingleNode("//h1").InnerText.Trim(), siteNum);
                    var curId = Helper.Encode(movieUrl);
                    var (releaseDate, displayDate) = GetReleaseDateAndDisplayDate(detailDoc.DocumentNode, searchDate);
                    var studioNode = detailDoc.DocumentNode.SelectSingleNode("//li[contains(., 'Studio:')]/a");
                    var studio = studioNode?.InnerText.Trim() ?? string.Empty;
                    results.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}" } },
                        Name = $"{titleNoFormatting} [{studio}] {displayDate}",
                        SearchProviderName = Plugin.Instance.Name,
                    });
                    var sceneNodes = detailDoc.DocumentNode.SelectNodes("//div[@class='row'][.//h3]");
                    if (sceneNodes != null)
                    {
                        for (int i = 0; i < sceneNodes.Count; i++)
                        {
                            var actorNames = string.Join(", ", sceneNodes[i].SelectNodes(".//div/a").Select(a => a.InnerText.Trim()));
                            var photoIdx = i * 2 - 1;
                            results.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}|{i + 1}|{photoIdx}" } },
                                Name = $"{titleNoFormatting}/#{i + 1}[{actorNames}][{studio}] {displayDate}",
                                SearchProviderName = Plugin.Instance.Name,
                            });
                        }
                    }
                }
            }

            return results;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem> { Item = new Movie(), People = new List<PersonInfo>() };
            var movie = (Movie)result.Item;
            var providerIds = sceneID[0].Split('|');
            var sceneURL = Helper.Decode(providerIds[0]);
            var sceneDate = providerIds[2];
            var cookies = new Dictionary<string, string> { { "ageConfirmed", "true" } };
            var http = await HTTP.Request(sceneURL, HttpMethod.Get, cancellationToken, null, cookies);
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);
            var splitScene = providerIds.Length > 3;
            var sceneNum = splitScene ? int.Parse(providerIds[3]) : 0;
            var sceneIndex = splitScene ? int.Parse(providerIds[4]) : 0;
            movie.Name = Helper.ParseTitle(doc.DocumentNode.SelectSingleNode("//h1").InnerText.Trim(), siteNum);
            if (splitScene)
            {
                movie.Name = $"{movie.Name} [Scene {sceneNum}]";
            }

            var summaryNode = doc.DocumentNode.SelectSingleNode("//div[@class='container'][.//h2]//parent::p");
            movie.Overview = summaryNode?.InnerText.Trim();
            var studioNode = doc.DocumentNode.SelectSingleNode("//li[contains(., 'Studio:')]/a");
            if (studioNode != null)
            {
                movie.AddStudio(studioNode.InnerText.Trim());
                movie.AddCollection(studioNode.InnerText.Trim());
            }

            var taglineNode = doc.DocumentNode.SelectSingleNode("//h2/a[@label='Series']");
            if (taglineNode != null)
            {
                var tagline = Regex.Replace(taglineNode.InnerText.Trim().Split('"')[1], @"\(.*\)", string.Empty).Trim();
                movie.AddTag(Helper.ParseTitle(tagline, siteNum));
                movie.AddCollection(Helper.ParseTitle(tagline, siteNum));
            }
            else if (splitScene)
            {
                movie.AddCollection(Helper.ParseTitle(doc.DocumentNode.SelectSingleNode("//h1").InnerText, siteNum).Trim());
            }

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }
            else
            {
                var (releaseDate, _) = GetReleaseDateAndDisplayDate(doc.DocumentNode, null);
                if (!string.IsNullOrEmpty(releaseDate) && DateTime.TryParse(releaseDate, out parsedDate))
                {
                    movie.PremiereDate = parsedDate;
                    movie.ProductionYear = parsedDate.Year;
                }
            }

            var genreNodes = doc.DocumentNode.SelectNodes("//li//a[@label='Category']");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            HtmlNodeCollection actorNodes;
            if (splitScene)
            {
                var sceneNode = doc.DocumentNode.SelectNodes("//div[@class='row'][.//h3]")[sceneIndex];
                actorNodes = sceneNode.SelectNodes(".//div/a");
            }
            else
            {
                actorNodes = doc.DocumentNode.SelectNodes("//div[contains(., 'Starring')][1]/a[contains(@href, 'pornstars')]");
            }

            if (actorNodes != null)
            {
                foreach (var actorLink in actorNodes)
                {
                    var actorName = actorLink.InnerText.Split('(')[0].Trim();
                    var actorPhotoNode = doc.DocumentNode.SelectSingleNode($"//div[contains(., 'Starring')]//img[contains(@title, \"{actorName}\")]");
                    var actorPhotoURL = actorPhotoNode?.GetAttributeValue("src", string.Empty);
                    if (!string.IsNullOrEmpty(actorName))
                    {
                        result.People.Add(new PersonInfo { Name = actorName, ImageUrl = actorPhotoURL, Type = PersonKind.Actor });
                    }
                }
            }

            var directorNodes = doc.DocumentNode.SelectNodes("//div[./a[@name='cast']]//li[./*[contains(., 'Director')]]/a");
            if (directorNodes != null)
            {
                foreach (var directorLink in directorNodes)
                {
                    var directorName = directorLink.InnerText.Trim();
                    var directorPhotoNode = doc.DocumentNode.SelectSingleNode($"//div[contains(., 'Starring')]//img[contains(@title, \"{directorName}\")]");
                    var directorPhotoURL = directorPhotoNode?.GetAttributeValue("src", string.Empty);
                    result.People.Add(new PersonInfo { Name = directorName, ImageUrl = directorPhotoURL, Type = PersonKind.Director });
                }
            }

            var producerNodes = doc.DocumentNode.SelectNodes("//div[./a[@name='cast']]//li[./*[contains(., 'Producer')]]/text()");
            if (producerNodes != null)
            {
                foreach (var producerLink in producerNodes)
                {
                    var producerName = producerLink.InnerText.Trim();
                    var producerPhotoNode = doc.DocumentNode.SelectSingleNode($"//div[contains(., 'Starring')]//img[contains(@title, \"{producerName}\")]");
                    var producerPhotoURL = producerPhotoNode?.GetAttributeValue("src", string.Empty);
                    result.People.Add(new PersonInfo { Name = producerName, ImageUrl = producerPhotoURL, Type = PersonKind.Producer });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var providerIds = sceneID[0].Split('|');
            var sceneURL = Helper.Decode(providerIds[0]);
            var splitScene = providerIds.Length > 3;

            var cookies = new Dictionary<string, string>() { { "ageConfirmed", "true" } };

            var http = await HTTP.Request(sceneURL, HttpMethod.Get, cancellationToken, null, cookies);
            if (!http.IsOK)
            {
                return images;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);

            var art = new List<string>();
            var xpaths = new[]
            {
                "//div[@class='boxcover-container']/a/img/@src",
                "//div[@class='boxcover-container']/a/@href",
            };
            foreach (var xpath in xpaths)
            {
                var node = doc.DocumentNode.SelectSingleNode(xpath);
                if (node != null)
                {
                    art.Add(node.GetAttributeValue("src", node.GetAttributeValue("href", string.Empty)));
                }
            }

            if (splitScene)
            {
                var sceneIndex = int.Parse(providerIds[4]);
                var splitScenesXpath = $"//div[@class='row'][.//div[@class='row']][.//a[@rel='scenescreenshots']][{sceneIndex + 1}]//a/@href";
                var sceneImageNodes = doc.DocumentNode.SelectNodes(splitScenesXpath);
                if (sceneImageNodes != null)
                {
                    foreach (var node in sceneImageNodes)
                    {
                        art.Add(node.GetAttributeValue("href", string.Empty));
                    }
                }
            }
            else
            {
                var scenesXpath = "//div[@class='row'][.//div[@class='row']][.//a[@rel='scenescreenshots']]//div[@class='row']//a/@href";
                var sceneImageNodes = doc.DocumentNode.SelectNodes(scenesXpath);
                if (sceneImageNodes != null)
                {
                    foreach (var node in sceneImageNodes)
                    {
                        art.Add(node.GetAttributeValue("href", string.Empty));
                    }
                }
            }

            // This logic of downloading images to determine type can be slow.
            foreach (var imageUrl in art)
            {
                try
                {
                    var imageHttp = await HTTP.Request(imageUrl, HttpMethod.Get, cancellationToken, null, cookies);
                    if (imageHttp.IsOK)
                    {
                        using (var ms = new MemoryStream(imageHttp.ContentStream.ToBytes()))
                        {
                            var image = new Bitmap(ms);
                            var imageInfo = new RemoteImageInfo { Url = imageUrl };
                            if (image.Height > image.Width)
                            {
                                imageInfo.Type = ImageType.Primary;
                            }
                            else
                            {
                                imageInfo.Type = ImageType.Backdrop;
                            }

                            images.Add(imageInfo);
                        }
                    }
                }
                catch
                {
                    // Ignore image processing errors
                }
            }

            return images;
        }
    }
}
