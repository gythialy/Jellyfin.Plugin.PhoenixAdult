using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PhoenixAdult.Helpers.Utils
{
    /// <summary>
    /// Generic client for FlareSolverr (https://github.com/FlareSolverr/FlareSolverr).
    /// </summary>
    /// <remarks>
    /// Some sites (e.g. the Vixen Media Group / NetworkStrike3 network) protect their APIs with
    /// Cloudflare that blocks plain HTTP clients based on the TLS fingerprint -- cookies alone are
    /// not enough, so FlareSolverrSharp's ClearanceHandler cannot pass them. The only way is to send
    /// the request from a real browser context. This helper does that using the patched FlareSolverr
    /// "contentType: json" mode (see the patches/ folder) which issues a same-origin fetch() from the
    /// browser, carrying a real browser fingerprint AND custom headers (e.g. Apollo CSRF preflight).
    /// </remarks>
    internal static class FlareSolverr
    {
        private const string SessionId = "phoenix-adult";
        private const int MaxTimeoutSeconds = 120;
        private const int WarmupTimeoutSeconds = 60;

        private static readonly HttpClient Http = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(MaxTimeoutSeconds + 30),
        };

        // FlareSolverr sessions are browser instances: concurrent requests on the same session are not
        // safe, so all calls are serialized.
        private static readonly SemaphoreSlim Lock = new SemaphoreSlim(1, 1);

        // Hosts whose session has already been warmed up (a plain GET so the browser context is
        // established on the target origin and Cloudflare is passed).
        private static readonly HashSet<string> WarmedHosts = new HashSet<string>();

        public static bool IsConfigured
            => !string.IsNullOrWhiteSpace(Plugin.Instance.Configuration.FlareSolverrURL);

        /// <summary>
        /// POST a JSON body from FlareSolverr's browser context.
        /// </summary>
        /// <param name="url">Target URL.</param>
        /// <param name="jsonBody">Raw JSON request body.</param>
        /// <param name="headers">Optional extra headers (e.g. CSRF preflight headers).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The parsed JSON response body, or <c>null</c> when FlareSolverr is not configured
        /// or the response was not successful JSON.</returns>
        /// <exception cref="Exception">Thrown when FlareSolverr itself fails (after one retry).</exception>
        public static async Task<JObject> PostJson(string url, string jsonBody, IDictionary<string, string> headers = null, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
            {
                return null;
            }

            var flareSolverrUrl = Plugin.Instance.Configuration.FlareSolverrURL.TrimEnd('/');
            var host = new Uri(url).Host;

            await Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Try up to twice: once directly, and once more after re-warming the session
                // (a FlareSolverr restart drops sessions, so the first call may fail).
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        await EnsureSession(flareSolverrUrl, cancellationToken).ConfigureAwait(false);
                        await EnsureWarmed(flareSolverrUrl, host, cancellationToken).ConfigureAwait(false);

                        var request = new JObject
                        {
                            ["cmd"] = "request.post",
                            ["url"] = url,
                            ["postData"] = jsonBody,
                            ["contentType"] = "json",
                            ["headers"] = HeadersToJObject(headers),
                            ["session"] = SessionId,
                            ["maxTimeout"] = MaxTimeoutSeconds * 1000,
                        };

                        var result = await SendCommand(flareSolverrUrl, request, cancellationToken).ConfigureAwait(false);
                        return ParseResponse(result);
                    }
                    catch (Exception e)
                    {
                        Logger.Error($"FlareSolverr attempt {attempt + 1} failed for {url}: {e.Message}");
                        WarmedHosts.Remove(host);
                        if (attempt == 1)
                        {
                            throw;
                        }
                    }
                }
            }
            finally
            {
                Lock.Release();
            }

            return null;
        }

        private static async Task EnsureSession(string flareSolverrUrl, CancellationToken cancellationToken)
        {
            var create = new JObject
            {
                ["cmd"] = "sessions.create",
                ["session"] = SessionId,
            };

            await SendCommand(flareSolverrUrl, create, cancellationToken).ConfigureAwait(false);
        }

        private static async Task EnsureWarmed(string flareSolverrUrl, string host, CancellationToken cancellationToken)
        {
            if (WarmedHosts.Contains(host))
            {
                return;
            }

            var warm = new JObject
            {
                ["cmd"] = "request.get",
                ["url"] = $"https://{host}/",
                ["session"] = SessionId,
                ["maxTimeout"] = WarmupTimeoutSeconds * 1000,
            };

            var result = await SendCommand(flareSolverrUrl, warm, cancellationToken).ConfigureAwait(false);
            if (result?["solution"]?["status"]?.Value<int>() == 200)
            {
                WarmedHosts.Add(host);
            }
        }

        private static async Task<JObject> SendCommand(string flareSolverrUrl, JObject body, CancellationToken cancellationToken)
        {
            var content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, flareSolverrUrl + "/v1")
            {
                Content = content,
            };
            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var json = JObject.Parse(text);
            if ((string)json["status"] != "ok")
            {
                throw new Exception($"FlareSolverr error: {text}");
            }

            return json;
        }

        private static JObject ParseResponse(JObject result)
        {
            var solution = (JObject)result?["solution"];
            var httpStatus = solution?["status"]?.Value<int>() ?? 0;
            var response = (string)solution?["response"];
            if (httpStatus != 200 || string.IsNullOrEmpty(response))
            {
                Logger.Error($"FlareSolverr response status {httpStatus}, body: {Truncate(response, 500)}");
                return null;
            }

            // Some error responses come HTML-wrapped in <pre> tags, extract the JSON inside
            var jsonText = response;
            if (response.TrimStart().StartsWith("<", StringComparison.Ordinal))
            {
                var start = response.IndexOf("<pre>", StringComparison.OrdinalIgnoreCase);
                var end = response.IndexOf("</pre>", StringComparison.OrdinalIgnoreCase);
                if (start >= 0 && end > start)
                {
                    jsonText = response.Substring(start + 5, end - start - 5);
                }
            }

            if (string.IsNullOrWhiteSpace(jsonText))
            {
                return null;
            }

            return JObject.Parse(jsonText);
        }

        private static JObject HeadersToJObject(IDictionary<string, string> headers)
        {
            var result = new JObject();
            if (headers == null)
            {
                return result;
            }

            foreach (var header in headers)
            {
                result[header.Key] = header.Value;
            }

            return result;
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            {
                return text;
            }

            return text.Substring(0, maxLength) + "...";
        }
    }
}
