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
    /// JavDB 登录态自检：配置页的「检测 Cookie」按钮调这里。
    /// JavDB 的登录需要图形验证码（rucaptcha）与 CSRF token，插件不可能自动登录/续期，
    /// 所以这里只做「这个 Cookie 现在还能不能过登录墙」的判定，把结果直接显示在设置页上。
    /// </summary>
    [ApiController]
    [Route("PhoenixAdult")]
    public class JavDBCookieController : ControllerBase
    {
        /// <summary>未登录访问 /users/profile 时 JavDB 返回的 JS 跳转片段（实测 content-type: text/javascript）。</summary>
        private const string LoginRedirectMarker = "/login";

        /// <summary>加在探测请求上，避免插件自带的响应缓存把上一次的结果复用给下一次检测。</summary>
        private const string NoCacheHeader = "no-cache";

        /// <summary>无码演员页：匿名访问一定落到登录页，用来二次确认登录态。</summary>
        private const string UncensoredActorProbeUrl = "https://javdb.com/actors/MmE9R";

        /// <summary>请求体：可以是页面上还没保存的 Cookie 值。</summary>
        public sealed class CookieTestRequest
        {
            /// <summary>Gets or sets 待检测的 Cookie（形如 over18=1; _jdb_session=…）。</summary>
            public string Cookie { get; set; }
        }

        /// <summary>
        /// 用给定 Cookie（缺省时用已保存的配置）访问 JavDB 的两个受登录保护的入口，
        /// 返回 valid / expired / missing / invalid / error。
        /// </summary>
        /// <param name="request">请求体。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>检测结果。</returns>
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
                // 1) /users/profile：未登录时服务端只回 window.location.href='/login';
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

                // 2) 无码演员页二次确认：匿名一定拿到登录表单（含 password 输入框）。
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

        /// <summary>解析 "a=b; c=d"；格式不对返回 null。</summary>
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
