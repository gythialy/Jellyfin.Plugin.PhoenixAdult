using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
#if !__EMBY__
using Jellyfin.Data.Enums;
#endif
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    /// <summary>
    /// JavDB (javdb.com); a scene id is the absolute /v/<id> URL.
    /// Search and censored detail pages need no account; actor pages and uncensored titles need a session.
    /// One search sends a single request, the site meters quota per IP.
    /// </summary>
    public class SiteJavDB : IProviderBase
    {
        /// <summary>JavDB image CDN (public).</summary>
        private const string CdnBaseUrl = "https://c0.jdbstatic.com";

        private const string DefaultBaseUrl = "https://javdb.com";

        private const string DefaultSearchPath = "/search?q=";

        private const string LocaleQuery = "locale=zh";

        /// <summary>Max images per GetImages call.</summary>
        private const int MaxImages = 30;

        /// <summary>Runtime from the runtime row, e.g. "60 min" or "41:50".</summary>
        private static readonly Regex RuntimeRegex = new Regex(
            @"(?<min>[0-9]+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>Release date as "2022-04-19".</summary>
        private static readonly Regex DateRegex = new Regex(
            @"(?<year>[0-9]{4})[-/.](?<mon>[0-9]{1,2})[-/.](?<day>[0-9]{1,2})",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>Code comparison key: ignore spaces, hyphens and underscores.</summary>
        private static readonly Regex CodeSeparatorRegex = new Regex(
            @"[\s\-_]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>Studio row may list two spellings; keep the second.</summary>
        private static readonly Regex JapaneseRegex = new Regex(
            @"[\u3040-\u30FF\u3400-\u4DBF\u4E00-\u9FFF]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrWhiteSpace(searchTitle))
            {
                return result;
            }

            var code = NormalizeCode(searchTitle);
            if (string.IsNullOrEmpty(code))
            {
                return result;
            }

            try
            {
                var baseUrl = GetBaseUrl(siteNum);
                var url = GetSearchUrl(siteNum) + Uri.EscapeDataString(code) + "&f=all";
                var searchData = await FetchPage(url, baseUrl, cancellationToken).ConfigureAwait(false);
                if (searchData == null)
                {
                    return result;
                }

                // Card: a.box under /v/ with cover, title and meta date; actor cards (box actor-box) are filtered by the href test.
                foreach (var card in searchData.SelectNodesSafe("//a[starts-with(@href, '/v/') and contains(@class, 'box')]"))
                {
                    var href = card.GetAttributeValue("href", string.Empty);
                    if (string.IsNullOrEmpty(href))
                    {
                        continue;
                    }

                    var shownNode = card.SelectSingleNode(".//strong");
                    var shown = shownNode == null ? string.Empty : shownNode.InnerText.Trim();
                    if (!IsSameCode(shown, code))
                    {
                        continue;
                    }

                    var titleAttr = card.GetAttributeValue("title", string.Empty).Trim();
                    var name = string.IsNullOrEmpty(titleAttr) ? shown : $"{shown} {titleAttr}";

                    var res = new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, Helper.Encode(baseUrl + href) } },
                        Name = name.Trim(),
                        ImageUrl = NormalizeUrl(FirstImageUrl(card), baseUrl),
                    };

                    // Meta date maps to PremiereDate so the dialog shows it and results can be sorted.
                    var metaDate = card.SelectSingleNode(".//div[contains(@class, 'meta')]")?.InnerText.Trim();
                    if (!string.IsNullOrEmpty(metaDate)
                        && DateTime.TryParseExact(metaDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var metaDateValue))
                    {
                        res.PremiereDate = metaDateValue;
                        res.ProductionYear = metaDateValue.Year;
                    }

                    // A card occasionally has no <img> (lazy loading, uncensored rows); derive the CDN cover path from /v/<id>,
                    // which matches the site's own covers/<first two id chars, lower case>/<id>.jpg rule.
                    if (string.IsNullOrEmpty(res.ImageUrl))
                    {
                        var movieId = href.Trim('/').Split('/').LastOrDefault();
                        if (!string.IsNullOrEmpty(movieId) && movieId.Length > 2)
                        {
                            res.ImageUrl = $"{CdnBaseUrl}/covers/{movieId.Substring(0, 2).ToLowerInvariant()}/{movieId}.jpg";
                        }
                    }

                    result.Add(res);

                    // Keep only the first row for a code: duplicates exist on the site and none is more trustworthy than the first.
                    break;
                }
            }
            catch (Exception e)
            {
                Logger.Error($"[JavDB] Search failed for '{searchTitle}': {e.Message}");
            }

            return result;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem>()
            {
                Item = new Movie(),
                People = new List<PersonInfo>(),
            };

            if (siteNum == null || sceneID == null || sceneID.Length == 0)
            {
                return result;
            }

            var sceneURL = ResolveSceneUrl(siteNum, sceneID[0]);
            if (string.IsNullOrEmpty(sceneURL))
            {
                return result;
            }

            try
            {
                var baseUrl = GetBaseUrl(siteNum);
                var localeUrl = WithLocale(sceneURL);
                var sceneData = await FetchPage(localeUrl, baseUrl, cancellationToken).ConfigureAwait(false);
                if (sceneData == null)
                {
                    return result;
                }

                if (IsLoginWall(sceneData))
                {
                    // Actor pages and uncensored titles require a session: log it and return, do not throw, and never parse the login page as a title page.
                    Logger.Info($"[JavDB] Login wall hit for '{sceneURL}'.");
                    LogLoginWallHint();
                    return result;
                }

                // Panel rows: <strong>label:</strong><span class="value">; actresses carry actor-female, male actors do not.
                string shownCode = null;
                string director = null;
                var genres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var actors = new List<string>();
                var studios = new List<string>();
                var series = string.Empty;

                foreach (var panel in sceneData.SelectNodesSafe("//div[contains(@class, 'panel-block')]"))
                {
                    var labelNode = panel.SelectSingleNode("./strong");
                    var valueNode = panel.SelectSingleNode(".//span[contains(@class, 'value')]");
                    if (labelNode == null || valueNode == null)
                    {
                        continue;
                    }

                    var label = NormalizeLabel(labelNode.InnerText);
                    if (string.IsNullOrEmpty(label))
                    {
                        continue;
                    }

                    var value = valueNode.InnerText.Trim();

                    switch (label)
                    {
                        case "番號":
                            shownCode = value;
                            break;

                        case "日期":
                            var date = ParseDate(value);
                            if (date.HasValue)
                            {
                                result.Item.PremiereDate = date;
                                result.Item.ProductionYear = date.Value.Year;
                            }

                            break;

                        case "時長":
                            var runtime = RuntimeRegex.Match(value ?? string.Empty);
                            if (runtime.Success
                                && int.TryParse(runtime.Groups["min"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
                                && minutes > 0)
                            {
                                result.Item.RunTimeTicks = TimeSpan.FromMinutes(minutes).Ticks;
                            }

                            break;

                        case "導演":
                            director = value;
                            break;

                        case "片商":
                            var maker = PickMaker(value);
                            if (!string.IsNullOrEmpty(maker))
                            {
                                studios.Add(maker);
                            }

                            break;

                        case "發行":
                            if (!string.IsNullOrEmpty(value))
                            {
                                studios.Add(value);
                            }

                            break;

                        case "系列":
                            if (!string.IsNullOrEmpty(value))
                            {
                                series = value;
                            }

                            break;

                        case "類別":
                            foreach (var genreNode in valueNode.SelectNodesSafe(".//a"))
                            {
                                var genreName = genreNode.InnerText.Trim();
                                if (!string.IsNullOrEmpty(genreName))
                                {
                                    genres.Add(genreName);
                                }
                            }

                            break;

                        case "演員":
                            foreach (var actorNode in valueNode.SelectNodesSafe(".//a[contains(@class, 'actor-female')]"))
                            {
                                var actorName = actorNode.InnerText.Trim();
                                if (!string.IsNullOrEmpty(actorName) && !actors.Contains(actorName))
                                {
                                    actors.Add(actorName);
                                }
                            }

                            break;
                    }
                }

                var title = PickTitle(sceneData);
                if (string.IsNullOrEmpty(title))
                {
                    title = shownCode ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(title))
                {
                    // Neither the panel nor the title parsed: treat it as missing instead of writing an empty item.
                    Logger.Debug($"[JavDB] No usable metadata for '{sceneURL}'");
                    return result;
                }

                result.Item.Name = title;
                result.Item.ExternalId = StripLocale(sceneURL);

                var normalizedCode = NormalizeCode(shownCode);
                if (!string.IsNullOrEmpty(normalizedCode))
                {
                    result.Item.OriginalTitle = normalizedCode;
                }

                // Studio is primary; the publisher label and series are recorded into Studio as other JAV handlers in this repo do.
                foreach (var studio in studios.Where(o => !string.IsNullOrEmpty(o)))
                {
                    result.Item.AddStudio(studio);
                }

                if (!string.IsNullOrEmpty(series))
                {
                    result.Item.AddStudio(series);
                }

                foreach (var genre in genres)
                {
                    result.Item.AddGenre(genre);
                }

                if (!string.IsNullOrEmpty(director))
                {
                    result.AddPerson(new PersonInfo { Name = director, Type = PersonKind.Director });
                }

                foreach (var actor in actors)
                {
                    result.AddPerson(new PersonInfo { Name = actor, Type = PersonKind.Actor });
                }

                var cover = PickCover(sceneData);
                if (!string.IsNullOrEmpty(cover))
                {
                    result.Item.SetImage(new ItemImageInfo { Path = cover, Type = ImageType.Primary }, 0);
                }
            }
            catch (Exception e)
            {
                Logger.Error($"[JavDB] Update failed for '{sceneURL}': {e.Message}");
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            if (siteNum == null || sceneID == null || sceneID.Length == 0)
            {
                return result;
            }

            var sceneURL = ResolveSceneUrl(siteNum, sceneID[0]);
            if (string.IsNullOrEmpty(sceneURL))
            {
                return result;
            }

            try
            {
                var baseUrl = GetBaseUrl(siteNum);
                var sceneData = await FetchPage(WithLocale(sceneURL), baseUrl, cancellationToken).ConfigureAwait(false);
                if (sceneData == null)
                {
                    return result;
                }

                if (IsLoginWall(sceneData))
                {
                    Logger.Info($"[JavDB] Login wall hit for images of '{sceneURL}'.");
                    LogLoginWallHint();
                    return result;
                }

                var cover = PickCover(sceneData);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(cover) && seen.Add(cover))
                {
                    result.Add(new RemoteImageInfo { Type = ImageType.Primary, Url = cover });
                }

                // Samples: the tile-item href is the full-size image on the public CDN.
                foreach (var tile in sceneData.SelectNodesSafe("//a[contains(@class, 'tile-item')]"))
                {
                    if (result.Count >= MaxImages)
                    {
                        break;
                    }

                    var href = NormalizeUrl(tile.GetAttributeValue("href", string.Empty), baseUrl);
                    if (string.IsNullOrEmpty(href) || !href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!seen.Add(href))
                    {
                        continue;
                    }

                    result.Add(new RemoteImageInfo { Type = ImageType.Backdrop, Url = href });
                }
            }
            catch (Exception e)
            {
                Logger.Error($"[JavDB] GetImages failed for '{sceneURL}': {e.Message}");
            }

            return result;
        }

        /// <summary>Site base URL, falling back to the built-in one.</summary>
        private static string GetBaseUrl(int[] siteNum)
        {
            var baseUrl = string.Empty;
            try
            {
                baseUrl = Helper.GetSearchBaseURL(siteNum);
            }
            catch (Exception)
            {
                // Group 255 may be unregistered; use the built-in URL.
            }

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = DefaultBaseUrl;
            }

            return baseUrl.TrimEnd('/');
        }

        /// <summary>Absolute search URL built from the relative site path.</summary>
        private static string GetSearchUrl(int[] siteNum)
        {
            var searchUrl = string.Empty;
            try
            {
                searchUrl = Helper.GetSearchSearchURL(siteNum);
            }
            catch (Exception)
            {
                // Same as above: use the built-in path when unavailable.
            }

            if (string.IsNullOrWhiteSpace(searchUrl))
            {
                searchUrl = DefaultSearchPath;
            }

            if (!searchUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                searchUrl = GetBaseUrl(siteNum) + (searchUrl.StartsWith("/", StringComparison.Ordinal) ? searchUrl : "/" + searchUrl);
            }

            return searchUrl;
        }

        private static IDictionary<string, string> GetHeaders(string baseUrl)
        {
            return new Dictionary<string, string>
            {
                { "Referer", baseUrl + "/" },
                { "Accept-Language", "ja,en;q=0.9" },
            };
        }

        /// <summary>
        /// Split the pasted cookie header into a dictionary; null when empty.
        /// The age gate and the session both live in that header.
        /// </summary>
        private static IDictionary<string, string> GetCookies()
        {
            var cookies = new Dictionary<string, string>();
            string raw;
            try
            {
                raw = Plugin.Instance?.Configuration?.JavDBCookie;
            }
            catch (Exception)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            foreach (var part in raw.Split(';'))
            {
                var pair = part.Trim();
                if (pair.Length == 0)
                {
                    continue;
                }

                var index = pair.IndexOf('=');
                if (index <= 0)
                {
                    continue;
                }

                var name = pair.Substring(0, index).Trim();
                var value = pair.Substring(index + 1).Trim();
                if (name.Length > 0)
                {
                    cookies[name] = value;
                }
            }

            return cookies.Count > 0 ? cookies : null;
        }

        /// <summary>
        /// Direct request first; FlareSolverr only on a real challenge.
        /// The title decides: the challenge-platform script also appears on normal pages.
        /// </summary>
        private static async Task<HtmlNode> FetchPage(string url, string baseUrl, CancellationToken cancellationToken)
        {
            var headers = GetHeaders(baseUrl);
            var cookies = GetCookies();

            try
            {
                var html = await HTML.ElementFromURL(url, cancellationToken, headers, cookies).ConfigureAwait(false);
                if (IsUsable(html) && !LooksLikeChallenge(html))
                {
                    return html;
                }
            }
            catch (Exception e)
            {
                Logger.Debug($"[JavDB] Direct request failed for '{url}': {e.Message}");
            }

            try
            {
                var html = await Fetcher.Html(url, cancellationToken, headers, cookies).ConfigureAwait(false);
                return IsUsable(html) ? html : null;
            }
            catch (Exception e)
            {
                Logger.Error($"[JavDB] Fallback request failed for '{url}': {e.Message}");
                return null;
            }
        }

        /// <summary>OuterHtml throws on the detached node returned for non-200 responses.</summary>
        private static bool IsUsable(HtmlNode node)
        {
            if (node == null)
            {
                return false;
            }

            try
            {
                return !string.IsNullOrWhiteSpace(node.OuterHtml);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Judge the challenge by title only: the body always carries the challenge script.</summary>
        private static bool LooksLikeChallenge(HtmlNode node)
        {
            var titleNode = node.SelectSingleNode("//title");
            if (titleNode == null)
            {
                return false;
            }

            var title = titleNode.InnerText.Trim();
            return title.StartsWith("Just a moment", StringComparison.OrdinalIgnoreCase)
                || title.StartsWith("请稍候", StringComparison.Ordinal)
                || title.StartsWith("Attention Required", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Absolute detail URL from a scene id, or a raw identify-dialog URL.</summary>
        private static string ResolveSceneUrl(int[] siteNum, string sceneID)
        {
            if (string.IsNullOrWhiteSpace(sceneID))
            {
                return string.Empty;
            }

            var sceneURL = DecodeSafe(sceneID).Trim();
            if (sceneURL.Length == 0)
            {
                return string.Empty;
            }

            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var baseUrl = GetBaseUrl(siteNum);
                sceneURL = sceneURL.StartsWith("/", StringComparison.Ordinal)
                    ? baseUrl + sceneURL
                    : $"{baseUrl}/{sceneURL}";
            }

            return sceneURL;
        }

        /// <summary>Decode the scene id; raw values pass through.</summary>
        private static string DecodeSafe(string value)
        {
            try
            {
                var decoded = Helper.Decode(value);
                if (!string.IsNullOrWhiteSpace(decoded) && decoded.IndexOf('\uFFFD') < 0 && !decoded.Any(char.IsControl))
                {
                    return decoded;
                }
            }
            catch (Exception)
            {
                // Non-Base58 input throws FormatException; treat it as a raw value.
            }

            return value;
        }

        /// <summary>Canonical code form via JavId, else trimmed and upper-cased.</summary>
        private static string NormalizeCode(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var value = input.Trim();
            if (JavId.TryParse(value, out var javId) && !string.IsNullOrEmpty(javId))
            {
                return javId;
            }

            // With a typed title, try JavId per token so "1080p" is not read as a code.
            foreach (var token in value.Split(new[] { ' ', '\t', '/', '|', '_' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (JavId.TryParse(token, out var tokenId) && !string.IsNullOrEmpty(tokenId))
                {
                    return tokenId;
                }
            }

            return CodeSeparatorRegex.Replace(value, "-").ToUpperInvariant();
        }

        /// <summary>Same title when both codes match after removing spaces and hyphens.</summary>
        private static bool IsSameCode(string left, string right)
        {
            var first = CodeSeparatorRegex.Replace(left ?? string.Empty, string.Empty);
            var second = CodeSeparatorRegex.Replace(right ?? string.Empty, string.Empty);
            return first.Length > 0 && second.Length > 0
                && first.Equals(second, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Drop a trailing colon from a panel label.</summary>
        private static string NormalizeLabel(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            return raw.Replace("：", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace(":", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();
        }

        /// <summary>Keep the second of two studio spellings.</summary>
        private static string PickMaker(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var parts = value.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(o => o.Trim())
                .Where(o => o.Length > 0)
                .ToList();

            if (parts.Count == 0)
            {
                return value.Trim();
            }

            var japanese = parts.FirstOrDefault(o => JapaneseRegex.IsMatch(o));
            return japanese ?? parts[0];
        }

        /// <summary>Prefer strong.current-title, else .origin-title.</summary>
        private static string PickTitle(HtmlNode sceneData)
        {
            foreach (var xpath in new[]
            {
                "//strong[contains(@class, 'current-title')]",
                "//*[contains(@class, 'origin-title')]",
            })
            {
                var node = sceneData.SelectSingleNode(xpath);
                if (node == null)
                {
                    continue;
                }

                var title = node.InnerText.Trim();
                if (!string.IsNullOrEmpty(title))
                {
                    return title;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Cover: first img.video-cover, else og:image.
        /// </summary>
        private static string PickCover(HtmlNode sceneData)
        {
            var cover = NormalizeUrl(sceneData.SelectSingleText("//img[contains(@class, 'video-cover')]/@src"), DefaultBaseUrl);
            if (!string.IsNullOrEmpty(cover))
            {
                return cover;
            }

            return NormalizeUrl(sceneData.SelectSingleText("//meta[@property='og:image']/@content"), DefaultBaseUrl);
        }

        /// <summary>Card cover from src, or data-src when lazy loaded.</summary>
        private static string FirstImageUrl(HtmlNode card)
        {
            var image = card.SelectSingleNode(".//img");
            if (image == null)
            {
                return string.Empty;
            }

            var src = image.GetAttributeValue("src", string.Empty).Trim();
            return src.Length > 0 ? src : image.GetAttributeValue("data-src", string.Empty).Trim();
        }

        /// <summary>Login wall: the page title is the login page.</summary>
        /// <summary>
        /// Explain the wall: signing in needs a captcha and a CSRF token,
        /// so the plugin cannot renew the session.
        /// </summary>
        private static void LogLoginWallHint()
        {
            var cookie = Plugin.Instance?.Configuration?.JavDBCookie;
            if (string.IsNullOrWhiteSpace(cookie))
            {
                Logger.Info("[JavDB] This page requires a JavDB account. Paste a session cookie in the plugin settings to read actor pages and uncensored titles.");
            }
            else
            {
                Logger.Warning("[JavDB] The configured JavDB cookie no longer passes the login wall (expired or revoked). Paste a fresh cookie in the plugin settings.");
            }
        }

        private static bool IsLoginWall(HtmlNode sceneData)
        {
            var titleNode = sceneData.SelectSingleNode("//title");
            if (titleNode != null && titleNode.InnerText.TrimStart().StartsWith("登入", StringComparison.Ordinal))
            {
                return true;
            }

            try
            {
                var html = sceneData.OuterHtml;
                return !string.IsNullOrEmpty(html)
                    && html.IndexOf("<title>登入 |", StringComparison.Ordinal) >= 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Append ?locale=zh (UI language only).</summary>
        private static string WithLocale(string url)
        {
            var clean = StripLocale(url);
            return clean + (clean.IndexOf('?') >= 0 ? "&" : "?") + LocaleQuery;
        }

        /// <summary>Remove the locale parameter.</summary>
        private static string StripLocale(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return string.Empty;
            }

            var index = url.IndexOf('?');
            if (index < 0)
            {
                return url;
            }

            var path = url.Substring(0, index);
            var kept = url.Substring(index + 1)
                .Split('&')
                .Where(o => o.Length > 0 && !o.StartsWith(LocaleQuery, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return kept.Count == 0 ? path : path + "?" + string.Join("&", kept);
        }

        private static DateTime? ParseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var match = DateRegex.Match(value);
            if (!match.Success)
            {
                return null;
            }

            var text = $"{match.Groups["year"].Value}-{match.Groups["mon"].Value}-{match.Groups["day"].Value}";
            if (DateTime.TryParseExact(text, "yyyy-M-d", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                && date.Year >= 1990 && date.Year <= DateTime.UtcNow.Year + 1)
            {
                return date;
            }

            return null;
        }

        /// <summary>Make a relative URL absolute.</summary>
        private static string NormalizeUrl(string url, string baseURL)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            url = url.Trim();
            if (url.StartsWith("//", StringComparison.Ordinal))
            {
                return "https:" + url;
            }

            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            var baseUrl = string.IsNullOrEmpty(baseURL) ? DefaultBaseUrl : baseURL.TrimEnd('/');
            return url.StartsWith("/", StringComparison.Ordinal) ? baseUrl + url : $"{baseUrl}/{url}";
        }
    }
}
