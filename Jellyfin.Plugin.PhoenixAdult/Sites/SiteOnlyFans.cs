using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Newtonsoft.Json.Linq;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    public class SiteOnlyFans : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
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

            if (sceneID == null)
            {
                return result;
            }

            var sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var http = await HTTP.Request(sceneURL, HttpMethod.Post, cancellationToken).ConfigureAwait(false);
            var sceneData = HTML.ElementFromStream(http.ContentStream);
            var json = sceneData.SelectSingleText("//script[@type='application/ld+json']");
            JObject sceneDataJSON = null;
            if (!string.IsNullOrEmpty(json))
            {
                sceneDataJSON = JObject.Parse(json);
            }

            result.Item.ExternalId = sceneURL;

            result.Item.Name = sceneData.SelectSingleText("//h1[@class='title']");
            var studioName = sceneData.SelectSingleText("//div[@class='userInfo']//a");
            result.Item.AddStudio("Pornhub");

            if (!string.IsNullOrEmpty(studioName))
            {
                result.Item.AddStudio(studioName);
            }

            if (sceneDataJSON != null)
            {
                var date = (string)sceneDataJSON["uploadDate"];
                if (date != null)
                {
                    if (DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
                    {
                        result.Item.PremiereDate = sceneDateObj;
                    }
                }
            }

            var genreNode = sceneData.SelectNodesSafe("(//div[@class='categoriesWrapper'] | //div[@class='tagsWrapper'])/a");
            foreach (var genreLink in genreNode)
            {
                var genreName = genreLink.InnerText;

                result.Item.AddGenre(genreName);
            }

            var actorsNode = sceneData.SelectNodesSafe("//div[contains(@class, 'pornstarsWrapper')]/a");
            foreach (var actorLink in actorsNode)
            {
                string actorName = actorLink.Attributes["data-mxptext"].Value,
                        actorPhotoURL = actorLink.SelectSingleText(".//img[@class='avatar']/@src");

                result.People.Add(new PersonInfo
                {
                    Name = actorName,
                    ImageUrl = actorPhotoURL,
                });
            }

            return result;
        }
    }
}
