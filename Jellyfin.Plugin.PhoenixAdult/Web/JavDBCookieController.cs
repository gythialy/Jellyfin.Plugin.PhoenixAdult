#if !__EMBY__
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Web
{
    /// <summary>
    /// Session self-check behind the settings page button: reports whether a cookie still passes the login wall.
    /// </summary>
    [ApiController]
    [Route("PhoenixAdult")]
    public class JavDBCookieController : ControllerBase
    {
        /// <summary>JS redirect JavDB returns for the profile probe when signed out.</summary>
        private const string LoginRedirectMarker = "/login";

        /// <summary>Cache buster so a cached probe result is not reused.</summary>
        private const string NoCacheHeader = "no-cache";

        /// <summary>Login-gated page used to confirm the session.</summary>
        private const string UncensoredActorProbeUrl = "https://javdb.com/actors/MmE9R";

        /// <summary>Request body: the value from the box, which may not be saved yet.</summary>
        public sealed class CookieTestRequest
        {
            /// <summary>Gets or sets the cookie to test.</summary>
            public string Cookie { get; set; }
        }

        /// <summary>
        /// Probe two login-protected endpoints and report valid, expired, missing, invalid, unknown or error.
        /// </summary>
        [HttpPost("javdb/cookie-test")]
        [Authorize]
        public async Task<ActionResult> TestCookie([FromBody] CookieTestRequest request, CancellationToken cancellationToken)
        {
            if (User?.Identity?.IsAuthenticated != true || User.IsInRole("Administrator") == false)
            {
                return this.Forbid();
            }

            var raw = string.IsNullOrWhiteSpace(request?.Cookie)
                ? Plugin.Instance?.Configuration?.JavDBCookie
                : request.Cookie;

            if (string.IsNullOrWhiteSpace(raw))
            {
                return this.Ok(Result("missing", "No cookie configured."));
            }

            var cookies = ParseCookies(raw);
            if (cookies == null)
            {
                return this.Ok(Result("invalid", "Cookie could not be parsed. Expected something like \"name=value; name2=value2\"."));
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Referer", "https://javdb.com/" },
                { "Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8" },
                { "Cache-Control", NoCacheHeader },
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            try
            {
                // Signed out, the profile probe answers a redirect to /login.
                var profile = await HTTP.Request(
                    "https://javdb.com/users/profile?t=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    cts.Token,
                    headers,
                    cookies).ConfigureAwait(false);

                if (!profile.IsOK)
                {
                    return this.Ok(Result("error", $"JavDB returned HTTP {(int)profile.StatusCode} for the profile probe."));
                }

                if (!string.IsNullOrEmpty(profile.Content)
                    && profile.Content.IndexOf(LoginRedirectMarker, StringComparison.OrdinalIgnoreCase) >= 0
                    && profile.Content.Length < 512)
                {
                    return this.Ok(Result("expired", "JavDB redirected the profile probe to /login."));
                }

                // Confirm with a page that always requires a session.
                var actorPage = await HTML.ElementFromURL(UncensoredActorProbeUrl, cts.Token, headers, cookies).ConfigureAwait(false);
                if (actorPage == null)
                {
                    return this.Ok(Result("unknown", "The profile probe passed but the login-gated actor page could not be read."));
                }

                var hasLoginForm = actorPage.SelectSingleNode("//input[@name='password']") != null
                    || actorPage.OuterHtml.IndexOf("rucaptcha", StringComparison.OrdinalIgnoreCase) >= 0;
                if (hasLoginForm)
                {
                    return this.Ok(Result("expired", "JavDB served the login form for the login-gated actor page."));
                }

                if (actorPage.SelectSingleNode("//span[contains(@class, 'actor-section-name')]") != null
                    || actorPage.SelectSingleNode("//div[contains(@class, 'actor-box')]") != null)
                {
                    return this.Ok(Result("valid", "The cookie passes JavDB's login wall."));
                }

                return this.Ok(Result("unknown", "The actor page was readable but did not look like an actor page; JavDB may have changed its layout."));
            }
            catch (OperationCanceledException)
            {
                return this.Ok(Result("error", "The check timed out before JavDB answered."));
            }
            catch (Exception e)
            {
                Logger.Error($"[JavDB] Cookie test failed: {e.Message}");
                return this.Ok(Result("error", e.Message));
            }
        }

        private static object Result(string status, string message)
            => new { status, message, checkedAtUtc = DateTime.UtcNow.ToString("O") };

        /// <summary>Parse "a=b; c=d"; null when malformed.</summary>
        private static IDictionary<string, string> ParseCookies(string raw)
        {
            var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in raw.Split(';'))
            {
                var pair = part.Split(new[] { '=' }, 2);
                if (pair.Length != 2 || string.IsNullOrWhiteSpace(pair[0]))
                {
                    continue;
                }

                cookies[pair[0].Trim()] = pair[1].Trim();
            }

            return cookies.Count > 0 ? cookies : null;
        }
    }
}
#endif
