using System;
using System.Collections.Generic;
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
    public class SitePOVR : IProviderBase
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
                foreach (var searchResult in doc.DocumentNode.SelectNodes("//div[@class='thumbnail-wrap']/div"))
                {
                    var titleNoFormatting = searchResult.SelectSingleNode(".//div//h6[@class='thumbnail__title']").InnerText;
                    var sceneURL = searchResult.SelectSingleNode(".//a[@class='thumbnail__link']").GetAttributeValue("href", string.Empty);
                    var subSite = searchResult.SelectSingleNode(".//a[contains(@class, 'thumbnail__footer-link')]").InnerText;
                    var curID = Helper.Encode($"{sceneURL}|{subSite}");

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = $"{titleNoFormatting} [{subSite}]",
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
            var sceneData = sceneID[0].Split('|');
            var sceneURL = Helper.Decode(sceneData[0]);
            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);
            movie.ExternalId = sceneURL;
            var scriptText = doc.DocumentNode.SelectSingleNode("//script[@type='application/ld+json']").InnerText;
            var json = JObject.Parse(scriptText);

            movie.Name = (string)json["name"];
            movie.Overview = (string)json["description"];
            movie.AddStudio(sceneData[1]);
            movie.AddCollection(sceneData[1]);

            if (DateTime.TryParse((string)json["uploadDate"], out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            foreach (var actorLink in json["actor"])
            {
                var actorName = (string)actorLink["name"];
                var actorPage = (string)actorLink["@id"];
                var actorHttp = await HTTP.Request(actorPage, cancellationToken);
                if (actorHttp.IsOK)
                {
                    var actorDoc = new HtmlDocument();
                    actorDoc.LoadHtml(actorHttp.Content);
                    var actorScript = actorDoc.DocumentNode.SelectSingleNode("//script[@type='application/ld+json']").InnerText;
                    var actorJson = JObject.Parse(actorScript);
                    var actorPhotoUrl = (string)actorJson["image"];
                    result.AddPerson(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            foreach (var genreLink in doc.DocumentNode.SelectNodes("//ul[@class='category-link mb-2']/li/a"))
            {
                var genreName = genreLink.InnerText.Trim();
                movie.AddGenre(genreName);
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var sceneURL = Helper.Decode(sceneID[0].Split('|')[0]);
            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                var scriptText = doc.DocumentNode.SelectSingleNode("//script[@type='application/ld+json']").InnerText;
                var json = JObject.Parse(scriptText);
                var background = ((string)json["thumbnailUrl"]).Replace("tiny", "large");
                images.Add(new RemoteImageInfo { Url = background, Type = ImageType.Primary });
            }

            return images;
        }
    }
}
