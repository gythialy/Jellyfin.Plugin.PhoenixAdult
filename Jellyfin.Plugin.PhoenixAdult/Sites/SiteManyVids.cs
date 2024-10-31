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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    public class SiteManyVids : IProviderBase
    {
        private const string TitleWatermark = " - Manyvids";

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            var splitedTitle = searchTitle.Split();
            if (!int.TryParse(splitedTitle[0], out var sceneIDx))
            {
                return result;
            }

            var sceneURL = new Uri(Helper.GetSearchBaseURL(siteNum) + $"/video/{sceneIDx}");
            var sceneID = new List<string> { Helper.Encode(sceneURL.AbsolutePath) };

            if (searchDate.HasValue)
            {
                sceneID.Add(searchDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }
            else
            {
                var sceneData = await HTML.ElementFromURL(sceneURL.AbsolutePath, cancellationToken).ConfigureAwait(false);
                var applicationLD = sceneData.SelectSingleText("//script[@type='application/ld+json']");
                var metadata = JsonConvert.DeserializeObject<ManyVidsMetadata>(applicationLD);
                sceneID.Add(DateTime.Parse(metadata.UploadDate).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }

            var searchResult = await Helper.GetSearchResultsFromUpdate(this, siteNum, sceneID.ToArray(), searchDate, cancellationToken).ConfigureAwait(false);
            if (searchResult.Any())
            {
                result.AddRange(searchResult);
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

            Logger.Info($"site: {siteNum})");
            Logger.Info($"searched ID: {sceneID}");
            Logger.Info($"cancellationToken: {cancellationToken}");

            string sceneURL = Helper.Decode(sceneID[0]),
                sceneDate = string.Empty;

            var searchResults = await GetDataFromAPI("https://www.manyvids.com/bff/store" + sceneURL, cancellationToken).ConfigureAwait(false);
            if (searchResults == null)
            {
                return result;
            }

            var videoPageElements = searchResults["data"];

            result.Item.ExternalId = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            result.Item.Name = (string)videoPageElements["title"];
            result.Item.Overview = (string)videoPageElements["description"];

            result.Item.AddStudio("ManyVids");
            var actor = new PersonInfo { Name = (string)videoPageElements["model"]["displayName"] };
            result.People.Add(actor);

            if (DateTime.TryParseExact((string)videoPageElements["launchDate"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
            {
                result.Item.PremiereDate = sceneDateObj;
             }

            foreach (var genre in videoPageElements["tagList"])
            {
                result.Item.AddGenre((string)genre["label"]);
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();

            if (sceneID == null)
            {
                return result;
            }

            var sceneURL = Helper.Decode(sceneID[0]);
            var searchResults = await GetDataFromAPI("https://www.manyvids.com/bff/store" + sceneURL, cancellationToken).ConfigureAwait(false);
            if (searchResults == null)
            {
                return result;
            }

            var videoPageElements = searchResults["data"];
            var imgUrl = (string)videoPageElements["screenshot"];
            if (!string.IsNullOrEmpty(imgUrl))
            {
                result.Add(new RemoteImageInfo
                {
                    Url = imgUrl,
                    Type = ImageType.Primary,
                });
            }

            return result;
        }

        public static async Task<JObject> GetDataFromAPI(string url, CancellationToken cancellationToken)
        {
            JObject json = null;

            Logger.Info($"Requesting data: {url}");
            var http = await HTTP.Request(url, cancellationToken).ConfigureAwait(false);
            if (http.IsOK)
            {
                json = JObject.Parse(http.Content);
            }
            else
            {
                Logger.Error($"Failed to get data ({http.StatusCode}): {http.Content}");
            }

            return json;
        }

        private class ManyVidsMetadata
        {
            public string UploadDate { get; set; }
        }
    }
}
