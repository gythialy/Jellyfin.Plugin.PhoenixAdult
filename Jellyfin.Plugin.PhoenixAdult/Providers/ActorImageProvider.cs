using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using HtmlAgilityPack;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Newtonsoft.Json.Linq;
using PhoenixAdult.Configuration;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

#if __EMBY__
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Configuration;
#else
using System.Net.Http;
#endif

namespace PhoenixAdult.Providers
{
    public class ActorImageProvider : IRemoteImageProvider
    {
        private const string JavDBBaseUrl = "https://javdb.com";

        /// <summary>
        /// JavDB 的"暂无头像"占位图。它和真头像一样返回 200 image/jpeg，
        /// 不按 URL 排除掉的话，所有查不到头像的演员都会挂上这同一张灰图。
        /// </summary>
        private const string JavDBActorUnknownImage = "https://c0.jdbstatic.com/images/actor_unknow.jpg";

        /// <summary>
        /// 封面兜底结果的来源标记：让用户能分辨"这是作品封面而不是演员头像"。
        /// 同时用作 GetImages 里"本次是否已经试过封面兜底"的判据，避免重复请求。
        /// </summary>
        private const string JavDBCoverProviderName = "JavDB (cover)";

        private static readonly TimeSpan GFriendsCacheTtl = TimeSpan.FromHours(2);
        private static readonly SemaphoreSlim GFriendsLock = new SemaphoreSlim(1, 1);
        private static JObject gFriendsTree;
        private static DateTime gFriendsTreeTime = DateTime.MinValue;

        public string Name => Plugin.Instance.Name;

