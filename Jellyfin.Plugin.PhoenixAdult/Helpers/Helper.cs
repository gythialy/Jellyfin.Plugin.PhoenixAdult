using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Helpers.Utils;
using PhoenixAdult.Sites;

#if __EMBY__
using MediaBrowser.Common.Net;
#else
using System.Net.Http;
#endif

namespace PhoenixAdult.Helpers
{
    internal static class Helper
    {
        public static string GetSearchSiteName(int[] siteNum)
        {
            if (siteNum == null)
            {
                return string.Empty;
            }

            string siteName = Database.SiteList.Sites[siteNum[0]][siteNum[1]][0];
            if (string.IsNullOrEmpty(siteName))
            {
                // 组内第一个存在的站（组删除站0后不能固定访问 [0]）
                siteName = Database.SiteList.Sites[siteNum[0]].First().Value[0];
            }

            return siteName;
        }

        public static string GetSearchBaseURL(int[] siteNum)
        {
            string url = string.Empty;

            if (siteNum == null)
            {
                return url;
            }

            if (!string.IsNullOrEmpty(Database.SiteList.Sites[siteNum[0]][siteNum[1]].ElementAtOrDefault(1)))
            {
                url = Database.SiteList.Sites[siteNum[0]][siteNum[1]][1];
            }
            else
            {
                // 组内第一个带 URL 的站
                var fallback = Database.SiteList.Sites[siteNum[0]].FirstOrDefault(o => o.Value.ElementAtOrDefault(1) != null);
                url = fallback.Value?.ElementAtOrDefault(1) ?? string.Empty;
            }

            return url;
        }

        public static string GetSearchSearchURL(int[] siteNum)
        {
            string url = string.Empty;

            if (siteNum == null)
            {
                return url;
            }

            if (!string.IsNullOrEmpty(Database.SiteList.Sites[siteNum[0]][siteNum[1]].ElementAtOrDefault(2)))
            {
                url = Database.SiteList.Sites[siteNum[0]][siteNum[1]][2];
            }
            else
            {
                // 组内第一个带 search URL 的站（不能固定 First()——站0可能已被删除）
                var fallback = Database.SiteList.Sites[siteNum[0]].FirstOrDefault(o => !string.IsNullOrEmpty(o.Value.ElementAtOrDefault(2)));
                url = fallback.Value?.ElementAtOrDefault(2) ?? string.Empty;
            }

            if (!string.IsNullOrEmpty(url))
            {
                if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    url = GetSearchBaseURL(siteNum) + url;
                }
            }

            return url;
        }

        public static string Encode(string text)
            => Base58.EncodePlain(Encoding.UTF8.GetBytes(text));

        public static string Decode(string base64Text)
            => Encoding.UTF8.GetString(Base58.DecodePlain(base64Text) ?? Array.Empty<byte>());

        public static (int[] siteNum, string siteName) GetSiteFromTitle(string title)
        {
            var clearName = Regex.Replace(title, @"\W", string.Empty);
            var possibleSites = new Dictionary<int[], string>();

            foreach (var siteGroup in Database.SiteList.Sites)
            {
                foreach (var siteData in siteGroup.Value)
                {
                    var siteName = siteData.Value[0];

                    if (!string.IsNullOrEmpty(siteName))
                    {
                        var clearSite = Regex.Replace(siteName, @"\W", string.Empty);
                        if (clearName.StartsWith(clearSite, StringComparison.OrdinalIgnoreCase))
                        {
                            possibleSites.Add(new int[2] { siteGroup.Key, siteData.Key }, clearSite);
                        }
                    }
                }
            }

            (int[], string) result = (null, null);
            if (possibleSites.Any())
            {
                var site = possibleSites.OrderByDescending(o => o.Value.Length).First();
                result = (site.Key, site.Value);
            }

            return result;
        }

