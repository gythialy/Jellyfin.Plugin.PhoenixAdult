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
using System.Linq;

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class SiteNewSensationsOther : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var searchURL = Helper.GetSearchSearchURL(siteNum) + searchTitle.Replace(" ", "+").ToLower();
            var http = await HTTP.Request(searchURL, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                foreach (var searchResult in doc.DocumentNode.SelectNodes("//div[@class='update_details']"))
                {
                    var titleNoFormatting = searchResult.SelectSingleNode(".//a").InnerText.Trim();
                    var sceneURL = searchResult.SelectSingleNode(".//@href").GetAttributeValue("href", string.Empty);
                    var curID = Helper.Encode(sceneURL);

                    var date = searchResult.SelectSingleNode(".//div[@class='date_small']").InnerText.Split(':').Last().Trim();
                    var releaseDate = string.Empty;
                    if (DateTime.TryParseExact(date, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
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
            movie.Name = doc.DocumentNode.SelectSingleNode("//div[@class='update_title']").InnerText.Trim();
            movie.Overview = doc.DocumentNode.SelectSingleNode("//span[@class='update_description']").InnerText.Trim();
            movie.AddStudio("New Sensations");
            movie.AddCollection(Helper.GetSearchSiteName(siteNum));

            if (DateTime.TryParseExact(sceneID[0].Split('|')[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            foreach (var genreLink in doc.DocumentNode.SelectNodes("//span[@class='update_tags']/a"))
            {
                var genreName = Helper.ParseTitle(genreLink.InnerText.Replace("-", string.Empty).Trim(), siteNum);
                movie.AddGenre(genreName);
            }

            foreach (var actorLink in doc.DocumentNode.SelectNodes("//span[@class='update_models']/a"))
            {
                var actorName = actorLink.InnerText.Trim();
                var modelURL = actorLink.GetAttributeValue("href", string.Empty);
                var actorHttp = await HTTP.Request(modelURL, cancellationToken);
                if (actorHttp.IsOK)
                {
                    var actorDoc = new HtmlDocument();
                    actorDoc.LoadHtml(actorHttp.Content);
                    var actorPhotoURL = actorDoc.DocumentNode.SelectSingleNode("//div[@class='cell_top cell_thumb']/img").GetAttributeValue("src0_1x", string.Empty);
                    result.AddPerson(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoURL });
                }
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
                var posterNode = doc.DocumentNode.SelectSingleNode("//div[@class='mejs-layers']//img");
                if (posterNode != null)
                {
                    var posterUrl = posterNode.GetAttributeValue("src", string.Empty);
                    images.Add(new RemoteImageInfo { Url = posterUrl, Type = ImageType.Primary });
                }
            }

            return images;
        }
    }
}