        public static async Task<List<RemoteImageInfo>> GetActorPhotos(string name, CancellationToken cancellationToken)
        {
            var tasks = new Dictionary<string, Task<string>>();
            var imageList = new List<RemoteImageInfo>();

            if (string.IsNullOrEmpty(name))
            {
                return imageList;
            }

            if (Plugin.Instance.Configuration.PreferedActorNameSource == PreferedActorNameSource.LocalDatabase)
            {
                name = Actors.ResolveWithJapaneseStyle(name);
            }

            Logger.Info($"Searching actor images for \"{name}\"");

            tasks.Add("AdultDVDEmpire", GetFromAdultDVDEmpire(name, cancellationToken));
            tasks.Add("Boobpedia", GetFromBoobpedia(name, cancellationToken));
            tasks.Add("Babepedia", GetFromBabepedia(name, cancellationToken));
            tasks.Add("IAFD", GetFromIAFD(name, cancellationToken));
            tasks.Add("Gfriends", GetFromGFriends(name, cancellationToken));
            tasks.Add("JavDB", GetFromJavDB(name, cancellationToken));

            var splitedName = name.Split();
            switch (Plugin.Instance.Configuration.JAVActorNamingStyle)
            {
                case JAVActorNamingStyle.JapaneseStyle:
                    if (splitedName.Length > 1)
                    {
                        var nameReversed = string.Join(" ", splitedName.Reverse());

                        tasks.Add("Boobpedia 2", GetFromBoobpedia(nameReversed, cancellationToken));
                        tasks.Add("Babepedia 2", GetFromBabepedia(nameReversed, cancellationToken));
                        tasks.Add("IAFD 2", GetFromIAFD(nameReversed, cancellationToken));
                    }

                    break;
            }

            try
            {
                await Task.WhenAll(tasks.Values).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Logger.Error($"GetActorPhotos error: \"{e}\"");

                await Analytics.Send(
                    new AnalyticsExeption
                    {
                        Request = name,
                        Exception = e,
                    }, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                foreach (var image in tasks)
                {
                    // 按任务状态取值：任一源抛异常只丢该源的结果。
                    // 原来直接用 image.Value.Result，只要一个源炸了（站点改版常见的空引用）就会抛
                    // AggregateException，把其它源已经拿到的头像一起作废。
                    if (image.Value.Status != TaskStatus.RanToCompletion)
                    {
                        Logger.Debug($"Actor image source \"{image.Key}\" failed: {image.Value.Exception?.GetBaseException().Message}");

                        continue;
                    }

                    var res = image.Value.Result;

                    if (!string.IsNullOrEmpty(res) && !imageList.Where(o => o.Url == res).Any())
                    {
                        imageList.Add(new RemoteImageInfo
                        {
                            ProviderName = image.Key,
                            Url = res,
                        });
                    }
                }

                // 封面兜底：只有所有头像源都空、且用户没关掉该开关时才用。
                // 作品封面不是头像，仅作为"有图总比没图好"的最后手段，故单独标注来源。
                if (imageList.Count == 0 && !cancellationToken.IsCancellationRequested && Plugin.Instance.Configuration.ActorPhotoCoverFallback)
                {
                    try
                    {
                        var cover = await GetFromJavDBCover(name, cancellationToken).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(cover))
                        {
                            imageList.Add(new RemoteImageInfo
                            {
                                ProviderName = JavDBCoverProviderName,
                                Url = cover,
                                Type = ImageType.Primary,
                            });
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error($"JavDB cover fallback error: \"{e.Message}\"");
                    }
                }
            }

            return imageList;
        }

        public bool Supports(BaseItem item) => item is Person;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
            => new List<ImageType>
            {
                ImageType.Primary,
            };

#if __EMBY__
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, LibraryOptions libraryOptions, CancellationToken cancellationToken)
#else
        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
#endif
        {
            var images = new List<RemoteImageInfo>();

            if (item == null)
            {
                return images;
            }

            images = await GetActorPhotos(item.Name, cancellationToken).ConfigureAwait(false);

            // GetActorPhotos 返回空列表 = 它已经试过封面兜底但没结果；返回的列表里带 "JavDB (cover)" = 试过且拿到了封面。
            // 两种情况都说明"本次已试过"，下面校验后为空时就不必用同样的查询再打一次 javdb。
            var coverFallbackTried = images.Count == 0
                || images.Any(o => string.Equals(o.ProviderName, JavDBCoverProviderName, StringComparison.Ordinal));

            if (item.ProviderIds.TryGetValue(this.Name, out var externalID))
            {
                var curID = externalID.Split('#');
                if (curID.Length > 2)
                {
                    var siteNum = new int[2] { int.Parse(curID[0], CultureInfo.InvariantCulture), int.Parse(curID[1], CultureInfo.InvariantCulture) };
                    var sceneID = item.ProviderIds;

                    if (sceneID.ContainsKey(this.Name))
                    {
                        var provider = Helper.GetProviderBySiteID(siteNum[0]);
                        if (provider != null)
                        {
                            IEnumerable<RemoteImageInfo> imgs = new List<RemoteImageInfo>();
                            try
                            {
                                imgs = await provider.GetImages(siteNum, curID.Skip(2).ToArray(), item, cancellationToken).ConfigureAwait(false);
                            }
                            catch (Exception e)
                            {
                                Logger.Error($"GetImages error: \"{e}\"");

                                await Analytics.Send(
                                    new AnalyticsExeption
                                    {
                                        Request = string.Join("#", curID.Skip(2)),
                                        SiteNum = siteNum,
                                        Exception = e,
                                    }, cancellationToken).ConfigureAwait(false);
                            }
                            finally
                            {
                                if (imgs.Any())
                                {
                                    images.AddRange(imgs);
                                }
                            }
                        }
                    }
                }
            }

            images = await ImageHelper.GetImagesSizeAndValidate(images, cancellationToken).ConfigureAwait(false);

            // 需要 Referer 防盗链的图（JavDB 返回的 javbus/phncdn 地址等）Jellyfin 自己下载会 403，改成插件代理地址
            images = ImageProxyUrl.WrapForDownload(images);

            // 二次兜底：某个源返回的图可能在 HEAD 校验时 403/失效而被整体清空，
            // 此时再补一次封面兜底，否则用户最终一张图都没有。
            if (!images.Any() && !coverFallbackTried && !cancellationToken.IsCancellationRequested
                && Plugin.Instance.Configuration.ActorPhotoCoverFallback)
            {
                var cover = await GetFromJavDBCover(item.Name, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(cover))
                {
                    var coverImage = new RemoteImageInfo
                    {
                        ProviderName = JavDBCoverProviderName,
                        Url = cover,
                        Type = ImageType.Primary,
                    };

                    images = await ImageHelper.GetImagesSizeAndValidate(new[] { coverImage }, cancellationToken).ConfigureAwait(false);
                    images = ImageProxyUrl.WrapForDownload(images);
                }
            }

            if (images.Any())
            {
                foreach (var img in images)
                {
                    if (string.IsNullOrEmpty(img.ProviderName))
                    {
                        img.ProviderName = this.Name;
                    }
                }

                images = images.OrderByDescending(o => o.Height).ToList();
            }

            return images;
        }

#if __EMBY__
        public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
#else
        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
#endif
        {
            return Helper.GetImageResponse(url, cancellationToken);
        }

