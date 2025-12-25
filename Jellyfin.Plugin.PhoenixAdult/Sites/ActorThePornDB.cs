using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Newtonsoft.Json.Linq;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    public class ActorThePornDB : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            var url = $"{Helper.GetSearchSearchURL(siteNum)}/performers?q={Uri.EscapeDataString(searchTitle)}";

            var headers = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(Plugin.Instance.Configuration.MetadataAPIToken))
            {
                headers.Add("Authorization", $"Bearer {Plugin.Instance.Configuration.MetadataAPIToken}");
                headers.Add("Accept", "application/json");
            }

            var http = await HTTP.Request(url, cancellationToken, headers).ConfigureAwait(false);
            if (!http.IsOK)
            {
                return result;
            }

            var json = JObject.Parse(http.Content);
            var data = json["data"];
            if (data == null || data.Type == JTokenType.Null)
            {
                return result;
            }

            foreach (var item in data)
            {
                var curID = (string)item["id"];
                var name = (string)item["name"];
                var image = (string)item["image"];

                var res = new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, curID } },
                    Name = name,
                    ImageUrl = image,
                    SearchProviderName = Plugin.Instance.Name,
                };

                result.Add(res);
            }

            return result;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem>()
            {
                Item = new Person(),
                HasMetadata = true,
            };

            var id = sceneID[0];
            var url = $"{Helper.GetSearchSearchURL(siteNum)}/performers/{id}";

            var headers = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(Plugin.Instance.Configuration.MetadataAPIToken))
            {
                headers.Add("Authorization", $"Bearer {Plugin.Instance.Configuration.MetadataAPIToken}");
                headers.Add("Accept", "application/json");
            }

            var http = await HTTP.Request(url, cancellationToken, headers).ConfigureAwait(false);
            if (!http.IsOK)
            {
                return result;
            }

            var json = JObject.Parse(http.Content);
            var details = json["data"];
            if (details == null || details.Type == JTokenType.Null)
            {
                return result;
            }

            result.Item.ExternalId = url;
            result.Item.Name = (string)details["name"];
            result.Item.Overview = (string)details["bio"];

            if (DateTime.TryParseExact((string)details["born_on"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var bornDate))
            {
                result.Item.PremiereDate = bornDate;
            }

            var birthPlace = (string)details["birthplace"];
            if (!string.IsNullOrEmpty(birthPlace))
            {
                result.Item.ProductionLocations = new string[] { birthPlace };
            }

            var aliases = details["aliases"];
            if (aliases != null && aliases.HasValues)
            {
                var aliasList = aliases.Select(a => (string)a).Where(s => !string.IsNullOrEmpty(s)).ToList();
                if (aliasList.Any())
                {
                    result.Item.OriginalTitle = string.Join(", ", aliasList);
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();

            if (sceneID == null || sceneID.Length == 0)
            {
                return result;
            }

            // We can re-fetch details or assume we might have it if Update was called recently?
            // But standard practice is to fetch.
            // Or we can try to extract image from item if it was set, but GetImages usually fetches fresh.
            // For ThePornDB, the image URL is in the details.

            var id = sceneID[0];
            var url = $"{Helper.GetSearchSearchURL(siteNum)}/performers/{id}";

            var headers = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(Plugin.Instance.Configuration.MetadataAPIToken))
            {
                headers.Add("Authorization", $"Bearer {Plugin.Instance.Configuration.MetadataAPIToken}");
                headers.Add("Accept", "application/json");
            }

            var http = await HTTP.Request(url, cancellationToken, headers).ConfigureAwait(false);
            if (http.IsOK)
            {
                var json = JObject.Parse(http.Content);
                var details = json["data"];
                if (details != null && details.Type != JTokenType.Null)
                {
                    var image = (string)details["image"];
                    if (!string.IsNullOrEmpty(image))
                    {
                        result.Add(new RemoteImageInfo
                        {
                            Url = image,
                            Type = ImageType.Primary,
                        });
                    }

                    // ThePornDB also has thumbnails, but "image" is usually the poster/profile.
                    // There are also "posters" in some responses, let's check NetworkMetadataAPI for similar structure.
                    // NetworkMetadataAPI checks "posters" then "image".

                    if (details["posters"] is JObject postersObject)
                    {
                        // Prioritize higher res if available, similar to NetworkMetadataAPI
                        // NetworkMetadataAPI: large ?? medium ?? small
                        var posterUrl = (string)postersObject["large"] ?? (string)postersObject["medium"] ?? (string)postersObject["small"];
                        if (!string.IsNullOrEmpty(posterUrl) && posterUrl != image)
                        {
                             // If different or we want to add it.
                             // Usually we just want one Primary.
                             // If "image" is present, use it. If not, try posters.
                             // Or if "image" is low res?
                             // Let's stick to "image" first as it's the main property, but add logic to use posters if image is missing
                             // or add both? Jellyfin picks one.
                        }
                    }
                }
            }

            return result;
        }
    }
}