        public static string GetClearTitle(string title, string siteName = "")
        {
            if (string.IsNullOrEmpty(title))
            {
                return title;
            }

            string clearName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(title),
                   clearSite = siteName;

            clearName = clearName.Replace(".com", string.Empty, StringComparison.OrdinalIgnoreCase);

            // \p{L}/\p{N}：保留中日文等非 ASCII 字母数字（ASCII 行为与旧正则一致），
            // 否则中文标题会被清成空串，麻豆等中文站点永远搜不到。
            clearName = Regex.Replace(clearName, @"[^\p{L}\p{N} ']", " ");
            clearSite = Regex.Replace(clearSite, @"\W", string.Empty);

            var matched = false;
            while (!string.IsNullOrEmpty(clearSite) && clearName.Contains(' ', StringComparison.OrdinalIgnoreCase))
            {
                if (clearName.StartsWith(clearSite, StringComparison.OrdinalIgnoreCase))
                {
                    matched = true;
                    break;
                }
                else
                {
                    clearName = clearName.Replace(" ", string.Empty, 1, StringComparison.OrdinalIgnoreCase);
                }
            }

            if ((matched || !clearName.Contains(' ')) && !string.IsNullOrEmpty(clearSite))
            {
                clearName = clearName.Replace(clearSite, string.Empty, 1, StringComparison.OrdinalIgnoreCase);
            }

            clearName = string.Join(" ", clearName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

            return clearName;
        }

        /// <summary>
        /// 截取搜索词：文件名 Site.YY.MM.DD.Actors.Title 中演员/标题位置因站而异，
        /// 取前 N 词（演员）或后 N 词（标题）作为站内搜索词。
        /// 长搜索串（演员名+标题混串）会被按词 AND 匹配的站内搜索全部过滤掉，需截短。
        /// </summary>
        public static string GetSearchTitle(string title, int maxWords, bool fromEnd = false)
        {
            if (string.IsNullOrEmpty(title))
            {
                return title;
            }

            var words = title.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= maxWords)
            {
                return title;
            }

            return fromEnd
                ? string.Join(" ", words.Skip(words.Length - maxWords))
                : string.Join(" ", words.Take(maxWords));
        }

