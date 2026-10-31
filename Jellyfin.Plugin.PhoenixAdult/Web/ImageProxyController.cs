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
    /// Proxy for images that require a Referer: neither the Jellyfin downloader nor a browser sends one.
    ///
    /// Only hosts with a referer rule are proxied (others get 400).
    /// </summary>
    [ApiController]
    [Route("PhoenixAdult")]
    public class ImageProxyController : ControllerBase
    {
        private const int TimeoutSeconds = 30;

        /// <summary>
        /// Proxy one image.
        /// </summary>
        [HttpGet("image")]
        [AllowAnonymous]
        public async Task<IActionResult> GetImage([FromQuery] string u, CancellationToken cancellationToken)
        {
            var url = ImageProxyUrl.Decode(u);
            if (string.IsNullOrEmpty(url) || !ImageProxyUrl.NeedsProxy(url))
            {
                // Reject hosts without a rule so this cannot become an open proxy.
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
        /// Read the declared content type by name (it lives on the content headers).
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
        /// Prefer the magic bytes; fall back to the declared type.
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
