using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    public class SitePrivate : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            var searchURL = Helper.GetSearchSearchURL(siteNum) + searchTitle.Replace(" ", "+");
            var http = await HTTP.Request(searchURL, cancellationToken, new Dictionary<string, string> { { "Accept-Language", "en" } });
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                var searchResults = doc.DocumentNode.SelectNodes("//ul[@id='search_results']//li[@class='card']");
                if (searchResults != null)
                {
                    foreach (var searchResult in searchResults)
                    {
                        var titleNode = searchResult.SelectSingleNode(".//h3/a");
                        if (titleNode == null)
                        {
                            continue;
                        }

                        var titleNoFormatting = Helper.ParseTitle(titleNode.InnerText.Trim(), siteNum);
                        var sceneURL = titleNode.GetAttributeValue("href", string.Empty);
                        if (string.IsNullOrEmpty(sceneURL))
                        {
                            continue;
                        }

                        var curID = Helper.Encode(sceneURL);

                        DateTime? premiereDate = null;
                        var dateNode = searchResult.SelectSingleNode(".//span[@class='scene-date']");
                        if (dateNode != null && DateTime.TryParseExact(dateNode.InnerText.Trim(), "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                        {
                            premiereDate = parsedDate;
                        }

                        var item = new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, curID } },
                            Name = titleNoFormatting,
                            PremiereDate = premiereDate,
                            SearchProviderName = Plugin.Instance.Name,
                        };

                        var imageNode = searchResult.SelectSingleNode(".//img");
                        if (imageNode != null)
                        {
                            var imageUrl = imageNode.GetAttributeValue("src", string.Empty);
                            if (string.IsNullOrEmpty(imageUrl))
                            {
                                imageUrl = imageNode.GetAttributeValue("data-src", string.Empty);
                            }

                            if (!string.IsNullOrEmpty(imageUrl))
                            {
                                item.ImageUrl = imageUrl;
                            }
                        }

                        result.Add(item);
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
            var movie = (Movie)result.Item;
            var sceneURL = Helper.Decode(sceneID[0]);
            var http = await HTTP.Request(sceneURL, cancellationToken, new Dictionary<string, string> { { "Accept-Language", "en" } });
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);
            movie.ExternalId = sceneURL;
            var titleNode = doc.DocumentNode.SelectSingleNode("//h1");
            if (titleNode != null)
            {
                movie.Name = Helper.ParseTitle(titleNode.InnerText, siteNum);
            }

            var descriptionNode = doc.DocumentNode.SelectSingleNode("//meta[@itemprop='description']");
            if (descriptionNode != null)
            {
                movie.Overview = descriptionNode.GetAttributeValue("content", string.Empty);
            }

            movie.AddStudio("Private");

            var taglineNode = doc.DocumentNode.SelectSingleNode("//li[@class='tag-sites']//a");
            var tagline = taglineNode?.InnerText.Trim() ?? "Private";
            movie.AddCollection(tagline);

            var genreNodes = doc.DocumentNode.SelectNodes("//li[@class='tag-tags']//a");
            if (genreNodes != null)
            {
                foreach (var genreLink in genreNodes)
                {
                    var genreName = genreLink.InnerText.ToLower();
                    movie.AddGenre(genreName);
                }
            }

            var dateNode = doc.DocumentNode.SelectSingleNode("//meta[@itemprop='uploadDate']");
            var date = dateNode?.GetAttributeValue("content", string.Empty) ?? string.Empty;
            if (DateTime.TryParse(date, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var actorNodes = doc.DocumentNode.SelectNodes("//li[@class='tag-models']//a");
            if (actorNodes != null)
            {
                foreach (var actorPage in actorNodes)
                {
                    var actorName = actorPage.InnerText;
                    var modelURL = actorPage.GetAttributeValue("href", string.Empty);
                    var actorHttp = await HTTP.Request(modelURL, cancellationToken);
                    if (actorHttp.IsOK)
                    {
                        var modelDoc = new HtmlDocument();
                        modelDoc.LoadHtml(actorHttp.Content);
                        var actorPhotoURL = modelDoc.DocumentNode.SelectSingleNode("//img/@srcset").GetAttributeValue("srcset", string.Empty).Split(',').Last().Split(' ').First().Trim();
                        result.AddPerson(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoURL });
                    }
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var sceneURL = Helper.Decode(sceneID[0]);
            var http = await HTTP.Request(sceneURL, cancellationToken, new Dictionary<string, string> { { "Accept-Language", "en" } });
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                var poster = doc.DocumentNode.SelectSingleNode("//meta[@itemprop='thumbnailUrl']").GetAttributeValue("content", string.Empty);
                images.Add(new RemoteImageInfo { Url = poster, Type = ImageType.Primary });

                var sceneId = sceneURL.Split('/').Last();
                var galleryPageUrl = $"https://www.private.com/gallery.php?type=highres&id={sceneId}&langx=en";
                var galleryHttp = await HTTP.Request(galleryPageUrl, cancellationToken, new Dictionary<string, string> { { "Accept-Language", "en" } });
                if (galleryHttp.IsOK)
                {
                    var galleryDoc = new HtmlDocument();
                    galleryDoc.LoadHtml(galleryHttp.Content);
                    foreach (var image in galleryDoc.DocumentNode.SelectNodes("//a[contains(@href, 'content/upload')]/@href"))
                    {
                        images.Add(new RemoteImageInfo { Url = image.GetAttributeValue("href", string.Empty), Type = ImageType.Backdrop });
                    }
                }
            }

            return images;
        }
    }
}
