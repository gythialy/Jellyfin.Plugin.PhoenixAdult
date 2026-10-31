using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

#if __EMBY__
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Configuration;
#else
using System.Net.Http;
#endif

namespace PhoenixAdult.Providers
{
    public class MovieImageProvider : IRemoteImageProvider
    {
        public string Name => Plugin.Instance.Name;

        public bool Supports(BaseItem item) => item is Movie;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
            => new List<ImageType>
            {
                ImageType.Primary,
                ImageType.Backdrop,
            };

#if __EMBY__
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, LibraryOptions libraryOptions, CancellationToken cancellationToken)
#else
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
#endif
        {
            IEnumerable<RemoteImageInfo> images = new List<RemoteImageInfo>();
            if (item == null)
            {
                return images;
            }

            if (!item.ProviderIds.TryGetValue(this.Name, out var externalID))
            {
                return images;
            }

            var curID = externalID.Split('#');
            if (curID.Length < 3)
            {
                return images;
            }

            var siteNum = new int[2] { int.Parse(curID[0], CultureInfo.InvariantCulture), int.Parse(curID[1], CultureInfo.InvariantCulture) };

            var provider = Helper.GetProviderBySiteID(siteNum[0]);
            if (provider != null)
            {
                try
                {
                    images = await provider.GetImages(siteNum, curID.Skip(2).ToArray(), item, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Logger.Error($"GetImages error: \"{e}\"");

                    await Analytics.Send(
                        new AnalyticsExeption
                        {
                            Request = string.Join("#", curID.Skip(2)),
                            SiteNum = siteNum,
                            Exception = e,
                        }, cancellationToken).ConfigureAwait(false);
                }

                // 少数站点给的是协议相对地址（//host/path），ImageHelper 里的 Uri 解析会直接抛 UriFormatException
                images = NormalizeUrls(images);
                images = await ImageHelper.GetImagesSizeAndValidate(images, cancellationToken).ConfigureAwait(false);

                // 需要 Referer 防盗链的图（JavBus 封面等）Jellyfin 自己下载会 403，改成插件代理地址
                images = ImageProxyUrl.WrapForDownload(images);
            }

            return images;
        }

        private static IEnumerable<RemoteImageInfo> NormalizeUrls(IEnumerable<RemoteImageInfo> images)
        {
            if (images == null)
            {
                return Array.Empty<RemoteImageInfo>();
            }

            var normalized = new List<RemoteImageInfo>();
            foreach (var image in images)
            {
                if (image == null || string.IsNullOrEmpty(image.Url))
                {
                    continue;
                }

                if (image.Url.StartsWith("//", StringComparison.Ordinal))
                {
                    image.Url = "https:" + image.Url;
                }

                normalized.Add(image);
            }

            return normalized;
        }

#if __EMBY__
        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
#else
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
#endif
        {
            return Helper.GetImageResponse(url, cancellationToken);
        }
    }
}
