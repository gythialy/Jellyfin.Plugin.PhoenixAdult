using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PhoenixAdult.Helpers.Utils
{
    public static class CaptchaHelper
    {
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/68.0.3440.106 Safari/537.36";
        private static readonly ConcurrentDictionary<string, (DateTime Expiry, IDictionary<string, string> Cookies)> CachedCookies =
            new ConcurrentDictionary<string, (DateTime Expiry, IDictionary<string, string> Cookies)>(StringComparer.OrdinalIgnoreCase);

        private class TurnstileConfig
        {
            public string challenge { get; set; }

            public int difficulty { get; set; }

            public long timestamp { get; set; }

            public string returnTo { get; set; }
        }

        public static async Task<IDictionary<string, string>> NCookies(int[] siteNum, CancellationToken cancellationToken = default)
        {
            string baseUrl = Helper.GetSearchBaseURL(siteNum);
            return await GetVerifiedCookies(baseUrl, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<IDictionary<string, string>> GetVerifiedCookies(string baseUrl, CancellationToken cancellationToken = default, bool forceRefresh = false)
        {
            if (string.IsNullOrEmpty(baseUrl))
            {
                return new Dictionary<string, string>();
            }

            string baseUri = baseUrl.TrimEnd('/');
            if (!forceRefresh && CachedCookies.TryGetValue(baseUri, out var cached) && DateTime.UtcNow < cached.Expiry)
            {
                return new Dictionary<string, string>(cached.Cookies);
            }

            string galleryUrl = $"{baseUri}/video/gallery";
            var uri = new Uri(galleryUrl);

            var cookieContainer = new CookieContainer();
            var handler = new HttpClientHandler
            {
                CookieContainer = cookieContainer,
                Proxy = HTTP.Proxy,
                AutomaticDecompression = DecompressionMethods.All,
            };

            if (Plugin.Instance.Configuration.DisableSSLCheck)
            {
                handler.ServerCertificateCustomValidationCallback = (sender, certificate, chain, errors) => true;
            }

            using (handler)
            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) })
            {
                var request = new HttpRequestMessage(HttpMethod.Get, galleryUrl);
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
                request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");
                request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
                request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
                request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "none");
                request.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
                request.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");

                HttpResponseMessage r;
                try
                {
                    r = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.Warning($"[CaptchaHelper] GET {galleryUrl} failed: {ex.Message}");
                    return null;
                }

                if ((int)r.StatusCode == 429)
                {
                    Logger.Warning($"[CaptchaHelper] Rate-limited on {galleryUrl}");
                    return null;
                }

                string html = await r.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var match = Regex.Match(html, @"var\s+turnstileConfig\s*=\s*(\{.*?\});", RegexOptions.Singleline);
                if (!match.Success)
                {
                    var resultCookies = ExtractCookies(cookieContainer, uri);
                    CachedCookies[baseUri] = (DateTime.UtcNow.AddMinutes(30), resultCookies);
                    return resultCookies;
                }

                TurnstileConfig config;
                long nonce;
                try
                {
                    config = JsonSerializer.Deserialize<TurnstileConfig>(match.Groups[1].Value);
                    nonce = SolvePoW(config.challenge, config.difficulty);
                }
                catch (Exception ex)
                {
                    Logger.Warning($"[CaptchaHelper] PoW solve failed for {baseUri}: {ex.Message}");
                    return null;
                }

                string verifyUrl = $"{baseUri}/turnstile/verify";
                var payload = new
                {
                    nonce = nonce.ToString(CultureInfo.InvariantCulture),
                    timestamp = config.timestamp,
                    difficulty = config.difficulty,
                    environmentChecks = new
                    {
                        screenWidth = 1920,
                        screenHeight = 1080,
                        hasCanvas = true,
                        hasWebGL = true,
                        colorDepth = 24,
                        timezoneOffset = 300,
                        languages = "en-US,en",
                        platform = "Win32",
                        cookieEnabled = true,
                    },
                    returnTo = config.returnTo,
                };

                string jsonString = JsonSerializer.Serialize(payload);
                using (var postContent = new StringContent(jsonString, Encoding.UTF8, "application/json"))
                {
                    var postRequest = new HttpRequestMessage(HttpMethod.Post, verifyUrl)
                    {
                        Content = postContent,
                    };
                    postRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                    postRequest.Headers.TryAddWithoutValidation("Accept", "*/*");
                    postRequest.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                    postRequest.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
                    postRequest.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
                    postRequest.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
                    postRequest.Headers.TryAddWithoutValidation("Referer", galleryUrl);
                    postRequest.Headers.TryAddWithoutValidation("Origin", baseUri);

                    HttpResponseMessage vr;
                    try
                    {
                        vr = await client.SendAsync(postRequest, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"[CaptchaHelper] POST {verifyUrl} failed: {ex.Message}");
                        return null;
                    }

                    if (!vr.IsSuccessStatusCode)
                    {
                        Logger.Warning($"[CaptchaHelper] Verify POST returned {(int)vr.StatusCode} for {baseUri}");
                        return null;
                    }

                    string vrContent = await vr.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        using (var doc = JsonDocument.Parse(vrContent))
                        {
                            if (doc.RootElement.TryGetProperty("success", out var successProp) && successProp.GetBoolean())
                            {
                                Logger.Info($"[CaptchaHelper] Solved PoW for {baseUri} (nonce={nonce})");
                                var resultCookies = ExtractCookies(cookieContainer, uri);
                                CachedCookies[baseUri] = (DateTime.UtcNow.AddMinutes(30), resultCookies);
                                return resultCookies;
                            }
                            else
                            {
                                Logger.Warning($"[CaptchaHelper] Verify failed for {baseUri}: {vrContent}");
                                return null;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"[CaptchaHelper] Non-JSON verify response for {baseUri}: {ex.Message}");
                        return null;
                    }
                }
            }
        }

        public static long SolvePoW(string challenge, int difficulty)
        {
            using (var sha256 = SHA256.Create())
            {
                long nonce = 0;
                while (true)
                {
                    string input = $"{challenge}:{nonce}";
                    byte[] inputBytes = Encoding.UTF8.GetBytes(input);
                    byte[] hash = sha256.ComputeHash(inputBytes);

                    if (CheckLeadingZeroBits(hash, difficulty))
                    {
                        return nonce;
                    }

                    nonce++;
                }
            }
        }

        private static bool CheckLeadingZeroBits(byte[] byteArray, int requiredBits)
        {
            int fullBytes = requiredBits / 8;
            int remainingBits = requiredBits % 8;

            for (int i = 0; i < fullBytes; i++)
            {
                if (byteArray[i] != 0)
                {
                    return false;
                }
            }

            if (remainingBits > 0)
            {
                byte mask = (byte)(0xFF << (8 - remainingBits));
                if ((byteArray[fullBytes] & mask) != 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static IDictionary<string, string> ExtractCookies(CookieContainer cookieContainer, Uri uri)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var cookies = cookieContainer.GetCookies(uri);
            foreach (Cookie c in cookies)
            {
                dict[c.Name] = c.Value;
            }

            return dict;
        }
    }
}
