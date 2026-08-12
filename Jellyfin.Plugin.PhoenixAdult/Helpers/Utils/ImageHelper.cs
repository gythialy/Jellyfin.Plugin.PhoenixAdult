using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace PhoenixAdult.Helpers.Utils
{
    internal static class ImageHelper
    {
        // Jellyfin 默认对 Backdrop 的 MinWidth 过滤值（MediaBrowser.Model.Configuration.TypeOptions.DefaultImageOptions）
        // 低于该宽度的 backdrop 在自动下载时会被静默跳过（手动设置不受此限制）
        private const int BackdropMinWidth = 1280;

        public static async Task<List<RemoteImageInfo>> GetImagesSizeAndValidate(IEnumerable<RemoteImageInfo> images, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            var tasks = new List<Task<RemoteImageInfo>>();

            var cleanImages = Cleanup(images);

            var primaryList = cleanImages.Where(o => o.Type == ImageType.Primary).ToList();
            var backdropList = cleanImages.Where(o => o.Type == ImageType.Backdrop).ToList();
            var dublList = new List<RemoteImageInfo>();

            foreach (var image in primaryList)
            {
                tasks.Add(GetImageSizeAndValidate(image, cancellationToken));
            }

            foreach (var image in backdropList)
            {
                if (!primaryList.Where(o => o.Url == image.Url).Any())
                {
                    tasks.Add(GetImageSizeAndValidate(image, cancellationToken));
                }
                else
                {
                    dublList.Add(image);
                }
            }

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Logger.Error($"GetImagesSizeAndValidate error: \"{e}\"");

                await Analytics.Send(
                    new AnalyticsExeption
                    {
                        Request = string.Join(" | ", cleanImages.Select(o => o.Url)),
                    }, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                foreach (var task in tasks)
                {
                    if (!task.IsCompletedSuccessfully)
                    {
                        continue;
                    }

                    var res = task.Result;

                    if (res != null)
                    {
                        result.Add(res);
                    }
                }
            }

            if (result.Any())
            {
                foreach (var image in dublList)
                {
                    var res = result.Where(o => o.Url == image.Url);
                    if (res.Any())
                    {
                        var img = res.First();

                        var dublResult = new RemoteImageInfo
                        {
                            ProviderName = image.ProviderName,
                            Url = image.Url,
                            Type = ImageType.Backdrop,
                        };

                        // 与 primary 同 URL 的 backdrop 复用其尺寸；同样受 Jellyfin MinWidth 过滤影响，
                        // 宽度不足 1280 时不填尺寸，避免被自动下载流程跳过
                        if (!img.Width.HasValue || img.Width.Value >= BackdropMinWidth)
                        {
                            dublResult.Height = img.Height;
                            dublResult.Width = img.Width;
                        }

                        result.Add(dublResult);
                    }
                }
            }

            return result;
        }

        private static List<RemoteImageInfo> Cleanup(IEnumerable<RemoteImageInfo> images)
        {
            var clearImages = new List<RemoteImageInfo>();

            foreach (var image in images)
            {
                if (!clearImages.Where(o => o.Url == image.Url && o.Type == image.Type).Any())
                {
                    if (string.IsNullOrEmpty(image.ProviderName))
                    {
                        image.ProviderName = Plugin.Instance.Name;
                    }

                    clearImages.Add(image);
                }
            }

            var backdrops = clearImages.Where(o => o.Type == ImageType.Backdrop);
            if (backdrops.Any())
            {
                var firstBackdrop = backdrops.First();
                if (firstBackdrop != null && clearImages.Where(o => o.Type == ImageType.Primary).First().Url == firstBackdrop.Url)
                {
                    clearImages.Remove(firstBackdrop);
                    clearImages.Add(firstBackdrop);
                }
            }

            return clearImages;
        }

        private static async Task<RemoteImageInfo> GetImageSizeAndValidate(RemoteImageInfo item, CancellationToken cancellationToken)
        {
            if (Plugin.Instance.Configuration.DisableImageValidation)
            {
                return item;
            }

            var http = await HTTP.Request(item.Url, HttpMethod.Head, cancellationToken).ConfigureAwait(false);
            if (http.IsOK)
            {
                if (Plugin.Instance.Configuration.DisableImageSize)
                {
                    return item;
                }

                http = await HTTP.Request(item.Url, cancellationToken).ConfigureAwait(false);
                if (http.IsOK)
                {
                    try
                    {
                        var imageData = http.ContentStream;
                        var dimensions = GetImageDimensions(imageData);

                        if (dimensions.HasValue && dimensions.Value.width > 100)
                        {
                            var result = new RemoteImageInfo
                            {
                                ProviderName = item.ProviderName,
                                Url = item.Url,
                                Type = item.Type,
                            };

                            // Jellyfin 自动下载 backdrop 时按 MinWidth=1280 过滤（TypeOptions 默认值），
                            // 宽度不足的图会被静默跳过，导致 backdrop 无法自动设置（手动设置无此限制）。
                            // 对不满足尺寸的 backdrop 不填 Width/Height，Jellyfin 的过滤检查
                            // (image.Width.HasValue && image.Width.Value < minWidth) 不成立，即可正常下载。
                            if (item.Type != ImageType.Backdrop || dimensions.Value.width >= BackdropMinWidth)
                            {
                                result.Height = dimensions.Value.height;
                                result.Width = dimensions.Value.width;
                            }

                            return result;
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error($"ImageHelper error: \"{e}\"");
                    }
                }
            }

            return null;
        }

        private static (int width, int height)? GetImageDimensions(Stream stream)
        {
            using (var memoryStream = new MemoryStream())
            {
                stream.CopyTo(memoryStream);
                var bytes = memoryStream.ToArray();

                if (bytes.Length < 24)
                {
                    return null;
                }

                // Check for JPEG
                if (bytes[0] == 0xFF && bytes[1] == 0xD8)
                {
                    return GetJpegDimensions(bytes);
                }

                // Check for PNG
                else if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                {
                    return GetPngDimensions(bytes);
                }

                // Check for GIF
                else if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
                {
                    return GetGifDimensions(bytes);
                }

                return null;
            }
        }

        private static (int width, int height)? GetJpegDimensions(byte[] bytes)
        {
            int pos = 2; // Skip SOI marker (0xFFD8)

            while (pos < bytes.Length)
            {
                if (bytes[pos] != 0xFF)
                {
                    return null;
                }

                pos++; // Skip 0xFF
                byte marker = bytes[pos];
                pos++; // Move to length

                // Skip APP segments (0xE0-0xEF), comment (0xFE), etc.
                if (marker == 0xC0 || marker == 0xC1 || marker == 0xC2 || marker == 0xC3 ||
                    marker == 0xC5 || marker == 0xC6 || marker == 0xC7 || marker == 0xC9 ||
                    marker == 0xCA || marker == 0xCB || marker == 0xCD || marker == 0xCE ||
                    marker == 0xCF)
                {
                    // We found a SOF (Start of Frame) marker
                    if (pos + 5 < bytes.Length)
                    {
                        int height = (bytes[pos + 1] << 8) | bytes[pos + 2];
                        int width = (bytes[pos + 3] << 8) | bytes[pos + 4];
                        return (width, height);
                    }
                }
                else
                {
                    // Calculate segment length
                    int segmentLength = (bytes[pos] << 8) | bytes[pos + 1];
                    pos += segmentLength;
                }
            }

            return null;
        }

        private static (int width, int height)? GetPngDimensions(byte[] bytes)
        {
            if (bytes.Length < 24)
            {
                return null;
            }

            // PNG signature: 89 50 4E 47 0D 0A 1A 0A
            if (bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47 ||
                bytes[4] != 0x0D || bytes[5] != 0x0A || bytes[6] != 0x1A || bytes[7] != 0x0A)
            {
                return null;
            }

            // IHDR chunk: bytes 12-15 should be "IHDR"
            if (bytes[12] != 'I' || bytes[13] != 'H' || bytes[14] != 'D' || bytes[15] != 'R')
            {
                return null;
            }

            // Width: bytes 16-19 (big-endian)
            int width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];

            // Height: bytes 20-23 (big-endian)
            int height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];

            return (width, height);
        }

        private static (int width, int height)? GetGifDimensions(byte[] bytes)
        {
            if (bytes.Length < 10)
            {
                return null;
            }

            // GIF signature: "GIF87a" or "GIF89a"
            if (bytes[0] != 'G' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != '8' ||
                (bytes[4] != '7' && bytes[4] != '9') || bytes[5] != 'a')
            {
                return null;
            }

            // Width: bytes 6-7 (little-endian)
            int width = bytes[6] | (bytes[7] << 8);

            // Height: bytes 8-9 (little-endian)
            int height = bytes[8] | (bytes[9] << 8);

            return (width, height);
        }
    }
}
