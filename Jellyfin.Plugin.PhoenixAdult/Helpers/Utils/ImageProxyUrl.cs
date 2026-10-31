using System;
using System.Collections.Generic;
using System.Text;
using MediaBrowser.Model.Providers;

namespace PhoenixAdult.Helpers.Utils
{
    /// <summary>
    /// 需要 Referer 防盗链的图片（JavBus 封面、phncdn 签名图等）不能把原始 CDN 地址直接交给 Jellyfin：
    /// 服务端下载图片时不带 Referer，浏览器直连也只会带本站 Referer，两种情况都会拿到 403，
    /// 结果就是「元数据有了、图片一格都看不到」。
    ///
    /// 这里把这类地址改写成插件自己的代理地址（<c>/PhoenixAdult/image?u=…</c>），由插件补上 Referer 再回传字节。
    /// 两种消费方要不同的形态：Jellyfin 服务端下载必须绝对地址（走回环），识别对话框是浏览器加载必须同源相对地址。
    /// </summary>
    internal static class ImageProxyUrl
    {
        private const string PathSuffix = "/PhoenixAdult/image?u=";

        /// <summary>
        /// 插件所在服务器的绝对 API 根地址（如 <c>http://127.0.0.1:8096/jellyfin</c>），由 IPluginServiceRegistrator 注入。
        /// 为空表示当前构建/环境没有代理端点（例如 Emby 构建），此时一律返回原始地址，行为与旧版一致。
        /// </summary>
        internal static string BaseUrl { get; set; }

        /// <summary>
        /// 代理端点是否可用。
        /// </summary>
        internal static bool IsAvailable => !string.IsNullOrEmpty(BaseUrl);

        /// <summary>
        /// 判断该地址是否需要走代理（即是否命中 Referer 规则）。
        /// </summary>
        /// <param name="url">图片地址。</param>
        /// <returns>需要代理时为 true。</returns>
        internal static bool NeedsProxy(string url)
            => !string.IsNullOrEmpty(url) && Helper.GetImageReferer(url) != null;

        /// <summary>
        /// 服务端消费方（Jellyfin 的图片下载器）：返回绝对代理地址。
        /// </summary>
        /// <param name="url">原始图片地址。</param>
        /// <returns>需要时返回代理地址，否则返回原地址。</returns>
        internal static string ForDownload(string url)
        {
            if (!NeedsProxy(url) || !IsAvailable)
            {
                return url;
            }

            return BaseUrl.TrimEnd('/') + PathSuffix + Encode(url);
        }

        /// <summary>
        /// 浏览器消费方（识别对话框的 &lt;img src&gt;）：返回同源相对地址，避免把服务器的回环地址塞给浏览器。
        /// </summary>
        /// <param name="url">原始图片地址。</param>
        /// <returns>需要时返回相对代理地址，否则返回原地址。</returns>
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
        /// 批量把 RemoteImageInfo 的地址改写成服务端可下载的代理地址（就地修改，调用方传的是刚构建好的列表）。
        /// </summary>
        /// <param name="images">图片信息集合。</param>
        /// <returns>同一个集合（便于链式赋值）。</returns>
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
        /// 把图片地址编码成代理参数（Base64Url，无填充）。
        /// </summary>
        /// <param name="value">原始地址。</param>
        /// <returns>编码结果。</returns>
        internal static string Encode(string value)
            => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>
        /// 还原代理参数；格式不对返回 null。
        /// </summary>
        /// <param name="value">编码结果。</param>
        /// <returns>原始地址；无法解码时返回 null。</returns>
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
