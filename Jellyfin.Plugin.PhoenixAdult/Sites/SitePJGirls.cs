using System;
using System.Collections.Generic;
using System.Globalization;
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
    public class SitePJGirls : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var searchURL = Helper.GetSearchSearchURL(siteNum) + searchTitle.Replace(" ", "+");
            var http = await HTTP.Request(searchURL, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                foreach (var searchResult in doc.DocumentNode.SelectNodes("//div[@class='thumb video']"))
                {
                    var titleNoFormatting = searchResult.SelectSingleNode("./h2").InnerText.Trim();
                    var sceneURL = searchResult.SelectSingleNode("./a").GetAttributeValue("href", string.Empty);
                    var curID = Helper.Encode(sceneURL);
                    var releaseDate = string.Empty;
                    var dateNode = searchResult.SelectSingleNode("./a/div/span[2]");
                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = $"{titleNoFormatting} [PJGirls] {releaseDate}",
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
            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);
            movie.ExternalId = sceneURL;
            movie.Name = doc.DocumentNode.SelectSingleNode("//title").InnerText.Split(new[] { "- porn video" }, StringSplitOptions.None)[0].Trim();
            movie.Overview = doc.DocumentNode.SelectSingleNode("//div[@class='text']/p").InnerText.Trim();
            movie.AddStudio("PJGirls");
            movie.AddCollection("PJGirls");

            var dateNode = doc.DocumentNode.SelectSingleNode("//div[@class='info']/h3[1]");
            if (dateNode != null && DateTime.TryParseExact(dateNode.InnerText.Trim(), "MMMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            foreach (var genreLink in doc.DocumentNode.SelectNodes("//div[@class='detailTagy clear']/a"))
            {
                var genreName = genreLink.InnerText.Trim();
                movie.AddGenre(genreName);
            }

            foreach (var actorLink in doc.DocumentNode.SelectNodes("//div[@class='info']/h3[3]/a"))
            {
                var actorName = actorLink.InnerText.Trim();
                var actorPageURL = Helper.GetSearchBaseURL(siteNum) + actorLink.GetAttributeValue("href", string.Empty);
                var actorHttp = await HTTP.Request(actorPageURL, cancellationToken);
                var actorPhotoURL = string.Empty;
                if (actorHttp.IsOK)
                {
                    var actorDoc = new HtmlDocument();
                    actorDoc.LoadHtml(actorHttp.Content);
                    var actorPhotoNode = actorDoc.DocumentNode.SelectSingleNode("//div[@class='image']/img");
                    if (actorPhotoNode != null)
                    {
                        actorPhotoURL = actorPhotoNode.GetAttributeValue("src", string.Empty);
                    }

                    if (!string.IsNullOrEmpty(actorPhotoURL) && !actorPhotoURL.StartsWith("http"))
                    {
                        actorPhotoURL = Helper.GetSearchBaseURL(siteNum) + actorPhotoURL;
                    }
                }

                result.AddPerson(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoURL });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var sceneURL = Helper.Decode(sceneID[0]);
            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                var posterNode = doc.DocumentNode.SelectSingleNode("//div[@class='videoObal']/img");
                if (posterNode != null)
                {
                    var posterUrl = posterNode.GetAttributeValue("src", string.Empty);
                    if (!posterUrl.StartsWith("http"))
                    {
                        posterUrl = Helper.GetSearchBaseURL(siteNum) + posterUrl;
                    }

                    images.Add(new RemoteImageInfo { Url = posterUrl, Type = ImageType.Primary });
                }

                var backdropNodes = doc.DocumentNode.SelectNodes("//div[@class='thumb mini']/a");
                if (backdropNodes != null)
                {
                    foreach (var node in backdropNodes)
                    {
                        var backdropUrl = node.GetAttributeValue("href", string.Empty);
                        if (!backdropUrl.StartsWith("http"))
                        {
                            backdropUrl = Helper.GetSearchBaseURL(siteNum) + backdropUrl;
                        }

                        images.Add(new RemoteImageInfo { Url = backdropUrl, Type = ImageType.Backdrop });
                    }
                }
            }

            return images;
        }
    }
}