        public static (string searchTitle, DateTime? searchDateObj) GetDateFromTitle(string title)
        {
            string searchDate,
                   searchTitle = title;
            var regExRules = new Dictionary<string, string>
            {
                { @"\b[0-9]{4} [0-9]{2} [0-9]{2}\b", "yyyy MM dd" },
                { @"\b[0-9]{2} [0-9]{2} [0-9]{2}\b", "yy MM dd" },
            };
            var searchData = (searchTitle, default(DateTime?));

            foreach (var regExRule in regExRules)
            {
                var regEx = Regex.Match(searchTitle, regExRule.Key);
                if (regEx.Groups.Count > 0)
                {
                    if (DateTime.TryParseExact(regEx.Groups[0].Value, regExRule.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var searchDateObj))
                    {
                        searchDate = searchDateObj.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                        searchTitle = Regex.Replace(searchTitle, regExRule.Key, string.Empty).Trim();

                        searchTitle = string.Join(" ", searchTitle.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

                        searchData = (searchTitle, searchDateObj);
                        break;
                    }
                }
            }

            return searchData;
        }

        public static string ReplaceAbbrieviation(string title)
        {
            var newTitle = title + " ";

            foreach (var abbrieviation in Database.SiteList.Abbrieviations)
            {
                var regex = new Regex(abbrieviation.Key + " ", RegexOptions.IgnoreCase);
                if (regex.IsMatch(newTitle))
                {
                    newTitle = regex.Replace(newTitle, abbrieviation.Value + " ", 1);
                    break;
                }
            }

            newTitle = newTitle.Trim();

            return newTitle;
        }

        public static IProviderBase GetProviderBySiteID(int siteID)
        {
            if (Database.SiteList.SiteIDList != null && Database.SiteList.SiteIDList.ContainsKey(siteID))
            {
                return GetBaseSiteByName(Database.SiteList.SiteIDList[siteID]);
            }

            return null;
        }

        public static IProviderBase GetBaseSiteByName(string name)
        {
            name = $"{typeof(Plugin).Namespace}.Sites.{name}";
            var provider = Type.GetType(name, false, true);

            if (provider != null)
            {
                return (IProviderBase)Activator.CreateInstance(provider);
            }

            return null;
        }

        public static async Task<List<RemoteSearchResult>> GetSearchResultsFromUpdate(IProviderBase provider, int[] siteNum, string[] sceneID, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();

            var sceneData = await provider.Update(siteNum, sceneID, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(sceneData.Item.Name))
            {
                sceneData.Item.ProviderIds[Plugin.Instance.Name] = string.Join("#", sceneID);
                var posters = (await provider.GetImages(siteNum, sceneID, sceneData.Item, cancellationToken).ConfigureAwait(false)).Where(o => o.Type == ImageType.Primary);

                var res = new RemoteSearchResult
                {
                    ProviderIds = sceneData.Item.ProviderIds,
                    Name = sceneData.Item.Name,
                    PremiereDate = sceneData.Item.PremiereDate,
                };

                if (searchDate.HasValue && !res.PremiereDate.HasValue)
                {
                    res.PremiereDate = searchDate.Value;
                }

                if (!string.IsNullOrEmpty(sceneData.Item.OriginalTitle))
                {
                    res.Name = $"{sceneData.Item.OriginalTitle} {sceneData.Item.Name}";
                }

                if (posters.Any())
                {
                    res.ImageUrl = posters.First().Url;
                }

                result.Add(res);
            }

            return result;
        }

        public static string GetSiteNameFromTitle(string searchTitle)
        {
            if (TryExtractJavId(searchTitle, out var javId))
            {
                // 麻豆系（MadouQu / ModelMediaAsia）使用自家番号前缀
                if (Regex.IsMatch(javId, @"^(?:MD|MTVQ)[A-Z0-9]*-", RegexOptions.IgnoreCase))
                {
                    return $"MadouQu {javId}";
                }

                return $"JAV {javId}";
            }

            var clearedTitle = GetClearTitle(searchTitle, string.Empty);
            if (Plugin.Instance.Configuration.UseMetadataAPI && !string.IsNullOrEmpty(Plugin.Instance.Configuration.MetadataAPIToken))
            {
                return $"MetadataAPI {clearedTitle}";
            }

            return clearedTitle;
        }

        /// <summary>
        /// 从文件名/标题中提取 JAV 番号（保留 '-'）。支持三种形态：
        /// 1. 直接带连字符（ABP-123，任意位置，排除日期片段 blacked-24.01.14）；
        /// 2. 清理后形如 "ABP 123 ..."（前两段标签+数字，排除日期序列 26 10 20）；
        /// 3. 纯标签+数字（ssis277）。
        /// </summary>
        public static bool TryExtractJavId(string title, out string javId)
        {
            javId = null;
            if (string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            var direct = Regex.Match(title, @"(?<![A-Za-z0-9])([A-Za-z][A-Za-z0-9]{1,9})-([0-9]{2,5})(?![0-9]|[.-][0-9]{2})");
            if (direct.Success)
            {
                javId = $"{direct.Groups[1].Value.ToUpperInvariant()}-{direct.Groups[2].Value}";
                return true;
            }

            var words = GetClearTitle(title, string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 2
                && words[0].Length is >= 2 and <= 8
                && words[0].Any(char.IsLetter)
                && words[1].All(char.IsDigit)
                && words[1].Length is >= 2 and <= 5)
            {
                var dateLike = words.Length >= 3
                    && words[1].Length <= 2
                    && words[2].Length <= 2
                    && int.TryParse(words[1], out _)
                    && int.TryParse(words[2], out _);
                if (!dateLike && JavId.TryParse($"{words[0]}-{words[1]}", out var candidate))
                {
                    javId = candidate;
                    return true;
                }
            }

            if (JavId.TryParse(title.Trim(), out var single))
            {
                javId = single;
                return true;
            }

            javId = null;
            return false;
        }

        public static byte[] ConvertFromBase64String(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return null;
            }

            try
            {
                var working = input.Replace('-', '+').Replace('_', '/');
                while (working.Length % 4 != 0)
                {
                    working += '=';
                }

                return Convert.FromBase64String(working);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 部分图片 CDN 对签名 URL 做 Referer 防盗链，缺少 Referer 时直接 403。
        /// 仅对已知需要 Referer 的主机补上，避免影响其他站点的图片代理。
        /// </summary>
        /// <param name="url">图片地址。</param>
        /// <returns>需要发送的 Referer；无需发送时返回 null。</returns>
        internal static string GetImageReferer(string url)
            => ResolveImageReferer(url, Plugin.Instance?.Configuration?.ImageReferers);

        /// <summary>
        /// 解析 Referer 规则：用户规则优先（格式 "host=referer"，referer 为空表示显式禁用），
        /// 其次回退到 <see cref="Consts.ImageReferers"/> 的内置表。
        /// </summary>
        /// <param name="url">图片地址。</param>
        /// <param name="userRules">用户配置的规则行。</param>
        /// <returns>需要发送的 Referer；无需发送时返回 null。</returns>
        internal static string ResolveImageReferer(string url, IEnumerable<string> userRules)
        {
            if (string.IsNullOrEmpty(url))
            {
                return null;
            }

            if (userRules != null)
            {
                foreach (var line in userRules)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    var host = line.Substring(0, separator).Trim();
                    var referer = line.Substring(separator + 1).Trim();

                    if (host.Length > 0 && url.Contains(host, StringComparison.OrdinalIgnoreCase))
                    {
                        return referer.Length == 0 ? null : referer;
                    }
                }
            }

            foreach (var rule in Consts.ImageReferers)
            {
                if (url.Contains(rule.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return rule.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// 从 HTML 列表节点提取海报地址（识别对话框只渲染 RemoteSearchResult.ImageUrl）。
        /// 懒加载站点的真实地址常在 data-src / data-original 里，src 可能是占位图，故按优先级尝试；
        /// 同时兼容节点自身的 data-poster 与 style="background-image:url(...)"。相对地址用 baseUrl 补全。
        /// </summary>
        /// <param name="node">列表节点。</param>
        /// <param name="baseUrl">站点根地址（如 https://www.example.com）。</param>
        /// <returns>可直接给客户端加载的绝对地址；取不到返回 null。</returns>
        internal static string GetImageURL(HtmlNode node, string baseUrl = null)
        {
            if (node == null)
            {
                return null;
            }

            var candidates = new List<string>();
            var image = string.Equals(node.Name, "img", StringComparison.OrdinalIgnoreCase) ? node : node.SelectSingleNode(".//img");
            if (image != null)
            {
                foreach (var attribute in new[] { "data-src", "data-original", "data-lazy-src", "data-lazy", "data-echo", "data-thumb", "data-srcset", "srcset", "src" })
                {
                    var value = image.GetAttributeValue(attribute, null);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        candidates.Add(value);
                    }
                }
            }

            if (image != null)
            {
                // 部分站点用自定义属性存图（如 AuntJudys 的 src0_1x / src0_2x），
                // 兜底扫描所有“像图片地址”的属性值。
                foreach (var attribute in image.Attributes)
                {
                    var value = attribute.Value;
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    var looksLikeImage = (attribute.Name.Contains("src", StringComparison.OrdinalIgnoreCase)
                        || attribute.Name.Contains("image", StringComparison.OrdinalIgnoreCase))
                        && (value.Contains(".jpg", StringComparison.OrdinalIgnoreCase)
                            || value.Contains(".jpeg", StringComparison.OrdinalIgnoreCase)
                            || value.Contains(".png", StringComparison.OrdinalIgnoreCase)
                            || value.Contains(".webp", StringComparison.OrdinalIgnoreCase));
                    if (looksLikeImage && !candidates.Contains(value))
                    {
                        candidates.Add(value);
                    }
                }
            }

            foreach (var attribute in new[] { "data-poster", "data-image", "data-bg", "data-background", "data-background-image", "data-thumb" })
            {
                var value = node.GetAttributeValue(attribute, null);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    candidates.Add(value);
                }
            }

            var style = node.GetAttributeValue("style", null);
            if (!string.IsNullOrWhiteSpace(style))
            {
                var match = Regex.Match(style, @"url\(['""]?(.*?)['""]?\)");
                if (match.Success)
                {
                    candidates.Add(match.Groups[1].Value);
                }
            }

            // 有些站点的缩略图是子元素的 CSS 背景图（div style="background-image:url(...)"）
            // 或挂在 data-bg / data-src 上，这里再扫一遍子元素。
            foreach (var element in node.Descendants().Take(64))
            {
                var elementStyle = element.GetAttributeValue("style", null);
                if (!string.IsNullOrWhiteSpace(elementStyle))
                {
                    var match = Regex.Match(elementStyle, @"url\(['""]?(.*?)['""]?\)");
                    if (match.Success)
                    {
                        candidates.Add(match.Groups[1].Value);
                    }
                }

                foreach (var attribute in new[] { "data-bg", "data-background-image", "data-src", "data-poster" })
                {
                    var value = element.GetAttributeValue(attribute, null);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        candidates.Add(value);
                    }
                }
            }

            foreach (var candidate in candidates)
            {
                var url = candidate.Split(' ')[0].Split(',')[0].Trim().Trim('"', '\'');
                if (string.IsNullOrEmpty(url) || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // 列表页里常有站点头图/图标在同一节点内，命中这些关键词的多半不是海报
                // 有些站点的预览是视频文件（preview_320.mp4），不能当缩略图
                if (url.Contains(".mp4", StringComparison.OrdinalIgnoreCase)
                    || url.Contains(".webm", StringComparison.OrdinalIgnoreCase)
                    || url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (url.Contains("logo", StringComparison.OrdinalIgnoreCase)
                    || url.Contains("favicon", StringComparison.OrdinalIgnoreCase)
                    || url.Contains("/icons/", StringComparison.OrdinalIgnoreCase)
                    || url.Contains("/icon/", StringComparison.OrdinalIgnoreCase)
                    || url.Contains("sprite", StringComparison.OrdinalIgnoreCase)
                    || url.Contains("placeholder", StringComparison.OrdinalIgnoreCase)
                    || url.Contains("horizontalline", StringComparison.OrdinalIgnoreCase)
                    || url.Contains("spacer", StringComparison.OrdinalIgnoreCase)
                    || url.Contains("divider", StringComparison.OrdinalIgnoreCase)
                    || url.Contains("bullet", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (url.StartsWith("//", StringComparison.Ordinal))
                {
                    return "https:" + url;
                }

                if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    return url;
                }

                if (string.IsNullOrEmpty(baseUrl))
                {
                    continue;
                }

                var root = baseUrl.TrimEnd('/');
                return url.StartsWith("/", StringComparison.Ordinal) ? root + url : root + "/" + url;
            }

            return null;
        }

        /// <summary>
        /// 图片请求所需的额外请求头（当前仅 Referer）；无需附加时返回 null。
        /// </summary>
        /// <param name="url">图片地址。</param>
        /// <returns>请求头字典或 null。</returns>
        internal static IDictionary<string, string> GetImageRefererHeaders(string url)
        {
            var referer = GetImageReferer(url);
            return referer == null ? null : new Dictionary<string, string> { { "Referer", referer } };
        }

#if __EMBY__
        public static Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var options = new HttpRequestOptions
            {
                CancellationToken = cancellationToken,
                Url = url,
                EnableDefaultUserAgent = false,
                UserAgent = HTTP.GetUserAgent(),
            };

            var referer = GetImageReferer(url);
            if (referer != null)
            {
                // Emby 的 HttpRequestOptions.RequestHeaders 为只读集合，只能逐项写入
                options.RequestHeaders["Referer"] = referer;
            }

            return Plugin.Http.GetResponse(options);
        }
#else
        public static Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", HTTP.GetUserAgent());

            var referer = GetImageReferer(url);
            if (referer != null)
            {
                request.Headers.TryAddWithoutValidation("Referer", referer);
            }

            return Plugin.Http.CreateClient().SendAsync(request, cancellationToken);
        }
#endif

        public static DateTime GetLinkerTime(Assembly assembly)
        {
            const string BuildVersionMetadataPrefix = "+build";

            var attribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (attribute?.InformationalVersion != null)
            {
                var value = attribute.InformationalVersion;
                var index = value.IndexOf(BuildVersionMetadataPrefix);
                if (index > 0)
                {
                    value = value.Substring(index + BuildVersionMetadataPrefix.Length);
                    return DateTime.ParseExact(value, "yyyy-MM-ddTHH:mm:ss:fffZ", CultureInfo.InvariantCulture);
                }
            }

            return default;
        }

        public static IProviderBase GetMetadataAPIProvider()
        {
            return GetProviderBySiteID(48);
        }

        public static string ParseTitle(string title, int[] siteNum)
        {
            if (string.IsNullOrEmpty(title))
            {
                return title;
            }

            var s = Regex.Replace(title, @"w\/(?!\s)", "w/ ", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\,(?!\s|\d)", ", ");
            s = s.Replace("_", " ");
            s = s.Replace("’", "'");
            s = s.Replace("´", "'");

            var wordList = s.Split(' ').ToList();
            var final = new List<string>();

            var pattern = new Regex(@"\W");
            var firstWord = ParseWord(wordList[0]);
            var cleanFirstWord = pattern.Replace(firstWord, string.Empty);
            if (cleanFirstWord.Length > 1)
            {
                firstWord = char.ToUpper(firstWord[0]) + firstWord.Substring(1);
            }
            else
            {
                firstWord = firstWord.ToUpper();
            }

            final.Add(firstWord);

            for (int i = 1; i < wordList.Count; i++)
            {
                final.Add(ParseWord(wordList[i]));
            }

            var output = string.Join(" ", final);
            return output;
        }

        public static Task<IDictionary<string, string>> NCookies(int[] siteNum, CancellationToken cancellationToken = default)
            => CaptchaHelper.NCookies(siteNum, cancellationToken);

        private static string ParseWord(string word)
        {
            var lowerExceptions = new List<string> { "a", "y", "n", "an", "of", "the", "and", "for", "to", "onto", "but", "or", "nor", "at", "with", "vs", "in", "on", "com", "co", "org" };
            var upperExceptions = new List<string> { "bbc", "xxx", "bbw", "bf", "bff", "bts", "pov", "dp", "gf", "bj", "wtf", "cfnm", "bwc", "fm", "tv", "ai", "hd", "milf", "gilf", "dilf", "dtf", "zz", "xxxl", "usa", "nsa", "hr", "ii", "iii", "iv", "bbq", "avn", "xtc", "atv", "joi", "rpg", "wunf", "uk", "asap", "sss", "nf", "pawg" };
            var symbolsClean = new List<char> { '-', '/', '.', '+', '\'' };

            var pattern = new Regex(@"\W");
            var cleanWord = pattern.Replace(word, string.Empty);

            if (symbolsClean.Any(s => word.Contains(s)))
            {
                return word;
            }
            else if (upperExceptions.Contains(cleanWord.ToLower()))
            {
                return word.ToUpper();
            }
            else if (word.All(char.IsUpper) && !lowerExceptions.Contains(cleanWord.ToLower()))
            {
                return word.ToUpper();
            }
            else if (!word.All(char.IsLower) && !word.All(char.IsUpper) && !lowerExceptions.Contains(cleanWord.ToLower()))
            {
                return word;
            }
            else
            {
                return lowerExceptions.Contains(cleanWord.ToLower()) ? word.ToLower() : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(word);
            }
        }
    }
}
