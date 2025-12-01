using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
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

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class SiteData18Movies : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();
            var searchResults = new List<string>();
            var siteResults = new List<string>();
            var sceneId = searchTitle.Split(' ').FirstOrDefault();
            if (int.TryParse(sceneId, out var id) && id > 100)
            {
                searchTitle = searchTitle.Replace(sceneId, string.Empty).Trim();
                var movieUrl = $"{Helper.GetSearchBaseURL(siteNum)}/movies/{sceneId}";
                searchResults.Add(movieUrl);
            }

            var encodedTitle = searchTitle.Replace("'", string.Empty).Replace(",", string.Empty).Replace("& ", string.Empty).Replace("#", string.Empty);
            var searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{encodedTitle}&key2={encodedTitle}";
            var searchHttp = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken, new Dictionary<string, string> { { "Referer", "https://www.data18.com" } }, new Dictionary<string, string> { { "data_user_captcha", "1" } });
            if (searchHttp.IsOK)
            {
                var searchDoc = new HtmlDocument();
                searchDoc.LoadHtml(searchHttp.Content);
                var searchPagesMatch = Regex.Match(searchHttp.Content, @"(?<=pages:\s).*(?=])");
                var numSearchPages = searchPagesMatch.Success ? Math.Min(int.Parse(searchPagesMatch.Value), 10) : 1;
                for (var i = 0; i < numSearchPages; i++)
                {
                    var searchResultNodes = searchDoc.DocumentNode.SelectNodes("//a");
                    if (searchResultNodes != null)
                    {
                        foreach (var searchResult in searchResultNodes)
                        {
                            var movieUrl = searchResult.GetAttributeValue("href", string.Empty).Split('-')[0];
                            if (movieUrl.Contains("/movies/") && !searchResults.Contains(movieUrl))
                            {
                                var titleNoFormatting = Helper.ParseTitle(searchResult.SelectSingleNode(".//p[@class='gen12 bold']").InnerText, siteNum);
                                if (titleNoFormatting.Contains("..."))
                                {
                                    searchResults.Add(movieUrl);
                                }
                                else
                                {
                                    siteResults.Add(movieUrl);
                                    var curId = Helper.Encode(movieUrl);
                                    var dateNode = searchResult.SelectSingleNode(".//span[@class='gen11']/text()");
                                    var dateStr = dateNode?.InnerText.Trim();
                                    var releaseDate = !string.IsNullOrEmpty(dateStr) && !dateStr.Equals("unknown", StringComparison.OrdinalIgnoreCase)
                                        ? DateTime.ParseExact(dateStr, "MMMM, yyyy", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd")
                                        : searchDate?.ToString("yyyy-MM-dd") ?? string.Empty;
                                    var displayDate = !string.IsNullOrEmpty(dateStr) ? releaseDate : string.Empty;
                                    var studio = searchResult.SelectSingleNode(".//i")?.InnerText.Trim() ?? string.Empty;
                                    var detailHttp = await HTTP.Request(movieUrl, HttpMethod.Get, cancellationToken, null, new Dictionary<string, string> { { "data_user_captcha", "1" } });
                                    if (detailHttp.IsOK)
                                    {
                                        var detailDoc = new HtmlDocument();
                                        detailDoc.LoadHtml(detailHttp.Content);
                                        var studioNode = detailDoc.DocumentNode.SelectSingleNode("//b[contains(., 'Studio') or contains(., 'Network')]//following-sibling::b | //b[contains(., 'Studio') or contains(., 'Network')]//following-sibling::a | //p[contains(., 'Site:')]//following-sibling::a[@class='bold']");
                                        studio = studioNode?.InnerText.Trim() ?? studio;
                                        results.Add(new RemoteSearchResult
                                        {
                                            ProviderIds = { { Plugin.Instance.Name, $"{curId}|{releaseDate}" } },
                                            Name = $"{titleNoFormatting} [{studio}] {displayDate}",
                                            SearchProviderName = Plugin.Instance.Name,
                                        });
                                        var sceneCountMatch = detailDoc.DocumentNode.SelectSingleNode("//div[@id='relatedscenes']//span")?.InnerText.Split(' ')[0].Trim();
                                        if (int.TryParse(sceneCountMatch, out var sceneCount))
                                        {
                                            for (var j = 1; j <= sceneCount; j++)
                                            {
                                                var section = "Scene " + j;
                                                var sceneNode = detailDoc.DocumentNode.SelectSingleNode($"//a[contains(., '{section}')]");
                                                var scene = Helper.Encode(sceneNode.GetAttributeValue("href", string.Empty));
                                                results.Add(new RemoteSearchResult
                                                {
                                                    ProviderIds = { { Plugin.Instance.Name, $"{scene}|{releaseDate}|{titleNoFormatting}|{j}" } },
                                                    Name = $"{titleNoFormatting} [{section}][{studio}] {displayDate}",
                                                    SearchProviderName = Plugin.Instance.Name,
                                                });
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (numSearchPages > 1 && i + 1 < numSearchPages)
                    {
                        searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{encodedTitle}&key2={encodedTitle}&next=1&page={i + 1}";
                        searchHttp = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken, new Dictionary<string, string> { { "Referer", "https://www.data18.com" } }, new Dictionary<string, string> { { "data_user_captcha", "1" } });
                        if (searchHttp.IsOK)
                        {
                            searchDoc.LoadHtml(searchHttp.Content);
                        }
                    }
                }
            }

            var googleResults = await WebSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            foreach (var movieUrl in googleResults)
            {
                var url = movieUrl.Split('-')[0].Replace("http:", "https:");
                if (url.Contains("/movies/") && !url.EndsWith(".html") && !searchResults.Contains(url) && !siteResults.Contains(url))
                {
                    searchResults.Add(url);
                }
            }

            foreach (var movieUrl in searchResults)
            {
                var httpResult = await HTTP.Request(movieUrl, HttpMethod.Get, cancellationToken, null, new Dictionary<string, string> { { "data_user_captcha", "1" } });
                if (httpResult.IsOK)
                {
                    var detailDoc = new HtmlDocument();
                    detailDoc.LoadHtml(httpResult.Content);
                    var titleNoFormatting = Helper.ParseTitle(detailDoc.DocumentNode.SelectSingleNode("//h1").InnerText, siteNum);
                    var curId = Helper.Encode(movieUrl);
                    var dateNode = detailDoc.DocumentNode.SelectSingleNode("//@datetime");
                    var releaseDate = dateNode != null && DateTime.TryParse(dateNode.GetAttributeValue("datetime", string.Empty).Trim(), out var parsedDate)
                        ? parsedDate.ToString("yyyy-MM-dd")
                        : searchDate?.ToString("yyyy-MM-dd") ?? string.Empty;
                    var displayDate = dateNode != null ? releaseDate : string.Empty;
                    var studioNode = detailDoc.DocumentNode.SelectSingleNode("//b[contains(., 'Studio') or contains(., 'Network')]//following-sibling::a | //b[contains(., 'Studio') or contains(., 'Network')]//following-sibling::b | //p[contains(., 'Site:')]//following-sibling::a[@class='bold']");
                    var studio = studioNode?.InnerText.Trim() ?? string.Empty;
                    results.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{releaseDate}" } },
                        Name = $"{titleNoFormatting} [{studio}] {displayDate}",
                        SearchProviderName = Plugin.Instance.Name,
                    });
                    var sceneCountMatch = detailDoc.DocumentNode.SelectSingleNode("//div[@id='relatedscenes']//span")?.InnerText.Split(' ')[0].Trim();
                    if (int.TryParse(sceneCountMatch, out var sceneCount))
                    {
                        for (var j = 1; j <= sceneCount; j++)
                        {
                            var section = "Scene " + j;
                            var sceneNode = detailDoc.DocumentNode.SelectSingleNode($"//a[contains(., '{section}')]");
                            var scene = Helper.Encode(sceneNode.GetAttributeValue("href", string.Empty));
                            results.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, $"{scene}|{releaseDate}|{titleNoFormatting}|{j}" } },
                                Name = $"{titleNoFormatting} [{section}][{studio}] {displayDate}",
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
            var providerIds = sceneID[0].Split('|');
            if (providerIds.Length > 1)
            {
                var sceneProvider = new SiteData18Scenes();
                return await sceneProvider.Update(siteNum, sceneID, cancellationToken);
            }

            var sceneURL = Helper.Decode(providerIds[0]);
            var sceneDate = providerIds.Length > 1 ? providerIds[1] : null;
            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken, new Dictionary<string, string> { { "Referer", "https://www.data18.com" } }, new Dictionary<string, string> { { "data_user_captcha", "1" } });
            if (detailsPageElements == null)
            {
                return result;
            }

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneURL;
            movie.Name = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//h1").InnerText, siteNum);
            var summaryNode = detailsPageElements.SelectSingleNode("//div[@class='gen12']//div[contains(., 'Description')]");
            var summary = summaryNode?.InnerText.Split(new[] { "---", "Description -" }, StringSplitOptions.RemoveEmptyEntries).Last().Trim();
            if (!string.IsNullOrEmpty(summary))
            {
                movie.Overview = summary;
            }

            var studioNode = detailsPageElements.SelectSingleNode("//b[contains(., 'Network')]//following-sibling::b | //b[contains(., 'Studio')]//following-sibling::b | //p[contains(., 'Site:')]//following-sibling::a[@class='bold']");
            if (studioNode != null)
            {
                movie.AddStudio(studioNode.InnerText.Trim());
                movie.AddCollection(studioNode.InnerText.Trim());
            }

            var taglineNode = detailsPageElements.SelectSingleNode("//p[contains(., 'Movie Series')]//a[@title]");
            if (taglineNode != null)
            {
                movie.AddStudio(taglineNode.InnerText.Trim());
                movie.AddCollection(taglineNode.InnerText.Trim());
            }

            var dateNode = detailsPageElements.SelectSingleNode("//@datetime");
            if (dateNode != null && DateTime.TryParse(dateNode.GetAttributeValue("datetime", string.Empty).Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }
            else if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//p[./b[contains(., 'Categories')]]//a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//b[contains(., 'Cast')]//following::div//a[contains(@href, '/pornstars/')]//img | //b[contains(., 'Cast')]//following::div//img[contains(@data-original, 'user')] | //h3[contains(., 'Cast')]//following::div[@style]//img");
            if (actorNodes != null)
            {
                foreach (var actor in actorNodes)
                {
                    var actorName = actor.GetAttributeValue("alt", string.Empty).Trim();
                    var actorPhotoUrl = actor.GetAttributeValue("data-src", string.Empty);
                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, ImageUrl = actorPhotoUrl, Type = PersonKind.Actor });
                }
            }

            var directorNode = detailsPageElements.SelectSingleNode("//p[./b[contains(., 'Director')]]");
            if (directorNode != null)
            {
                var directorName = directorNode.InnerText.Split(':').Last().Split('-')[0].Trim();
                if (!directorName.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                {
                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = directorName, Type = PersonKind.Director });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            string[] providerIds = sceneID[0].Split('|');
            string sceneURL = Helper.Decode(providerIds[0]);

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken, new Dictionary<string, string> { { "Referer", "https://www.data18.com" } }, new Dictionary<string, string> { { "data_user_captcha", "1" } });
            if (detailsPageElements == null)
            {
                return result;
            }

            var imageNodes = detailsPageElements.SelectNodes("//a[@id='enlargecover']//@data-featherlight | //img[@id='backcoverzone']//@src | //img[@id='imgposter']//@src | //img[contains(@src, 'th8')]/@src | //img[contains(@data-original, 'th8')]/@data-original");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    result.Add(new RemoteImageInfo { Url = img.GetAttributeValue(img.Name == "a" ? "data-featherlight" : (img.Name == "img" ? "src" : "data-original"), string.Empty) });
                }
            }

            var galleries = detailsPageElements.SelectNodes("//div[@id='galleriesoff']//div");
            if (galleries != null)
            {
                string movieId = Regex.Replace(sceneURL, ".*/", string.Empty);
                foreach (var gallery in galleries)
                {
                    string galleryId = gallery.GetAttributeValue("id", string.Empty).Replace("gallery", string.Empty);
                    string photoViewerUrl = $"{Helper.GetSearchBaseURL(siteNum)}/sys/media_photos.php?movie={movieId.Substring(1)}&pic={galleryId}";
                    var req = await HTTP.Request(photoViewerUrl, cancellationToken);
                    var photoPageElements = HTML.ElementFromString(req.Content);
                    var photoNodes = photoPageElements.SelectNodes("//a[@id='enlargecover']//@data-featherlight | //img[@id='backcoverzone']//@src | //img[@id='imgposter']//@src | //img[contains(@src, 'th8')]/@src | //img[contains(@data-original, 'th8')]/@data-original");
                    if (photoNodes != null)
                    {
                        foreach (var img in photoNodes)
                        {
                            result.Add(new RemoteImageInfo { Url = (img.GetAttributeValue(img.Name == "a" ? "data-featherlight" : (img.Name == "img" ? "src" : "data-original"), string.Empty)).Replace("/th8", string.Empty).Replace("-th8", string.Empty) });
                        }
                    }
                }
            }

            return result;
        }
    }
}
