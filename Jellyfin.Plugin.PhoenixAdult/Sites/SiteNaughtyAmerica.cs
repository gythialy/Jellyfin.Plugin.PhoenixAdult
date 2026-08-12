using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
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
    public class SiteNaughtyAmerica : IProviderBase
    {
        private const string ApiBase = "https://api.naughtyapi.com/tools/scenes/scenes";

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            var searchURL = $"{ApiBase}?search={Uri.EscapeDataString(searchTitle)}";
            var searchData = await HTTP.Request(searchURL, cancellationToken);
            if (!searchData.IsOK)
            {
                return result;
            }

            var json = JObject.Parse(searchData.Content);
            if (json["data"] == null)
            {
                return result;
            }

            foreach (JObject scene in json["data"])
            {
                var sceneUrl = (string)scene["scene_url"];
                if (string.IsNullOrEmpty(sceneUrl))
                {
                    continue;
                }

                var res = new RemoteSearchResult
                {
                    Name = (string)scene["title"],
                };

                if (DateTime.TryParse((string)scene["published_date"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDate))
                {
                    res.PremiereDate = sceneDate;
                }

                var curID = Helper.Encode(sceneUrl);
                res.ProviderIds.Add(Plugin.Instance.Name, curID);
                result.Add(res);
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
            if (string.IsNullOrEmpty(sceneURL))
            {
                return result;
            }

            var idMatch = Regex.Match(sceneURL, @"-(\d+)$");
            if (!idMatch.Success)
            {
                return result;
            }

            var apiURL = $"{ApiBase}?id={idMatch.Groups[1].Value}";
            var http = await HTTP.Request(apiURL, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var json = JObject.Parse(http.Content);
            var sceneData = json["data"]?.First;
            if (sceneData == null)
            {
                return result;
            }

            result.Item.ExternalId = sceneURL;
            result.Item.Name = (string)sceneData["title"];

            var synopsis = (string)sceneData["synopsis"];
            if (!string.IsNullOrEmpty(synopsis))
            {
                result.Item.Overview = synopsis.Trim();
            }

            result.Item.AddStudio("Naughty America");

            var siteName = (string)sceneData["site_name"];
            if (!string.IsNullOrEmpty(siteName))
            {
                result.Item.AddStudio(siteName);
            }

            if (DateTime.TryParse((string)sceneData["published_date"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
            {
                result.Item.PremiereDate = sceneDateObj;
                result.Item.ProductionYear = sceneDateObj.Year;
            }

            if (sceneData["tags"] != null)
            {
                foreach (var tag in sceneData["tags"])
                {
                    var tagName = (string)tag;
                    if (!string.IsNullOrEmpty(tagName))
                    {
                        result.Item.AddGenre(tagName.Trim());
                    }
                }
            }

            var performers = sceneData["performers"];
            if (performers != null)
            {
                foreach (var gender in new[] { "female", "male", "transgender" })
                {
                    if (performers[gender] == null)
                    {
                        continue;
                    }

                    foreach (var performer in performers[gender])
                    {
                        var performerName = (string)performer;
                        if (!string.IsNullOrEmpty(performerName))
                        {
                            result.AddPerson(new PersonInfo { Name = performerName });
                        }
                    }
                }
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
            if (string.IsNullOrEmpty(sceneURL))
            {
                return result;
            }

            var idMatch = Regex.Match(sceneURL, @"-(\d+)$");
            if (!idMatch.Success)
            {
                return result;
            }

            var apiURL = $"{ApiBase}?id={idMatch.Groups[1].Value}";
            var http = await HTTP.Request(apiURL, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var json = JObject.Parse(http.Content);
            var sceneData = json["data"]?.First;
            if (sceneData == null)
            {
                return result;
            }

            var promo = (string)sceneData["promo_video_data"]?["aff_16mp4"];
            var trailer = (string)sceneData["trailers"]?["trailer_720"];
            var videoUrl = promo ?? trailer;
            if (string.IsNullOrEmpty(videoUrl))
            {
                return result;
            }

            var match = Regex.Match(videoUrl, @"/(\w+)/(\w+)/[^/]*\.mp4$");
            if (!match.Success)
            {
                return result;
            }

            var prefix = match.Groups[1].Value;
            var name = match.Groups[2].Value;
            var imageUrl = $"https://images4.naughtycdn.com/cms/nacmscontent/v1/scenes/{prefix}/{name}/scene/horizontal/1279x852c.jpg";

            result.Add(new RemoteImageInfo
            {
                Url = imageUrl,
                Type = ImageType.Primary,
            });
            result.Add(new RemoteImageInfo
            {
                Url = imageUrl,
                Type = ImageType.Backdrop,
            });

            return result;
        }
    }
}
