using System;
using System.Collections.Generic;
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
    public class SitePrivate : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var searchURL = Helper.GetSearchSearchURL(siteNum) + searchTitle.Replace(" ", "+");
            var http = await HTTP.Request(searchURL, cancellationToken, new Dictionary<string, string> { { "Accept-Language", "en" } });
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                foreach (var searchResult in doc.DocumentNode.SelectNodes("//ul[@id='search_results']//li[@class='card']"))
                {
                    var titleNoFormatting = Helper.ParseTitle(searchResult.SelectSingleNode(".//h3/a").InnerText.Trim(), siteNum);
                    var sceneURL = searchResult.SelectSingleNode(".//h3/a").GetAttributeValue("href", string.Empty);
                    var curID = Helper.Encode(sceneURL);

                    var date = searchResult.SelectSingleNode(".//span[@class='scene-date']");
                    var releaseDate = string.Empty;
                    if (date != null && DateTime.TryParse(date.InnerText.Trim(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = $"{titleNoFormatting} [Private] {releaseDate}",
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
            movie.Name = Helper.ParseTitle(doc.DocumentNode.SelectSingleNode("//h1").InnerText, siteNum);
            movie.Overview = doc.DocumentNode.SelectSingleNode("//meta[@itemprop='description']").GetAttributeValue("content", string.Empty);
            movie.AddStudio("Private");

            var tagline = doc.DocumentNode.SelectSingleNode("//li[@class='tag-sites']//a")?.InnerText.Trim() ?? "Private";
            movie.AddCollection(tagline);

            foreach (var genreLink in doc.DocumentNode.SelectNodes("//li[@class='tag-tags']//a"))
            {
                var genreName = genreLink.InnerText.ToLower();
                movie.AddGenre(genreName);
            }

            var date = doc.DocumentNode.SelectSingleNode("//meta[@itemprop='uploadDate']").GetAttributeValue("content", string.Empty);
            if (DateTime.TryParse(date, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            foreach (var actorPage in doc.DocumentNode.SelectNodes("//li[@class='tag-models']//a"))
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
                    foreach (var image in galleryDoc.DocumentNode.SelectNodes("//a/@href"))
                    {
                        images.Add(new RemoteImageInfo { Url = image.GetAttributeValue("href", string.Empty), Type = ImageType.Backdrop });
                    }
                }
            }

            return images;
        }
    }
}
