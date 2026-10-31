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
        // Jellyfin skips backdrops narrower than this when downloading automatically.
        private const int BackdropMinWidth = 1280;

        /// <summary>Fragment appended to a reused poster so both entries keep distinct URLs.</summary>
        private const string PrimaryAsBackdropSuffix = "#backdrop";

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

                        // A backdrop sharing the poster URL reuses its size, but stays size-less below the minimum.
                        if (!img.Width.HasValue || img.Width.Value >= BackdropMinWidth)
                        {
                            dublResult.Height = img.Height;
                            dublResult.Width = img.Width;
                        }

                        result.Add(dublResult);
                    }
                }
            }

            // Single-image sites have no backdrop: reuse the poster, distinguished by a fragment,
            // because the client dedupes by URL.
            if (result.Count > 0 && !result.Any(o => o.Type == ImageType.Backdrop))
            {
                var primary = result.FirstOrDefault(o => o.Type == ImageType.Primary);
                if (primary != null && !string.IsNullOrEmpty(primary.Url))
                {
                    var backdrop = new RemoteImageInfo
                    {
                        ProviderName = primary.ProviderName,
                        Url = primary.Url + PrimaryAsBackdropSuffix,
                        Type = ImageType.Backdrop,
                    };

                    // Same image as the poster: reuse the size, and omit it below the minimum.
                    if (!primary.Width.HasValue || primary.Width.Value >= BackdropMinWidth)
                    {
                        backdrop.Width = primary.Width;
                        backdrop.Height = primary.Height;
                    }

                    result.Add(backdrop);
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

            // A site returning banners only used to throw here.
            var backdrops = clearImages.Where(o => o.Type == ImageType.Backdrop).ToList();
            var primaryUrl = clearImages.FirstOrDefault(o => o.Type == ImageType.Primary)?.Url;
            if (backdrops.Count > 0 && primaryUrl != null && primaryUrl == backdrops[0].Url)
            {
                clearImages.Remove(backdrops[0]);
                clearImages.Add(backdrops[0]);
            }

            return clearImages;
        }

        private static async Task<RemoteImageInfo> GetImageSizeAndValidate(RemoteImageInfo item, CancellationToken cancellationToken)
        {
            if (Plugin.Instance.Configuration.DisableImageValidation)
            {
                return item;
            }

            // Referer protected CDNs must see the same headers as the final download,
            // otherwise valid images look unusable.
            var headers = Helper.GetImageRefererHeaders(item.Url);

            var http = await HTTP.Request(item.Url, HttpMethod.Head, cancellationToken, headers).ConfigureAwait(false);
            if (http.IsOK)
            {
                if (Plugin.Instance.Configuration.DisableImageSize)
                {
                    return item;
                }

                http = await HTTP.Request(item.Url, cancellationToken, headers).ConfigureAwait(false);
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

                            // Backdrops below the minimum width must stay size-less,
                            // otherwise Jellyfin skips them when downloading.
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

                // Check for WebP
                else if (bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F' &&
                         bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P')
                {
                    return GetWebpDimensions(bytes);
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
                    // JPEG SOF0: FF C0, length, precision, height, width.
                    if (pos + 6 < bytes.Length)
                    {
                        int height = (bytes[pos + 3] << 8) | bytes[pos + 4];
                        int width = (bytes[pos + 5] << 8) | bytes[pos + 6];
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

        private static (int width, int height)? GetWebpDimensions(byte[] bytes)
        {
            // RIFF container: "RIFF" + 4B size + "WEBP" + chunk
            if (bytes.Length < 30 || bytes[12] != 'V' || bytes[13] != 'P' || bytes[14] != '8')
            {
                return null;
            }

            var fourcc = (char)bytes[12] + ((char)bytes[13]).ToString() + ((char)bytes[14]).ToString() + ((char)bytes[15]).ToString();

            switch (fourcc)
            {
                case "VP8X":
                    // Extended: 24B canvas width-1, 24B canvas height-1 (little-endian), after 4B chunk size
                    if (bytes.Length < 30)
                    {
                        return null;
                    }

                    {
                        int canvasWidthMinusOne = bytes[24] | (bytes[25] << 8) | (bytes[26] << 16);
                        int canvasHeightMinusOne = bytes[27] | (bytes[28] << 8) | (bytes[29] << 16);
                        return (canvasWidthMinusOne + 1, canvasHeightMinusOne + 1);
                    }

                case "VP8L":
                    // Lossless: 1B signature 0x2F, then 14b width-1 + 14b height-1 (little-endian bit stream)
                    if (bytes.Length < 25 || bytes[20] != 0x2F)
                    {
                        return null;
                    }

                    {
                        uint bits = (uint)(bytes[21] | (bytes[22] << 8) | (bytes[23] << 16) | (bytes[24] << 24));
                        int width = (int)(bits & 0x3FFF) + 1;
                        int height = (int)((bits >> 14) & 0x3FFF) + 1;
                        return (width, height);
                    }

                case "VP8 ":
                    // Lossy: payload starts at 20 with the 3B frame tag, then the 3B start code
                    // (9d 01 2a), then 14b width-1 and 14b height-1 (little-endian).
                    if (bytes.Length < 30)
                    {
                        return null;
                    }

                    {
                        int pos = 23;
                        if (bytes[pos] != 0x9D || bytes[pos + 1] != 0x01 || bytes[pos + 2] != 0x2A)
                        {
                            return null;
                        }

                        pos += 3;
                        int width = (bytes[pos] | (bytes[pos + 1] << 8)) & 0x3FFF;
                        int height = (bytes[pos + 2] | (bytes[pos + 3] << 8)) & 0x3FFF;
                        return (width, height);
                    }

                default:
                    return null;
            }
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
