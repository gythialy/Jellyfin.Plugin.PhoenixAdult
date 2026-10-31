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
    /// JavDB（javdb.com）。条目 ID 是详情页绝对地址 https://javdb.com/v/{id}。
    /// <para>
    /// 走的是匿名可读的 HTML 页面，不依赖任何 token：
    /// 搜索 /search?q={番号}&amp;f=all，详情 /v/{id}?locale=zh（?locale=zh 只切界面语言，
    /// 面板标签固定成 番號/日期/時長/導演/片商/發行/系列/類別/演員）。
    /// </para>
    /// <para>
    /// 限制：演员页与无码作品页要求登录（302 到 /login，回的是「登入 | JavDB …」标题页），
    /// 未配置 Cookie 时这两类页面按「未取得」降级，不影响有码作品。
    /// 站方按出口 IP 计配额，超了回 403 并封 3~7 天，所以本 handler 一次搜索只发一次请求。
    /// </para>
    /// </summary>
    public class SiteJavDB : IProviderBase
    {
        /// <summary>JavDB 图片 CDN（公开，无需 Referer/Cookie）。</summary>
        private const string CdnBaseUrl = "https://c0.jdbstatic.com";

        private const string DefaultBaseUrl = "https://javdb.com";

        private const string DefaultSearchPath = "/search?q=";

        private const string LocaleQuery = "locale=zh";

        /// <summary>一次 GetImages 最多交出多少张（封面 1 张 + 样图若干）。</summary>
        private const int MaxImages = 30;

        /// <summary>片长只取「時長」一栏里的数字，站上写法形如 "180 分鍾" / "60分鐘" / "41:50"。</summary>
        private static readonly Regex RuntimeRegex = new Regex(
            @"(?<min>[0-9]+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>发行日期 "2022-04-19"；站上偶有 "/" 分隔。</summary>
        private static readonly Regex DateRegex = new Regex(
            @"(?<year>[0-9]{4})[-/.](?<mon>[0-9]{1,2})[-/.](?<day>[0-9]{1,2})",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>番号比较用：空格、连字符、下划线都不算区别（站上会写成 "MIAA -620"）。</summary>
        private static readonly Regex CodeSeparatorRegex = new Regex(
            @"[\s\-_]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>片商一栏并列英日两种写法（"PRESTIGE,プレステージ"），取含日文的那一段。</summary>
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

                // 结果卡片（2026-10-30 实测 /search?q=HEYZO-0924&f=all）：
                //   <div class="movie-list …"><div class="item">
                //     <a href="/v/xZMJQ" class="box" title="HAMEZO～… - 綾瀬まゆ">
                //       <div class="cover "><img loading="lazy" src="https://c0.jdbstatic.com/covers/xz/xZMJQ.jpg" /></div>
                //       <div class="video-title"><strong>HEYZO-0924</strong> HAMEZO～…</div>
                //     </a>
                //   </div></div>
                // 演员卡也带 box 类（class="box actor-box"），靠 href 必须是 /v/ 排除。
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

                    // 卡片里 <div class="meta">2015-08-23</div> 是发行日期：填进 PremiereDate，
                    // 识别对话框才会显示日期、多源结果也能按日期排序（以前这条信息被直接丢掉了）。
                    var metaDate = card.SelectSingleNode(".//div[contains(@class, 'meta')]")?.InnerText.Trim();
                    if (!string.IsNullOrEmpty(metaDate)
                        && DateTime.TryParseExact(metaDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var metaDateValue))
                    {
                        res.PremiereDate = metaDateValue;
                        res.ProductionYear = metaDateValue.Year;
                    }

                    // 卡片结构偶尔没有 <img>（懒加载/无码条目），用 /v/<id> 反推 CDN 封面路径，
                    // 与 JavDB 自己的 covers/<id 前两位小写>/<id>.jpg 规则一致。
                    if (string.IsNullOrEmpty(res.ImageUrl))
                    {
                        var movieId = href.Trim('/').Split('/').LastOrDefault();
                        if (!string.IsNullOrEmpty(movieId) && movieId.Length > 2)
                        {
                            res.ImageUrl = $"{CdnBaseUrl}/covers/{movieId.Substring(0, 2).ToLowerInvariant()}/{movieId}.jpg";
                        }
                    }

                    result.Add(res);

                    // 同号多条只取第一条：站上同一部片偶有重复条目，挑哪条都不比第一条更可信。
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
                    // 演员页与无码作品页要求登录：记一笔就走，不抛异常，也不把登录页当作品页解析。
                    Logger.Info($"[JavDB] Login wall hit for '{sceneURL}'.");
                    LogLoginWallHint();
                    return result;
                }

                // 详情面板（2026-10-30 实测 /v/ZXw5Jv?locale=zh）：
                //   <div class="panel-block first-block"><strong>番號:</strong>&nbsp;<span class="value">ABW -358</span></div>
                //   <div class="panel-block"><strong>片商:</strong>&nbsp;<span class="value">PRESTIGE,プレステージ</span></div>
                //   <div class="panel-block"><strong>演員:</strong>&nbsp;<span class="value">
                //       <a class="actor-female" href="/actors/a0bn">姫咲はな</a>, <a href="/actors/qDOVD">およよ中野</a>
                //   </span></div>
                // 男优挂的是同样的 /actors/ 链接，没有 actor-female 类，按类筛掉。
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
                    // 面板与标题都没解析出来：当作未取得，别把空条目写进库里。
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

                // 片商为主，發行（label）与系列按本仓库 JAV handler 的惯例一并记进 Studio。
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

                // 样图（2026-10-30 实测）：<a class="tile-item" href="https://c0.jdbstatic.com/samples/jr/JR4XW_l_0.jpg">
                //   <img src="…/JR4XW_s_0.jpg" /></a>，href 就是 _l_ 全尺寸，缩略图 _s_ 不要。
                // 都在 c0.jdbstatic.com 上，是公开 CDN，无需 Referer、无签名。
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

        /// <summary>站主域；未注册或取不到时回落到 https://javdb.com。</summary>
        private static string GetBaseUrl(int[] siteNum)
        {
            var baseUrl = string.Empty;
            try
            {
                baseUrl = Helper.GetSearchBaseURL(siteNum);
            }
            catch (Exception)
            {
                // 组 255 还没注册时取不到，用内置地址顶上，别让 Search 直接失败。
            }

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = DefaultBaseUrl;
            }

            return baseUrl.TrimEnd('/');
        }

        /// <summary>搜索地址：站点表里是相对路径（/search?q=），拼成绝对地址。</summary>
        private static string GetSearchUrl(int[] siteNum)
        {
            var searchUrl = string.Empty;
            try
            {
                searchUrl = Helper.GetSearchSearchURL(siteNum);
            }
            catch (Exception)
            {
                // 同上：取不到就用内置路径。
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
        /// 把配置里整段粘贴的 Cookie 拆成字典："a=b; c=d"。空配置返回 null（不传 Cookie）。
        /// 站上 "over18=1" 这类年龄门与登录态都在这一段里，由用户自己从浏览器复制。
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
        /// 取一页 HTML：先直连，只有真撞上 Cloudflare 验证页或直连失败时才交给 Fetcher 兜底
        /// （Fetcher 会走 FlareSolverr）。
        /// <para>
        /// 不能直接调 Fetcher.Html 的原因：它的挑战判定把 "challenge-platform" 也算命中，而
        /// javdb 的**正常页面**里就有这个脚本（2026-10-30 实测搜索页与详情页各出现 1 次），
        /// 直接用它会让每一次请求都判成挑战：装了 FlareSolverr 的用户每次多付一趟无头浏览器，
        /// 没装的每次多一条错误日志。这里按标题判挑战，正常页走最快的直连。
        /// </para>
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

        /// <summary>非 200 时 HTML.ElementFromURL 返回未挂载的 DocumentNode，OuterHtml 会抛异常。</summary>
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

        /// <summary>只按标题判 Cloudflare 验证页：正文里也注入 challenge-platform 脚本，按它判会误判。</summary>
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

        /// <summary>从 sceneID 还原详情页绝对地址；也兼容手工识别时直接传入的裸地址。</summary>
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

        /// <summary>sceneID 由本 handler 编码；解码失败（手工 Identify 直接给 URL）时按裸值处理。</summary>
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
                // 非 Base58 输入会抛 FormatException，按裸值处理。
            }

            return value;
        }

        /// <summary>番号的规范形态：JavId 认得出就用它的结果，否则去掉空白后大写。</summary>
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

            // 输入带了标题（识别页手填 "ABW-358 涼森れむ…"）时，逐个词交给 JavId 试一次，
            // 比在整串上做正则提取安全：不会把 "1080p" 之类当成番号。
            foreach (var token in value.Split(new[] { ' ', '\t', '/', '|', '_' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (JavId.TryParse(token, out var tokenId) && !string.IsNullOrEmpty(tokenId))
                {
                    return tokenId;
                }
            }

            return CodeSeparatorRegex.Replace(value, "-").ToUpperInvariant();
        }

        /// <summary>两个番号写法是否指同一部片：去掉空格与连字符后不区分大小写比较。</summary>
        private static bool IsSameCode(string left, string right)
        {
            var first = CodeSeparatorRegex.Replace(left ?? string.Empty, string.Empty);
            var second = CodeSeparatorRegex.Replace(right ?? string.Empty, string.Empty);
            return first.Length > 0 && second.Length > 0
                && first.Equals(second, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>面板标签去掉尾部冒号（站上是全角 "："，个别镜像写成半角）。</summary>
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

        /// <summary>片商并列英日两种写法时取日文那一段（"PRESTIGE,プレステージ" → "プレステージ"）。</summary>
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

        /// <summary>标题优先取 strong.current-title；缺失时退到 .origin-title（日文原标题）。</summary>
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
        /// 封面：详情页第一张 img.video-cover（第二张同类图在「預告片」入口里，src 与它相同）。
        /// 都取不到时退到 og:image。
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

        /// <summary>卡片封面：第一张 img 的 src，懒加载站点可能写在 data-src。</summary>
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

        /// <summary>登录墙判定：登录页标题是「登入 | JavDB 成人影片數據庫」。</summary>
        /// <summary>
        /// 登录墙提示：JavDB 的登录需要图形验证码（rucaptcha）与 CSRF token，插件无法自动登录/续期，
        /// 所以只能在 Cookie 失效时把话说明白——配了 Cookie 就是「过期了，去换一个」，没配就是「这类页面本来就要登录」。
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

        /// <summary>详情页补 ?locale=zh（只切界面语言，不改任何数据字段）。</summary>
        private static string WithLocale(string url)
        {
            var clean = StripLocale(url);
            return clean + (clean.IndexOf('?') >= 0 ? "&" : "?") + LocaleQuery;
        }

        /// <summary>去掉 URL 里的 locale 参数：ExternalId 记不带界面语言的地址。</summary>
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

        /// <summary>相对地址补成绝对地址；站上图片本来就是 https://c0.jdbstatic.com/... 绝对地址。</summary>
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