        /// <summary>
        /// AdultDVDEmpire 的年龄门：performer/search 会 302 到 /AgeConfirmation，
        /// 没有 ageConfirmed cookie 时永远只会拿到"我是否成年"的门页，列表节点不存在。
        /// 站内"I am 18+"按钮（SiteWide.js 的 ageConfirmationClicked）实际打的就是这个端点，
        /// 所以这里先打一次拿 cookie，再把 cookie 显式传给后续请求——不能依赖其它 handler
        /// 恰好往共享 CookieContainer 里种过这个域名的 cookie。
        /// </summary>
        private static async Task<IDictionary<string, string>> GetAdultDVDEmpireCookies(CancellationToken cancellationToken)
        {
            var http = await HTTP.Request("https://www.adultdvdempire.com/Account/AgeConfirmation?ageConfirmationClicked=true", cancellationToken).ConfigureAwait(false);

            var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (http.Cookies != null)
            {
                foreach (var cookie in http.Cookies)
                {
                    cookies[cookie.Name] = cookie.Value;
                }
            }

            return cookies.Count > 0 ? cookies : null;
        }

        private static async Task<string> GetFromAdultDVDEmpire(string name, CancellationToken cancellationToken)
        {
            string image = null;

            if (string.IsNullOrEmpty(name))
            {
                return image;
            }

            string encodedName = HttpUtility.UrlEncode(name),
                   url = $"https://www.adultdvdempire.com/performer/search?q={encodedName}";

            var cookies = await GetAdultDVDEmpireCookies(cancellationToken).ConfigureAwait(false);

            var actorData = await HTML.ElementFromURL(url, cancellationToken, null, cookies).ConfigureAwait(false);

            var actorPageURL = actorData.SelectSingleText("//div[@id='performerlist']/div//a/@href");
            if (!string.IsNullOrEmpty(actorPageURL))
            {
                if (!actorPageURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    actorPageURL = "https://www.adultdvdempire.com" + actorPageURL;
                }

                var actorPage = await HTML.ElementFromURL(actorPageURL, cancellationToken, null, cookies).ConfigureAwait(false);

                var img = actorPage.SelectSingleText("//div[contains(@class, 'performer-image-container')]/a/@href");
                if (!string.IsNullOrEmpty(img))
                {
                    image = img;
                }
            }

            return image;
        }

        private static async Task<string> GetFromBoobpedia(string name, CancellationToken cancellationToken)
        {
            string image = null;

            if (string.IsNullOrEmpty(name))
            {
                return image;
            }

            string encodedName = HttpUtility.UrlEncode(name),
                   url = $"http://www.boobpedia.com/wiki/index.php?search={encodedName}";

            var actorData = await HTML.ElementFromURL(url, cancellationToken).ConfigureAwait(false);

            // 现站（MediaWiki 新版）的 infobox 是 class="infobox plainlinks"、图在
            // <a class="mw-file-description"><img class="mw-file-element" src="/wiki/images/thumb/...">，
            // 旧的 @class='infobox' + a.image 精确匹配永远取不到，新选择器为主、旧选择器留作兜底。
            var img = actorData.SelectSingleText("//table[contains(@class, 'infobox')]//img[contains(@class, 'mw-file-element')]/@src");
            if (string.IsNullOrEmpty(img))
            {
                img = actorData.SelectSingleText("//table[@class='infobox']//a[@class='image']//img/@src");
            }

            if (!string.IsNullOrEmpty(img) && !img.Contains("NoImage", StringComparison.OrdinalIgnoreCase))
            {
                if (!img.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    // 站点已全站 301 到 https，直接补 https 少一跳。
                    img = "https://www.boobpedia.com" + img;
                }

                image = img;
            }

            return image;
        }

