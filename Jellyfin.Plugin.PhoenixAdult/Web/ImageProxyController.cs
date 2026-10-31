#if !__EMBY__
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Web
{
    /// <summary>
    /// 图片代理端点：JavBus 封面、phncdn 签名图这类图片要求 Referer，而 Jellyfin 下载远程图片时不带 Referer、
    /// 浏览器直连也只带本站 Referer，两者都会 403，最终表现为「元数据抓到了，但海报/背景图一张都没有」。
    ///
    /// 插件把这类地址改写成 <c>/PhoenixAdult/image?u=&lt;base64url&gt;</c>：本端点按 Referer 规则取回图片字节再回传，
    /// 服务端下载与浏览器显示都能拿到图。只代理命中 Referer 规则的主机，避免变成通用代理。
    /// </summary>
    [ApiController]
    [Route("PhoenixAdult")]
    public class ImageProxyController : ControllerBase
    {
        private const int TimeoutSeconds = 30;

        /// <summary>
        /// 代理一张需要 Referer 的图片。
        /// </summary>
        /// <param name="u">Base64Url 编码的原始图片地址。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>图片字节。</returns>
        [HttpGet("image")]
        [AllowAnonymous]
        public async Task<IActionResult> GetImage([FromQuery] string u, CancellationToken cancellationToken)
        {
            var url = ImageProxyUrl.Decode(u);
            if (string.IsNullOrEmpty(url) || !ImageProxyUrl.NeedsProxy(url))
            {
                // 只代理命中 Referer 规则的主机；其他地址一律拒绝，避免被当作开放代理使用
                return BadRequest();
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return BadRequest();
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

            try
            {
                var response = await HTTP.Request(url, cts.Token, Helper.GetImageRefererHeaders(url), null).ConfigureAwait(false);
                if (!response.IsOK || response.ContentStream == null)
                {
                    Logger.Info($"[ImageProxy] {(int)response.StatusCode} for '{url}'");
                    return NotFound();
                }

                using var buffer = new MemoryStream();
                await response.ContentStream.CopyToAsync(buffer, cts.Token).ConfigureAwait(false);
                var bytes = buffer.ToArray();
                if (bytes.Length == 0)
                {
                    return NotFound();
                }

                Logger.Info($"[ImageProxy] served {bytes.Length} bytes for '{url}'");
                Response.Headers["Cache-Control"] = "public, max-age=86400";
                return this.File(bytes, DetectContentType(bytes, GetDeclaredType(response.Headers)));
            }
            catch (OperationCanceledException)
            {
                Logger.Info($"[ImageProxy] timeout for '{url}'");
                return NotFound();
            }
            catch (Exception e)
            {
                Logger.Error($"[ImageProxy] failed for '{url}': {e.Message}");
                return NotFound();
            }
        }

        /// <summary>
        /// 读取响应头里的 Content-Type（System.Net.Http 把内容头放在 HttpContentHeaders 上，
        /// 这里只能按名字取）。
        /// </summary>
        private static string GetDeclaredType(System.Net.Http.Headers.HttpResponseHeaders headers)
        {
            if (headers != null && headers.TryGetValues("Content-Type", out var values))
            {
                return values.FirstOrDefault();
            }

            return null;
        }

        /// <summary>
        /// 判断图片真实类型：先看魔数，再用响应头兜底（部分 CDN 会把图片当 octet-stream 返回）。
        /// </summary>
        private static string DetectContentType(byte[] bytes, string declared)
        {
            if (bytes.Length > 3)
            {
                if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                {
                    return "image/jpeg";
                }

                if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                {
                    return "image/png";
                }

                if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
                {
                    return "image/gif";
                }

                if (bytes.Length > 11
                    && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
                    && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
                {
                    return "image/webp";
                }
            }

            if (!string.IsNullOrEmpty(declared)
                && declared.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return declared;
            }

            return "application/octet-stream";
        }
    }
}
#endif
