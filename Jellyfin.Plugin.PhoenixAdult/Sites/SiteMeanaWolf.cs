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
    public class SiteMeanaWolf : IProviderBase
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
                foreach (var searchResult in doc.DocumentNode.SelectNodes("//div[@class='videoBlock']"))
                {
                    var titleNoFormatting = searchResult.SelectSingleNode("./p/a").InnerText.Trim();
                    var sceneURL = searchResult.SelectSingleNode("./p/a").GetAttributeValue("href", string.Empty);
                    var curID = Helper.Encode(sceneURL);
                    var image = searchResult.SelectSingleNode(".//img[@class='video_placeholder']").GetAttributeValue("src", string.Empty);
                    if (!image.StartsWith("http"))
                    {
                        image = "https:" + image;
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = $"{titleNoFormatting} [Meana Wolf]",
                        SearchProviderName = Plugin.Instance.Name,
                        ImageUrl = image,
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
            movie.Name = doc.DocumentNode.SelectSingleNode("//div[@class='trailerArea']/h3").InnerText.Trim();
            movie.Overview = doc.DocumentNode.SelectSingleNode("//div[@class='trailerContent']/p").InnerText.Trim();
            movie.AddStudio("Meana Wolf");
            movie.AddCollection("Meana Wolf");

            var dateNode = doc.DocumentNode.SelectSingleNode("//div[@class='videoContent']/ul/li[2]");
            if (dateNode != null && DateTime.TryParseExact(dateNode.InnerText.Replace("ADDED:", string.Empty).Trim(), "MMMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            foreach (var genreLink in doc.DocumentNode.SelectNodes("//div[@class='videoContent']/ul/li[position()=last()]/a"))
            {
                var genreName = genreLink.InnerText.Trim();
                movie.AddGenre(genreName);
            }

            foreach (var actorLink in doc.DocumentNode.SelectNodes("//div[@class='videoContent']/ul/li[3]/a"))
            {
                var actorName = actorLink.InnerText.Trim();
                var actorPhotoURL = string.Empty;
                var actorPageURL = actorLink.GetAttributeValue("href", string.Empty);
                if (!string.IsNullOrEmpty(actorPageURL))
                {
                    var actorHttp = await HTTP.Request(actorPageURL, cancellationToken);
                    if (actorHttp.IsOK)
                    {
                        var actorDoc = new HtmlDocument();
                        actorDoc.LoadHtml(actorHttp.Content);
                        var actorPhotoNode = actorDoc.DocumentNode.SelectSingleNode("//div[@class='modelBioPic']/img");
                        if (actorPhotoNode != null)
                        {
                            actorPhotoURL = actorPhotoNode.GetAttributeValue("src0_3x", string.Empty);
                        }

                        if (!actorPhotoURL.StartsWith("http"))
                        {
                            actorPhotoURL = Helper.GetSearchBaseURL(siteNum) + actorPhotoURL;
                        }
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

                var posterNode = doc.DocumentNode.SelectSingleNode("//video[contains(@id,'preview')]/@poster");
                if (posterNode != null)
                {
                    var posterURL = posterNode.GetAttributeValue("poster", string.Empty);
                    if (!string.IsNullOrEmpty(posterURL))
                    {
                        if (!posterURL.StartsWith("http"))
                        {
                            posterURL = "https:" + posterURL;
                        }

                        images.Add(new RemoteImageInfo { Url = posterURL, Type = ImageType.Primary });
                    }
                }
            }

            return images;
        }
    }
}
