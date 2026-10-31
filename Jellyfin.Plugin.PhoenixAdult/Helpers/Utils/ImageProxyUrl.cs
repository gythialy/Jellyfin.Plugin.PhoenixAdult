using System;
using System.Collections.Generic;
using System.Text;
using MediaBrowser.Model.Providers;

namespace PhoenixAdult.Helpers.Utils
{
    /// <summary>
    /// Some CDNs reject requests without a Referer, and neither the Jellyfin downloader
    /// nor a browser sends the expected one, so such URLs are rewritten to the plugin proxy.
    ///
    /// The downloader needs an absolute URL, the identify dialog a same-origin one.
    /// </summary>
    internal static class ImageProxyUrl
    {
        private const string PathSuffix = "/PhoenixAdult/image?u=";

        /// <summary>
        /// Absolute API root captured at startup.
        /// Empty means no proxy is available (for example the Emby build) and URLs are left unchanged.
        /// </summary>
        internal static string BaseUrl { get; set; }

        /// <summary>
        /// Whether the proxy endpoint is available.
        /// </summary>
        internal static bool IsAvailable => !string.IsNullOrEmpty(BaseUrl);

        /// <summary>
        /// Whether this URL needs the proxy, i.e. a referer rule matches.
        /// </summary>
        internal static bool NeedsProxy(string url)
            => !string.IsNullOrEmpty(url) && Helper.GetImageReferer(url) != null;

        /// <summary>
        /// Absolute proxy URL for the server-side downloader.
        /// </summary>
        internal static string ForDownload(string url)
        {
            if (!NeedsProxy(url) || !IsAvailable)
            {
                return url;
            }

            return BaseUrl.TrimEnd('/') + PathSuffix + Encode(url);
        }

        /// <summary>
        /// Same-origin proxy URL for the browser.
        /// </summary>
        internal static string ForBrowser(string url)
        {
            if (!NeedsProxy(url) || !IsAvailable)
            {
                return url;
            }

            var pathBase = new Uri(BaseUrl).AbsolutePath.TrimEnd('/');
            return pathBase + PathSuffix + Encode(url);
        }

        /// <summary>
        /// Rewrite a result set for server-side download, in place.
        /// </summary>
        internal static List<RemoteImageInfo> WrapForDownload(IEnumerable<RemoteImageInfo> images)
        {
            var result = new List<RemoteImageInfo>();
            if (images == null)
            {
                return result;
            }

            foreach (var image in images)
            {
                if (image == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(image.Url))
                {
                    image.Url = ForDownload(image.Url);
                }

                if (!string.IsNullOrEmpty(image.ThumbnailUrl))
                {
                    image.ThumbnailUrl = ForDownload(image.ThumbnailUrl);
                }

                result.Add(image);
            }

            return result;
        }

        /// <summary>
        /// Encode to base64url without padding.
        /// </summary>
        internal static string Encode(string value)
            => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>
        /// Decode a base64url value; null when malformed.
        /// </summary>
        internal static string Decode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var padded = value.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2:
                    padded += "==";
                    break;
                case 3:
                    padded += "=";
                    break;
                case 1:
                    return null;
            }

            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