        private static async Task<string> GetFromBabepedia(string name, CancellationToken cancellationToken)
        {
            string image = null;

            if (string.IsNullOrEmpty(name))
            {
                return image;
            }

            string encodedName = name.Replace(" ", "_", StringComparison.OrdinalIgnoreCase),
                   url = $"https://www.babepedia.com/babe/{encodedName}";

            var actorData = await HTML.ElementFromURL(url, cancellationToken).ConfigureAwait(false);

            var img = actorData.SelectSingleText("//div[@id='profimg']/a/@href");
            if (!string.IsNullOrEmpty(img) && !img.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            {
                if (!img.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    img = "https://www.babepedia.com" + img;
                }

                image = img;
            }

            return image;
        }

        /// <summary>
        /// 判断 JavDB 页面是否落在登录墙（未登录/登录态过期时服务器会 302 到 /login）。
        /// 只认 <title> 里的登录字样：正常页面标题不会包含这些词。
        /// </summary>
        private static bool IsJavDBLoginWall(HtmlNode page)
        {
            var title = page?.SelectSingleNode("//head/title")?.InnerText ?? string.Empty;
            return title.IndexOf("登入", StringComparison.Ordinal) >= 0
                || title.IndexOf("登录", StringComparison.Ordinal) >= 0
                || title.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 解析 "a=b; c=d" 形式的 JavDB cookie 配置；空串表示未登录，返回 null 让请求不带 cookie。
        /// 登录态只用于读演员详情页的大图，搜索页免登录也能用。
        /// </summary>
        private static IDictionary<string, string> ParseJavDBCookies(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in raw.Split(';'))
            {
                var pair = item.Split(new[] { '=' }, 2);
                if (pair.Length == 2 && !string.IsNullOrWhiteSpace(pair[0]))
                {
                    cookies[pair[0].Trim()] = pair[1].Trim();
                }
            }

            return cookies.Count > 0 ? cookies : null;
        }

        private static IDictionary<string, string> JavDBHeaders()
            => new Dictionary<string, string> { { "Referer", "https://javdb.com/" } };

        /// <summary>
        /// 演员卡片的 title 是逗号分隔的别名列表（如 "三上悠亜, 三上悠亞, 鬼头桃菜"）。
        /// 任一别名与查询名完全相同才算同一个人，不做模糊匹配，避免把"姫咲はな"之类当成"姫咲みはる"。
        /// </summary>
        private static bool IsSameActorName(string titleNames, string name)
        {
            if (string.IsNullOrWhiteSpace(titleNames) || string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            foreach (var candidate in titleNames.Split(',', '，'))
            {
                if (string.Equals(candidate.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 过滤 JavDB 的"暂无头像"占位图：actor_unknow.jpg 对所有演员都返回 200，
        /// 不过滤的话每个查不到头像的人都会拿到同一张灰图，比没图更糟。
        /// </summary>
        private static bool IsUsableActorImage(string url)
        {
            return !string.IsNullOrEmpty(url)
                && !url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(url, JavDBActorUnknownImage, StringComparison.OrdinalIgnoreCase)
                && url.IndexOf("actor_unknow", StringComparison.OrdinalIgnoreCase) < 0;
        }

        /// <summary>
        /// javdb 演员头像。搜索页免登录即可返回演员卡片：
        /// <code>
        /// &lt;div class="box actor-box"&gt;
        /// &lt;a href="/actors/Av2e" title="三上悠亜, 三上悠亞, 鬼头桃菜"&gt;
        /// &lt;figure class="image"&gt;&lt;img class="avatar" src="https://c0.jdbstatic.com/avatars/av/Av2e.jpg" /&gt;&lt;/figure&gt;
        /// &lt;strong&gt;三上悠亜&lt;/strong&gt;
        /// &lt;/a&gt;
        /// &lt;/div&gt;
        /// </code>
        /// 取图顺序：登录态演员页主头像 → 卡片 img.avatar → CDN 规律兜底，
        /// 每一步都要求 HEAD 能拿到 200，因为演员页/头像 CDN 都可能是占位图或 403。
        /// </summary>
        private static async Task<string> GetFromJavDB(string name, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var cookies = ParseJavDBCookies(Plugin.Instance.Configuration.JavDBCookie);
            var headers = JavDBHeaders();
            var url = $"{JavDBBaseUrl}/search?q={Uri.EscapeDataString(name)}&f=actor";

            var searchData = await HTML.ElementFromURL(url, cancellationToken, headers, cookies).ConfigureAwait(false);

            string actorId = null,
                   cardAvatar = null;

            foreach (var card in searchData.SelectNodesSafe("//div[contains(@class, 'actor-box')]//a[@href]"))
            {
                var href = card.GetAttributeValue("href", string.Empty);
                if (href.IndexOf("/actors/", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (IsSameActorName(card.GetAttributeValue("title", string.Empty), name))
                {
                    actorId = href.Split('/').Last();
                    cardAvatar = card.SelectSingleText(".//img[contains(@class, 'avatar')]/@src");

                    break;
                }

                // 没有同名卡片时退化为第一张卡（站内搜索按相关度排序，第一张通常就是最接近的）。
                if (string.IsNullOrEmpty(actorId))
                {
                    actorId = href.Split('/').Last();
                    cardAvatar = card.SelectSingleText(".//img[contains(@class, 'avatar')]/@src");
                }
            }

            if (string.IsNullOrEmpty(actorId))
            {
                return null;
            }

            // 1) 登录态下演员详情页的大图（未登录时该页 302 到 /login，取不到任何 /avatars/ 图）。
            if (cookies != null)
            {
                var actorData = await HTML.ElementFromURL($"{JavDBBaseUrl}/actors/{actorId}", cancellationToken, headers, cookies).ConfigureAwait(false);

                if (IsJavDBLoginWall(actorData))
                {
                    // 登录需要图形验证码（rucaptcha）+ CSRF token，插件无法自动续期，只能提示用户换 Cookie。
                    Logger.Warning("[JavDB] The configured JavDB cookie no longer passes the login wall (expired or revoked); falling back to the public portrait sources.");
                }

                var pageAvatar = actorData.SelectSingleText("//div[contains(@class, 'actor-avatar')]//img/@src");
                if (IsUsableActorImage(pageAvatar) && await HTTP.IsUrlLoadable(pageAvatar, cancellationToken).ConfigureAwait(false))
                {
                    return pageAvatar;
                }

                // 布局改版时 actor-avatar 容器可能不在，退化为"页内第一张 /avatars/ 图"。
                foreach (var img in actorData.SelectNodesSafe("//img[@src]"))
                {
                    var src = img.GetAttributeValue("src", string.Empty);
                    if (IsUsableActorImage(src)
                        && src.IndexOf("/avatars/", StringComparison.OrdinalIgnoreCase) >= 0
                        && await HTTP.IsUrlLoadable(src, cancellationToken).ConfigureAwait(false))
                    {
                        return src;
                    }
                }
            }

            // 2) 搜索卡片里的头像。
            if (IsUsableActorImage(cardAvatar) && await HTTP.IsUrlLoadable(cardAvatar, cancellationToken).ConfigureAwait(false))
            {
                return cardAvatar;
            }

            // 3) CDN 规律兜底：Av2e → av/Av2e.jpg（存在 200 image/jpeg，不存在 403）。
            if (actorId.Length >= 2)
            {
                var derivedAvatar = $"https://c0.jdbstatic.com/avatars/{actorId.Substring(0, 2).ToLowerInvariant()}/{actorId}.jpg";
                if (await HTTP.IsUrlLoadable(derivedAvatar, cancellationToken).ConfigureAwait(false))
                {
                    return derivedAvatar;
                }
            }

            return null;
        }

        /// <summary>
        /// 最后兜底：演员没有头像时拿她的作品封面当头像。
        /// 只认 /v/{code} 作品卡，且要求 title 后缀精确等于演员名——
        /// f=all 搜索同一演员会带回很多同名/合演作品，只有"以她名字结尾"的那批标题才是她的封面，
        /// 用后缀判断能避免把别人的封面挂到她头上。
        /// </summary>
        private static async Task<string> GetFromJavDBCover(string name, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var cookies = ParseJavDBCookies(Plugin.Instance.Configuration.JavDBCookie);
            var url = $"{JavDBBaseUrl}/search?q={Uri.EscapeDataString(name)}&f=all";

            var searchData = await HTML.ElementFromURL(url, cancellationToken, JavDBHeaders(), cookies).ConfigureAwait(false);

            foreach (var card in searchData.SelectNodesSafe("//a[@href][contains(@class, 'box')]"))
            {
                var href = card.GetAttributeValue("href", string.Empty);
                if (!href.StartsWith("/v/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var title = card.GetAttributeValue("title", string.Empty).TrimEnd();
                if (!title.EndsWith(name.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var cover = card.SelectSingleText(".//img/@src");
                if (string.IsNullOrEmpty(cover) || cover.IndexOf("/covers/", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    // 卡片图规律：https://c0.jdbstatic.com/covers/{code 前两位小写}/{code}.jpg
                    // （懒加载占位图或布局变化时，用 URL 规律反向推导比读 img 更稳）。
                    var code = href.Split('/').Last();
                    if (code.Length >= 2)
                    {
                        cover = $"https://c0.jdbstatic.com/covers/{code.Substring(0, 2).ToLowerInvariant()}/{code}.jpg";
                    }
                }

                if (!string.IsNullOrEmpty(cover) && await HTTP.IsUrlLoadable(cover, cancellationToken).ConfigureAwait(false))
                {
                    return cover;
                }
            }

            return null;
        }

        /// <summary>
        /// gfriends：GitHub 上的演员头像仓库（Filetree.json 索引 + raw 图片）。
        /// 文件名即演员名，按目录顺序遍历后取最后一个命中（与上游一致）。
        /// </summary>
        private static async Task<string> GetFromGFriends(string name, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var tree = await GetGFriendsTree(cancellationToken).ConfigureAwait(false);
            var content = tree?["Content"];
            if (content == null)
            {
                return null;
            }

            string image = null;
            foreach (var folder in content.Children<JProperty>())
            {
                if (folder.Value is not JObject files)
                {
                    continue;
                }

                foreach (var file in files.Properties())
                {
                    if (string.Equals(Path.GetFileNameWithoutExtension(file.Name), name, StringComparison.OrdinalIgnoreCase))
                    {
                        image = $"https://raw.githubusercontent.com/gfriends/gfriends/master/Content/{folder.Name}/{file.Value}";
                    }
                }
            }

            return image;
        }

        private static async Task<JObject> GetGFriendsTree(CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            if (gFriendsTree != null && now - gFriendsTreeTime < GFriendsCacheTtl)
            {
                return gFriendsTree;
            }

            await GFriendsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                now = DateTime.UtcNow;
                if (gFriendsTree != null && now - gFriendsTreeTime < GFriendsCacheTtl)
                {
                    return gFriendsTree;
                }

                var http = await HTTP.Request("https://raw.githubusercontent.com/gfriends/gfriends/master/Filetree.json", cancellationToken).ConfigureAwait(false);
                if (http.IsOK)
                {
                    gFriendsTree = JObject.Parse(http.Content);
                    gFriendsTreeTime = DateTime.UtcNow;
                }
            }
            catch (Exception e)
            {
                Logger.Error($"Gfriends filetree error: \"{e.Message}\"");
            }
            finally
            {
                GFriendsLock.Release();
            }

            return gFriendsTree;
        }

        private static async Task<string> GetFromIAFD(string name, CancellationToken cancellationToken)
        {
            string image = null;

            if (string.IsNullOrEmpty(name))
            {
                return image;
            }

            string encodedName = HttpUtility.UrlEncode(name),
                   url = $"http://www.iafd.com/results.asp?searchtype=comprehensive&searchstring={encodedName}";

            var actorData = await HTML.ElementFromURL(url, cancellationToken).ConfigureAwait(false);

            var actorPageURL = actorData.SelectSingleText("//table[@id='tblFem']//tbody//a/@href");
            if (!string.IsNullOrEmpty(actorPageURL))
            {
                if (!actorPageURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    actorPageURL = "http://www.iafd.com" + actorPageURL;
                }

                var actorPage = await HTML.ElementFromURL(actorPageURL, cancellationToken).ConfigureAwait(false);

                var actorImage = actorPage.SelectSingleText("//div[@id='headshot']//img/@src");

                // 详情页没有 headshot 容器时 SelectSingleText 返回空串，直接 .Contains 会 NRE，
                // 进而让整个 actorImage 批次失败（现在还会被 finally 的 TaskStatus 判断挡掉，但仍要修）。
                if (!string.IsNullOrEmpty(actorImage) && !actorImage.Contains("nophoto", StringComparison.OrdinalIgnoreCase))
                {
                    image = actorImage;
                }
            }

            return image;
        }
    }
}
