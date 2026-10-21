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

            // 从场景页 og:image 取图片（从 trailer 路径推 scenes/{prefix}/{name} 的 name 常错，如 DirtyWivesClub → lucycodey）
            var sceneUrl = (string)sceneData["scene_url"];
            if (!string.IsNullOrEmpty(sceneUrl))
            {
                var pageHtml = await HTML.ElementFromURL(sceneUrl, cancellationToken);
                var ogNode = pageHtml?.SelectSingleNode("//meta[@property='og:image']");
                var ogImageUrl = ogNode?.GetAttributeValue("content", string.Empty);
                if (!string.IsNullOrEmpty(ogImageUrl))
                {
                    result.Add(new RemoteImageInfo { Url = ogImageUrl, Type = ImageType.Primary });
                    result.Add(new RemoteImageInfo { Url = ogImageUrl, Type = ImageType.Backdrop });
                    return result;
                }
            }

            // og:image 拿不到（场景页对无 Cookie 请求常返回空 202）时，按 TPDB networkNaughtyAmericaAPI 的推导：
            // 从 promo_video_data / trailers.trailer_720 里取 "abbrev/title"，再拼
            // https://images{cdn}.naughtycdn.com/cms/nacmscontent/v1/scenes/{abbrev}/{title}/scene/horizontal/{size}c.{ext}
            // 并用 HEAD 逐个校验（CDN 序号、尺寸、扩展名都不固定，光拼不验会 404）。
            var imageInsert = string.Empty;
            if (sceneData["promo_video_data"] is JObject promoObj)
            {
                foreach (var property in promoObj.Properties())
                {
                    var promoUrl = (string)property.Value;
                    if (string.IsNullOrEmpty(promoUrl))
                    {
                        continue;
                    }

                    var promoMatch = Regex.Match(promoUrl, @"promo/(.*?/.*?)/");
                    if (promoMatch.Success)
                    {
                        imageInsert = promoMatch.Groups[1].Value;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(imageInsert))
            {
                var trailer = (string)sceneData["trailers"]?["trailer_720"];
                if (!string.IsNullOrEmpty(trailer))
                {
                    var trailerMatch = Regex.Match(trailer, @"\.com/(\w+)/trailers/(\w+)_");
                    if (trailerMatch.Success)
                    {
                        var abbrev = trailerMatch.Groups[1].Value;
                        var title = trailerMatch.Groups[2].Value;
                        if (title.Contains("trailer", StringComparison.OrdinalIgnoreCase))
                        {
                            title = Regex.Match(title, @"(.*)trailer").Groups[1].Value;
                        }

                        if (title.StartsWith(abbrev, StringComparison.OrdinalIgnoreCase))
                        {
                            title = title.Substring(abbrev.Length);
                        }

                        imageInsert = $"{abbrev}/{title}";
                    }
                }
            }

            if (string.IsNullOrEmpty(imageInsert))
            {
                return result;
            }

            foreach (var cdn in Enumerable.Range(1, 5))
            {
                foreach (var ext in new[] { "jpg", "webp" })
                {
                    foreach (var size in new[] { "1499x944c", "1279x852c" })
                    {
                        var candidate = $"https://images{cdn}.naughtycdn.com/cms/nacmscontent/v1/scenes/{imageInsert}/scene/horizontal/{size}.{ext}";
                        if (!await HTTP.IsUrlLoadable(candidate, cancellationToken).ConfigureAwait(false))
                        {
                            continue;
                        }

                        result.Add(new RemoteImageInfo { Url = candidate, Type = ImageType.Primary });
                        result.Add(new RemoteImageInfo { Url = candidate, Type = ImageType.Backdrop });
                        return result;
                    }
                }
            }

            return result;
        }
    }
}
