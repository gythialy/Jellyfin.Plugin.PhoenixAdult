using System;
using System.Collections.Generic;
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

#if !__EMBY__
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    /// <summary>
    /// ModelMediaAsia official API source.
    /// The upstream provider was removed and the API domain is unreachable,
    /// so this returns an empty result instead of throwing until the domain is back.
    /// </summary>
    public class SiteModelMediaAsia : IProviderBase
    {
        /// <summary>API base URL.</summary>
        public const string ApiBase = "https://model-api.bvncmsldo.com/api/v2";

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            var json = await Fetcher.Json($"{ApiBase}/search?keyword={Uri.EscapeDataString(searchTitle)}", cancellationToken).ConfigureAwait(false);
            var videos = json?["data"]?["videos"] as JArray;
            if (videos == null)
            {
                return result;
            }

            foreach (var video in videos)
            {
                var serial = (string)video["serial_number"];
                if (string.IsNullOrEmpty(serial))
                {
                    continue;
                }

                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, Helper.Encode(serial) } },
                    Name = (string)video["title_cn"] ?? (string)video["title"] ?? serial,
                    ImageUrl = (string)video["cover"],
                    PremiereDate = ToDate((long?)video["published_at"]),
                });
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

            if (sceneID == null || sceneID.Length == 0)
            {
                return result;
            }

            var id = Helper.Decode(sceneID[0]);
            var json = await Fetcher.Json($"{ApiBase}/videos/{Uri.EscapeDataString(id)}", cancellationToken).ConfigureAwait(false);
            var data = json?["data"];
            if (data == null)
            {
                return result;
            }

            var serial = (string)data["serial_number"] ?? id;
            result.Item.Name = (string)data["title_cn"] ?? (string)data["title"] ?? serial;
            result.Item.OriginalTitle = serial;
            result.Item.Overview = (string)data["description_cn"] ?? (string)data["description"];
            result.Item.PremiereDate = ToDate((long?)data["published_at"]);
            result.Item.AddStudio("ModelMediaAsia");

            var duration = (int?)data["duration"];
            if (duration.HasValue && duration.Value > 0)
            {
                result.Item.RunTimeTicks = TimeSpan.FromSeconds(duration.Value).Ticks;
            }

            var cover = (string)data["cover"];
            if (!string.IsNullOrEmpty(cover))
            {
                result.Item.SetImage(new ItemImageInfo { Path = cover, Type = ImageType.Primary }, 0);
            }

            foreach (var tag in data["tags"] as JArray ?? new JArray())
            {
                var genre = (string)tag["name_cn"] ?? (string)tag["name"];
                if (!string.IsNullOrEmpty(genre))
                {
                    result.Item.AddGenre(genre);
                }
            }

            foreach (var model in data["models"] as JArray ?? new JArray())
            {
                var name = (string)model["name_cn"] ?? (string)model["name"];
                if (!string.IsNullOrEmpty(name))
                {
                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = name, Type = PersonKind.Actor });
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

            var id = Helper.Decode(sceneID[0]);
            var json = await Fetcher.Json($"{ApiBase}/videos/{Uri.EscapeDataString(id)}", cancellationToken).ConfigureAwait(false);
            var cover = (string)json?["data"]?["cover"];
            if (!string.IsNullOrEmpty(cover))
            {
                result.Add(new RemoteImageInfo { Type = ImageType.Primary, Url = cover });
                result.Add(new RemoteImageInfo { Type = ImageType.Backdrop, Url = cover });
            }

            return result;
        }

        private static DateTime? ToDate(long? unixMilliseconds)
        {
            if (!unixMilliseconds.HasValue || unixMilliseconds.Value <= 0)
            {
                return null;
            }

            return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds.Value).UtcDateTime;
        }
    }
}
