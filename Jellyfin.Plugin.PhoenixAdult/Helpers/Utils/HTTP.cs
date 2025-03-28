using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using FlareSolverrSharp;
using Microsoft.Extensions.Caching.Abstractions;
using Microsoft.Extensions.Caching.InMemory;
using MihaZupan;

namespace PhoenixAdult.Helpers.Utils
{
    internal static class HTTP
    {
        private const int DefaultTimeoutSeconds = 120;

        static HTTP()
        {
            if (!string.IsNullOrEmpty(Plugin.Instance.Configuration.FlareSolverrURL))
            {
                CloudflareHandler = new ClearanceHandler(Plugin.Instance.Configuration.FlareSolverrURL)
                {
                    MaxTimeout = (int)TimeSpan.FromSeconds(DefaultTimeoutSeconds).TotalMilliseconds,
                };
            }

            if (Plugin.Instance.Configuration.ProxyEnable && !string.IsNullOrEmpty(Plugin.Instance.Configuration.ProxyHost) && Plugin.Instance.Configuration.ProxyPort > 0)
            {
                Logger.Info("Proxy Enabled");
                var proxy = new List<ProxyInfo>();

                if (string.IsNullOrEmpty(Plugin.Instance.Configuration.ProxyLogin) || string.IsNullOrEmpty(Plugin.Instance.Configuration.ProxyPassword))
                {
                    proxy.Add(new ProxyInfo(Plugin.Instance.Configuration.ProxyHost, Plugin.Instance.Configuration.ProxyPort));
                    CloudflareHandler.ProxyUrl = $"socks5://{Plugin.Instance.Configuration.ProxyHost}:{Plugin.Instance.Configuration.ProxyPort}";
                }
                else
                {
                    proxy.Add(new ProxyInfo(
                        Plugin.Instance.Configuration.ProxyHost,
                        Plugin.Instance.Configuration.ProxyPort,
                        Plugin.Instance.Configuration.ProxyLogin,
                        Plugin.Instance.Configuration.ProxyPassword));
                }

                Proxy = new HttpToSocks5Proxy(proxy.ToArray());
            }

            HttpHandler = new HttpClientHandler()
            {
                CookieContainer = CookieContainer,
                Proxy = Proxy,
            };

            if (Plugin.Instance.Configuration.DisableSSLCheck)
            {
                HttpHandler.ServerCertificateCustomValidationCallback += (sender, certificate, chain, errors) => true;
            }

            if (!Plugin.Instance.Configuration.DisableCaching)
            {
                Logger.Debug("Caching Enabled");
                CacheHandler = new InMemoryCacheHandler(HttpHandler, CacheExpirationProvider.CreateSimple(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5)));
            }
            else
            {
                Logger.Debug("Caching Disabled");
            }

            if (CloudflareHandler != null)
            {
                CloudflareHandler.InnerHandler = CacheHandler != null ? (HttpMessageHandler)CacheHandler : HttpHandler;
                Http = new HttpClient(CloudflareHandler);
            }
            else
            {
                Http = new HttpClient(CacheHandler != null ? (HttpMessageHandler)CacheHandler : HttpHandler);
            }

            Http.Timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds);
        }

        private static ClearanceHandler CloudflareHandler { get; set; }

        private static CookieContainer CookieContainer { get; } = new CookieContainer();

        private static IWebProxy Proxy { get; set; }

        private static HttpClientHandler HttpHandler { get; set; }

        private static InMemoryCacheHandler CacheHandler { get; set; }

        private static HttpClient Http { get; set; }

        public static string GetUserAgent()
            => "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/110.0.0.0 Safari/537.36";

        public static async Task<HTTPResponse> Request(string url, HttpMethod method, HttpContent param, IDictionary<string, string> headers, IDictionary<string, string> cookies, CancellationToken cancellationToken, params HttpStatusCode[] additionalSuccessStatusCodes)
        {
            var result = new HTTPResponse()
            {
                IsOK = false,
            };

            url = Uri.EscapeUriString(Uri.UnescapeDataString(url));

            if (method == null)
            {
                method = HttpMethod.Get;
            }

            var request = new HttpRequestMessage(method, new Uri(url));

            Logger.Debug(string.Format(CultureInfo.InvariantCulture, "Requesting {1} \"{0}\"", request.RequestUri.AbsoluteUri, method.Method));

            request.Headers.TryAddWithoutValidation("User-Agent", GetUserAgent());

            if (param != null)
            {
                request.Content = param;
            }

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            if (cookies != null)
            {
                foreach (var cookie in cookies)
                {
                    CookieContainer.Add(request.RequestUri, new Cookie(cookie.Key, cookie.Value));
                }
            }

            if (CacheHandler != null && request.RequestUri.AbsoluteUri == Consts.DatabaseUpdateURL)
            {
                CacheHandler.InvalidateCache(request.RequestUri);
            }

            HttpResponseMessage response = null;
            try
            {
                response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Logger.Error($"Request error: {e.Message}");

                await Analytics.Send(
                    new AnalyticsExeption
                    {
                        Request = url,
                        Exception = e,
                    }, cancellationToken).ConfigureAwait(false);
            }

            if (response != null)
            {
                result.IsOK = response.IsSuccessStatusCode || additionalSuccessStatusCodes.Contains(response.StatusCode);
                result.StatusCode = response.StatusCode;
#if __EMBY__
                result.Content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                result.ContentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#else
                result.Content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                result.ContentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#endif
                result.Headers = response.Headers;
                result.Cookies = CookieContainer.GetCookies(request.RequestUri).Cast<Cookie>();
            }

            return result;
        }

        public static async Task<HTTPResponse> Request(string url, HttpMethod method, HttpContent param, CancellationToken cancellationToken, IDictionary<string, string> headers = null, IDictionary<string, string> cookies = null, params HttpStatusCode[] additionalSuccessStatusCodes)
            => await Request(url, method, param, headers, cookies, cancellationToken, additionalSuccessStatusCodes).ConfigureAwait(false);

        public static async Task<HTTPResponse> Request(string url, HttpMethod method, CancellationToken cancellationToken, IDictionary<string, string> headers = null, IDictionary<string, string> cookies = null, params HttpStatusCode[] additionalSuccessStatusCodes)
            => await Request(url, method, null, headers, cookies, cancellationToken, additionalSuccessStatusCodes).ConfigureAwait(false);

        public static async Task<HTTPResponse> Request(string url, CancellationToken cancellationToken, IDictionary<string, string> headers = null, IDictionary<string, string> cookies = null, params HttpStatusCode[] additionalSuccessStatusCodes)
        {
            var result = default(HTTPResponse);
            if (string.IsNullOrEmpty(url))
            {
                return result;
            }

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (headers != null)
                    {
                        // Sanitize headers to ensure ASCII characters only
                        foreach (var header in headers)
                        {
                            var sanitizedValue = System.Text.RegularExpressions.Regex.Replace(header.Value, @"[^\u0000-\u007F]", string.Empty);
                            if (!string.IsNullOrEmpty(sanitizedValue))
                            {
                                request.Headers.TryAddWithoutValidation(header.Key, sanitizedValue);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error($"Request error: {e}");
            }

            return result;
        }

        internal struct HTTPResponse
        {
            public string Content { get; set; }

            public Stream ContentStream { get; set; }

            public bool IsOK { get; set; }

            public HttpStatusCode StatusCode { get; set; }

            public IEnumerable<Cookie> Cookies { get; set; }

            public HttpResponseHeaders Headers { get; set; }
        }
    }
}
