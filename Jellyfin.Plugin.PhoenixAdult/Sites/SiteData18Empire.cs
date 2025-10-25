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

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class SiteData18Empire : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var searchResults = new List<string>();
            var siteResults = new List<string>();
            string sceneId = null;
            var parts = searchTitle.Split(' ');
            if (int.TryParse(parts[0], out var parsedId) && parsedId > 100)
            {
                sceneId = parts[0];
                searchTitle = searchTitle.Replace(sceneId, string.Empty).Trim();
                searchResults.Add($"{Helper.GetSearchBaseURL(siteNum)}/{sceneId}");
            }

            string encodedTitle = searchTitle.Trim().Replace(" ", "+");
            string searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{encodedTitle}";
            var req = await HTTP.Request(searchUrl, cancellationToken, new Dictionary<string, string> { { "Referer", "http://www.data18.empirestores.co" } });
            var searchPageElements = HTML.ElementFromString(req.Content);

            if (string.IsNullOrEmpty(sceneId))
            {
                var searchResultNodes = searchPageElements.SelectNodes("//a[@class='boxcover']");
                if (searchResultNodes != null)
                {
                    foreach (var searchResult in searchResultNodes)
                    {
                        string movieUrl = $"{Helper.GetSearchBaseURL(siteNum)}{searchResult.GetAttributeValue("href", string.Empty)}";
                        string urlId = searchResult.GetAttributeValue("href", string.Empty).Split('/')[1];
                        if (movieUrl.Contains("movies") && !searchResults.Contains(movieUrl))
                        {
                            string titleNoFormatting = searchResult.SelectSingleNode("./span/span/text()")?.InnerText.Trim();
                            string curId = Helper.Encode(movieUrl);

                            var detailsPageElements = await HTML.ElementFromURL(movieUrl, cancellationToken);
                            if (detailsPageElements == null) continue;

                            var dateNode = detailsPageElements.SelectSingleNode("//div[@class='release-date' and ./span[contains(., 'Released:')]]/text()");
                            string releaseDate = (dateNode != null && DateTime.TryParseExact(dateNode.InnerText.Trim(), "MMM dd, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)) ? parsedDate.ToString("yyyy-MM-dd") : string.Empty;
                            string studio = detailsPageElements.SelectSingleNode("//div[@class='studio']/a/text()")?.InnerText.Trim();
                            result.Add(new RemoteSearchResult { ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}" } }, Name = $"{titleNoFormatting} [{studio}] {releaseDate}" });

                            var scenes = detailsPageElements.SelectNodes("//div[@class='item-grid item-grid-scene']/div/a/@href");
                            if (scenes != null)
                            {
                                for (int i = 0; i < scenes.Count; i++)
                                {
                                    result.Add(new RemoteSearchResult { ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}|{i + 1}" } }, Name = $"{titleNoFormatting} [Scene {i + 1}][{studio}] {releaseDate}" });
                                }
                            }
                        }
                    }
                }
            }

            var googleResults = await GoogleSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            foreach (var movieUrl in googleResults)
            {
                if (movieUrl.Contains("/movies/") && !movieUrl.EndsWith(".html") && !searchResults.Contains(movieUrl) && !siteResults.Contains(movieUrl))
                {
                    searchResults.Add(movieUrl);
                }
            }

            foreach (var movieUrl in searchResults)
            {
                var httpResult = await HTTP.Request(movieUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var detailsPageElements = HTML.ElementFromString(httpResult.Content);
                    string titleNoFormatting = detailsPageElements.SelectSingleNode("//h1[@class='description']/text()")?.InnerText.Trim();
                    string curId = Helper.Encode(movieUrl);
                    var dateNode = detailsPageElements.SelectSingleNode("//div[@class='release-date' and ./span[contains(., 'Released:')]]/text()");
                    string releaseDate = (dateNode != null && DateTime.TryParseExact(dateNode.InnerText.Trim(), "MMM dd, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)) ? parsedDate.ToString("yyyy-MM-dd") : string.Empty;
                    string studio = detailsPageElements.SelectSingleNode("//div[@class='studio']/a/text()")?.InnerText.Trim();
                    result.Add(new RemoteSearchResult { ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}" } }, Name = $"{titleNoFormatting} [{studio}] {releaseDate}" });

                    var scenes = detailsPageElements.SelectNodes("//div[@class='item-grid item-grid-scene']/div/a/@href");
                    if (scenes != null)
                    {
                        for (int i = 0; i < scenes.Count; i++)
                        {
                            result.Add(new RemoteSearchResult { ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}|{i + 1}" } }, Name = $"{titleNoFormatting} [Scene {i + 1}][{studio}] {releaseDate}" });
                        }
                    }
                }
            }
            return result;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem> { Item = new Movie(), People = new List<PersonInfo>() };
            string[] providerIds = sceneID[0].Split('|');
            string sceneURL = Helper.Decode(providerIds[0]);
            string sceneDate = providerIds.Length > 2 ? providerIds[2] : null;

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (detailsPageElements == null)
            {
                return result;
            }

            var movie = (Movie)result.Item;
            string title = detailsPageElements.SelectSingleNode("//h1[@class='description']/text()")?.InnerText.Trim();
            movie.Name = title;
            if (providerIds.Length > 3)
            {
                movie.Name = $"{title} [Scene {providerIds[3]}]";
            }

            movie.Overview = string.Join("\n\n", detailsPageElements.SelectNodes("//div[@class='synopsis']//text()").Select(n => n.InnerText));

            var studio = detailsPageElements.SelectSingleNode("//div[@class='studio']/a/text()")?.InnerText.Trim();
            if (!string.IsNullOrEmpty(studio))
            {
                movie.AddStudio(studio);
            }

            var tagline = detailsPageElements.SelectSingleNode("//p[contains(text(), 'A scene from')]/a/text()")?.InnerText.Trim()
                ?? detailsPageElements.SelectSingleNode("//a[@data-label='Series List']/h2/text()")?.InnerText.Trim().Replace("Series:", string.Empty).Replace($"({studio})", string.Empty).Trim();
            movie.AddTag(tagline ?? studio);

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//div[@class='categories']/a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var actorNodes = new List<HtmlNode>();
            if (providerIds.Length > 3)
            {
                var sceneActorNodes = detailsPageElements.SelectNodes($"//div[@class='item-grid item-grid-scene']/div[@class='grid-item'][{int.Parse(providerIds[3])}]/div/div[@class='scene-cast-list']/a");
                if (sceneActorNodes != null)
                {
                    foreach (var sceneActor in sceneActorNodes)
                    {
                        var actorNode = detailsPageElements.SelectSingleNode($"//div[@class='video-performer']/a[./img[@title='{sceneActor.InnerText.Trim()}']]");
                        if (actorNode != null)
                        {
                            actorNodes.Add(actorNode);
                        }
                    }
                }
            }
            else
            {
                var mainActorNodes = detailsPageElements.SelectNodes("//div[@class='video-performer']/a");
                if (mainActorNodes != null)
                {
                    actorNodes.AddRange(mainActorNodes);
                }
            }
            if (actorNodes != null)
            {
                foreach (var actor in actorNodes)
                {
                    string actorName = actor.SelectSingleNode("./span/span")?.InnerText.Trim() ?? actor.InnerText.Trim();
                    string actorPhotoUrl = actor.SelectSingleNode("./img")?.GetAttributeValue("data-bgsrc", string.Empty);
                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            var directorNode = detailsPageElements.SelectSingleNode("//div[@class='director']/a/text()");
            if (directorNode != null && directorNode.InnerText.Split(':').Last().Trim() != "Unknown")
            {
                result.People.Add(new PersonInfo { Name = directorNode.InnerText.Split(':').Last().Trim(), Type = PersonKind.Director });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            string[] providerIds = sceneID[0].Split('|');
            string sceneURL = Helper.Decode(providerIds[0]);

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (detailsPageElements == null)
            {
                return result;
            }

            var imageNodes = detailsPageElements.SelectNodes("//div[@id='video-container-details']/div/section/a/picture/source[1]/@data-srcset | //div[@id='viewLargeBoxcoverCarousel']//noscript//@src");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    result.Add(new RemoteImageInfo { Url = img.GetAttributeValue(img.Name == "source" ? "data-srcset" : "src", string.Empty) });
                }
            }

            var galleryNode = detailsPageElements.SelectSingleNode("//div[@id='video-container-details']/div/section/div[2]/div[2]/a[@data-label='Gallery']");
            if (galleryNode != null)
            {
                var galleryHttp = await HTTP.Request($"{Helper.GetSearchBaseURL(siteNum)}{galleryNode.GetAttributeValue("href", string.Empty)}", cancellationToken);
                if (galleryHttp.IsOK)
                {
                    var galleryPageElement = HTML.ElementFromString(galleryHttp.Content);
                    var galleryImages = galleryPageElement.SelectNodes("//div[@class='item-grid item-grid-gallery']/div[@class='grid-item']/a/img/@data-src");
                    if (galleryImages != null)
                    {
                        foreach (var img in galleryImages)
                        {
                            result.Add(new RemoteImageInfo { Url = img.GetAttributeValue("data-src", string.Empty) });
                        }
                    }
                }
            }

            if (providerIds.Length > 3)
            {
                var sceneImage = detailsPageElements.SelectSingleNode($"//div[@class='item-grid item-grid-scene']/div[{providerIds[3]}]/a/img/@src");
                if (sceneImage != null)
                {
                    result.Add(new RemoteImageInfo { Url = sceneImage.GetAttributeValue("src", string.Empty) });
                }
            }

            return result;
        }
    }
}
