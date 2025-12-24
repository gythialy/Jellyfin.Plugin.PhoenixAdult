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
using System.Net.Http;

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

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
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

                images = await ImageHelper.GetImagesSizeAndValidate(images, cancellationToken).ConfigureAwait(false);
            }

            return images;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return Helper.GetImageResponse(url, cancellationToken);
        }
    }
}
