using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
                CacheHandler = new InMemoryCacheHandler(HttpHandler, CacheExpirationProvider.CreateSimple(TimeSpan.FromHours(12), TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(10)));
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
            => "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36";

        public static async Task<HTTPResponse> Request(string url, HttpMethod method, HttpContent param, IDictionary<string, string> headers, IDictionary<string, string> cookies, CancellationToken cancellationToken, bool freshSession = false, params HttpStatusCode[] additionalSuccessStatusCodes)
        {
            var result = new HTTPResponse()
            {
                IsOK = false,
            };

            if (method == null)
            {
                method = HttpMethod.Get;
            }

            var cookieContainer = freshSession ? new CookieContainer() : CookieContainer;

            var request = new HttpRequestMessage(method, new Uri(url));

            request.Headers.TryAddWithoutValidation("User-Agent", GetUserAgent());

            if (param != null)
            {
                request.Content = param;
                string contentString = await param.ReadAsStringAsync();
                Logger.Info($"[HTTP Request] params: {contentString}");
            }

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                string jsonString = JsonSerializer.Serialize(request.Headers.ToDictionary(), new JsonSerializerOptions { WriteIndented = true });
                Logger.Info($"[HTTP Request] headers: {jsonString}");
            }

            if (cookies != null)
            {
                Logger.Info($"[HTTP Request] Input cookies: {JsonSerializer.Serialize(cookies)}");

                foreach (var cookie in cookies)
                {
                    cookieContainer.Add(request.RequestUri, new Cookie(cookie.Key, cookie.Value));
                }

                var cookieCollection = cookieContainer.GetCookies(request.RequestUri);
                if (cookieCollection.Count > 0)
                {
                    Logger.Info($"[HTTP Request] Cookies being sent for {request.RequestUri}:");
                    foreach (Cookie cookie in cookieCollection)
                    {
                        Logger.Info($"[HTTP Request]  - Name: {cookie.Name}, Value: {cookie.Value}, Domain: {cookie.Domain}");
                    }
                }
                else
                {
                    Logger.Info($"[HTTP Request] No cookies found for {request.RequestUri}.");
                }
            }

            if (CacheHandler != null && request.RequestUri.AbsoluteUri == Consts.DatabaseUpdateURL)
            {
                CacheHandler.InvalidateCache(request.RequestUri);
            }

            Logger.Info(string.Format(CultureInfo.InvariantCulture, "[HTTP Request] Requesting {1} \"{0}\"", request.RequestUri.AbsoluteUri, method.Method));

            HttpResponseMessage response = null;
            try
            {
                if (freshSession)
                {
                    using (var freshHandler = new HttpClientHandler { CookieContainer = cookieContainer, Proxy = Proxy })
                    using (var freshClient = new HttpClient(freshHandler) { Timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds) })
                    {
                        response = await freshClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                }
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
                result.ResponseUrl = response.RequestMessage.RequestUri;
                result.IsOK = response.IsSuccessStatusCode || additionalSuccessStatusCodes.Contains(response.StatusCode);
                result.StatusCode = response.StatusCode;
                result.Content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                result.ContentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                result.Headers = response.Headers;
                result.Cookies = cookieContainer.GetCookies(request.RequestUri).Cast<Cookie>();
            }

            if (result.StatusCode == HttpStatusCode.TooManyRequests && !string.IsNullOrEmpty(Plugin.Instance.Configuration.FlareSolverrURL))
            {
                Logger.Info($"[HTTP Request] Encountered TooManyRequests (429). Falling back to direct FlareSolverr request for {url}");
                try
                {
                    var fsResponse = await RequestDirectViaFlareSolverr(url, method, param, headers, cookies, cancellationToken).ConfigureAwait(false);
                    if (fsResponse.IsOK)
                    {
                        return fsResponse;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"[HTTP Request] FlareSolverr direct fallback failed: {ex.Message}");
                }
            }

            return result;
        }

        private static async Task<HTTPResponse> RequestDirectViaFlareSolverr(string url, HttpMethod method, HttpContent param, IDictionary<string, string> headers, IDictionary<string, string> cookies, CancellationToken cancellationToken)
        {
            var result = new HTTPResponse { IsOK = false };

            using (var cleanHandler = new HttpClientHandler { Proxy = Proxy })
            {
                if (Plugin.Instance.Configuration.DisableSSLCheck)
                {
                    cleanHandler.ServerCertificateCustomValidationCallback += (sender, certificate, chain, errors) => true;
                }

                using (var client = new HttpClient(cleanHandler) { Timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds) })
                {
                    string cmd = method == HttpMethod.Post ? "request.post" : "request.get";

                    var fsCookies = new List<object>();
                    if (cookies != null)
                    {
                        foreach (var cookie in cookies)
                        {
                            fsCookies.Add(new { name = cookie.Key, value = cookie.Value });
                        }
                    }

                    var fsHeaders = new Dictionary<string, string>();
                    if (headers != null)
                    {
                        foreach (var h in headers)
                        {
                            fsHeaders[h.Key] = h.Value;
                        }
                    }

                    string postData = null;
                    if (method == HttpMethod.Post && param != null)
                    {
                        postData = await param.ReadAsStringAsync().ConfigureAwait(false);
                    }

                    var payload = new
                    {
                        cmd = cmd,
                        url = url,
                        maxTimeout = (int)TimeSpan.FromSeconds(DefaultTimeoutSeconds).TotalMilliseconds,
                        cookies = fsCookies.Count > 0 ? fsCookies : null,
                        headers = fsHeaders.Count > 0 ? fsHeaders : null,
                        postData = postData
                    };

                    string fsUrl = Plugin.Instance.Configuration.FlareSolverrURL;
                    if (!fsUrl.EndsWith("/"))
                    {
                        fsUrl += "/";
                    }
                    fsUrl += "v1";

                    var jsonPayload = JsonSerializer.Serialize(payload);
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    Logger.Info($"[HTTP Request] Sending direct request to FlareSolverr at {fsUrl} for {url}");
                    var response = await client.PostAsync(fsUrl, content, cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        var responseStr = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        using (var doc = JsonDocument.Parse(responseStr))
                        {
                            var root = doc.RootElement;
                            string status = root.GetProperty("status").GetString();
                            if (status == "ok")
                            {
                                var solution = root.GetProperty("solution");
                                string pageContent = solution.GetProperty("response").GetString();
                                int statusCode = solution.GetProperty("status").GetInt32();

                                result.IsOK = statusCode >= 200 && statusCode < 300;
                                result.StatusCode = (HttpStatusCode)statusCode;
                                result.Content = pageContent;
                                result.ContentStream = new MemoryStream(Encoding.UTF8.GetBytes(pageContent));

                                if (solution.TryGetProperty("url", out var finalUrlProp))
                                {
                                    result.ResponseUrl = new Uri(finalUrlProp.GetString());
                                }
                                else
                                {
                                    result.ResponseUrl = new Uri(url);
                                }

                                if (solution.TryGetProperty("cookies", out var cookiesProp) && cookiesProp.ValueKind == JsonValueKind.Array)
                                {
                                    var parsedCookies = new List<Cookie>();
                                    foreach (var c in cookiesProp.EnumerateArray())
                                    {
                                        try
                                        {
                                            string cName = c.GetProperty("name").GetString();
                                            string cValue = c.GetProperty("value").GetString();
                                            string cDomain = c.TryGetProperty("domain", out var dProp) ? dProp.GetString() : new Uri(url).Host;
                                            string cPath = c.TryGetProperty("path", out var pProp) ? pProp.GetString() : "/";

                                            var cookieObj = new Cookie(cName, cValue, cPath, cDomain);
                                            parsedCookies.Add(cookieObj);

                                            CookieContainer.Add(cookieObj);
                                        }
                                        catch (Exception cookieEx)
                                        {
                                            Logger.Warning($"[HTTP Request] Failed to add cookie from FlareSolverr: {cookieEx.Message}");
                                        }
                                    }
                                    result.Cookies = parsedCookies;
                                }

                                Logger.Info($"[HTTP Request] FlareSolverr direct request succeeded. Status code: {statusCode}");
                                return result;
                            }
                            else
                            {
                                string message = root.TryGetProperty("message", out var msgProp) ? msgProp.GetString() : "Unknown error";
                                Logger.Error($"[HTTP Request] FlareSolverr returned error: {message}");
                            }
                        }
                    }
                    else
                    {
                        Logger.Error($"[HTTP Request] FlareSolverr API request failed with status: {response.StatusCode}");
                    }
                }
            }

            return result;
        }

        public static async Task<HTTPResponse> Request(string url, HttpMethod method, HttpContent param, CancellationToken cancellationToken, IDictionary<string, string> headers = null, IDictionary<string, string> cookies = null, bool freshSession = false, params HttpStatusCode[] additionalSuccessStatusCodes)
            => await Request(url, method, param, headers, cookies, cancellationToken, freshSession, additionalSuccessStatusCodes).ConfigureAwait(false);

        public static async Task<HTTPResponse> Request(string url, HttpMethod method, CancellationToken cancellationToken, IDictionary<string, string> headers = null, IDictionary<string, string> cookies = null, bool freshSession = false, params HttpStatusCode[] additionalSuccessStatusCodes)
            => await Request(url, method, null, headers, cookies, cancellationToken, freshSession, additionalSuccessStatusCodes).ConfigureAwait(false);

        public static async Task<HTTPResponse> Request(string url, CancellationToken cancellationToken, IDictionary<string, string> headers = null, IDictionary<string, string> cookies = null, bool freshSession = false, params HttpStatusCode[] additionalSuccessStatusCodes)
            => await Request(url, null, null, headers, cookies, cancellationToken, freshSession, additionalSuccessStatusCodes).ConfigureAwait(false);

        internal struct HTTPResponse
        {
            public Uri ResponseUrl { get; set; }

            public string Content { get; set; }

            public Stream ContentStream { get; set; }

            public bool IsOK { get; set; }

            public HttpStatusCode StatusCode { get; set; }

            public IEnumerable<Cookie> Cookies { get; set; }

            public HttpResponseHeaders Headers { get; set; }
        }
    }
}
