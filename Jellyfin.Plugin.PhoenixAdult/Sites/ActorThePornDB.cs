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
            result.Item.SetProviderId(Plugin.Instance.Name, id);
            result.Item.Name = (string)details["name"];
            result.Item.Overview = (string)details["bio"];

            var extras = details["extras"];
            if (extras != null && extras.Type != JTokenType.Null)
            {
                if (DateTime.TryParseExact((string)extras["birthday"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var bornDate))
                {
                    result.Item.PremiereDate = bornDate;
                }

                var birthPlace = (string)extras["birthplace"];
                if (!string.IsNullOrEmpty(birthPlace))
                {
                    result.Item.ProductionLocations = new string[] { birthPlace };
                }
            }
            // Fallback to root if not in extras (though API sample suggests extras)
            else
            {
                if (DateTime.TryParseExact((string)details["born_on"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var bornDate))
                {
                    result.Item.PremiereDate = bornDate;
                }

                var birthPlace = (string)details["birthplace"];
                if (!string.IsNullOrEmpty(birthPlace))
                {
                    result.Item.ProductionLocations = new string[] { birthPlace };
                }
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
                    var addedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    // 1. details["image"]
                    var image = (string)details["image"];
                    if (!string.IsNullOrEmpty(image) && addedUrls.Add(image))
                    {
                        result.Add(new RemoteImageInfo
                        {
                            Url = image,
                            Type = ImageType.Primary,
                        });
                    }

                    // 2. details["face"]
                    var face = (string)details["face"];
                    if (!string.IsNullOrEmpty(face) && addedUrls.Add(face))
                    {
                        result.Add(new RemoteImageInfo
                        {
                            Url = face,
                            Type = ImageType.Primary,
                        });
                    }

                    // 3. details["posters"]
                    if (details["posters"] is JArray postersArray)
                    {
                        foreach (var posterToken in postersArray)
                        {
                            var posterUrl = (string)posterToken["url"];
                            if (!string.IsNullOrEmpty(posterUrl) && addedUrls.Add(posterUrl))
                            {
                                // Default to Primary as we cannot easily determine resolution/aspect ratio from this JSON structure
                                // without fetching headers for every image.
                                result.Add(new RemoteImageInfo
                                {
                                    Url = posterUrl,
                                    Type = ImageType.Primary,
                                });
                            }
                        }
                    }
                }
            }

            return result;
        }
    }
}
