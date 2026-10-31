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
        /// JavDB placeholder portrait: answers 200 like a real one, so exclude it by URL.
        /// </summary>
        private const string JavDBActorUnknownImage = "https://c0.jdbstatic.com/images/actor_unknow.jpg";

        /// <summary>
        /// Cover fallback label; also marks that the fallback was already tried.
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
                    // Read by task status so one failing source does not discard the others.
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

                // Cover fallback: only when every source is empty and the setting is on.
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

            // An empty list or the cover label both mean the fallback was already tried.
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

            // Referer protected URLs answer 403 to the downloader, so route them through the proxy.
            images = ImageProxyUrl.WrapForDownload(images);

            // Retry the cover fallback when validation dropped everything.
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
        /// ADVE age gate: obtain the ageConfirmed cookie once and pass it to later requests.
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

            // Current skin: infobox plainlinks with the image inside a mw-file-description link; the old selector is the fallback.
            var img = actorData.SelectSingleText("//table[contains(@class, 'infobox')]//img[contains(@class, 'mw-file-element')]/@src");
            if (string.IsNullOrEmpty(img))
            {
                img = actorData.SelectSingleText("//table[@class='infobox']//a[@class='image']//img/@src");
            }

            if (!string.IsNullOrEmpty(img) && !img.Contains("NoImage", StringComparison.OrdinalIgnoreCase))
            {
                if (!img.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    // The site redirects to https (301); adding it saves a round trip.
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
        /// Whether the page is the login wall (302 to /login); only the title is inspected.
        /// </summary>
        private static bool IsJavDBLoginWall(HtmlNode page)
        {
            var title = page?.SelectSingleNode("//head/title")?.InnerText ?? string.Empty;
            return title.IndexOf("登入", StringComparison.Ordinal) >= 0
                || title.IndexOf("登录", StringComparison.Ordinal) >= 0
                || title.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Parse the cookie setting; null when empty. Only actor pages need the session.
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
        /// The card title holds comma separated aliases; only an exact alias match counts.
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
        /// Filter the placeholder portrait, which answers 200 for every actress.
        /// </summary>
        private static bool IsUsableActorImage(string url)
        {
            return !string.IsNullOrEmpty(url)
                && !url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(url, JavDBActorUnknownImage, StringComparison.OrdinalIgnoreCase)
                && url.IndexOf("actor_unknow", StringComparison.OrdinalIgnoreCase) < 0;
        }

        /// <summary>
        /// JavDB portrait source: search cards work without a session.
        /// &lt;/a&gt;
        /// &lt;/div&gt;
        /// </code>
        /// Order: actor page avatar, card avatar, CDN naming rule; every step needs a 200.
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

                // No exact card: use the first one (relevance ordered).
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

            // 1) Large portrait from the actor page when signed in.
            if (cookies != null)
            {
                var actorData = await HTML.ElementFromURL($"{JavDBBaseUrl}/actors/{actorId}", cancellationToken, headers, cookies).ConfigureAwait(false);

                if (IsJavDBLoginWall(actorData))
                {
                    // Signing in needs a captcha, so only ask for a fresh cookie.
                    Logger.Warning("[JavDB] The configured JavDB cookie no longer passes the login wall (expired or revoked); falling back to the public portrait sources.");
                }

                var pageAvatar = actorData.SelectSingleText("//div[contains(@class, 'actor-avatar')]//img/@src");
                if (IsUsableActorImage(pageAvatar) && await HTTP.IsUrlLoadable(pageAvatar, cancellationToken).ConfigureAwait(false))
                {
                    return pageAvatar;
                }

                // Layout change may drop the container; use the first /avatars/ image.
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

            // 2) Portrait from the search card.
            if (IsUsableActorImage(cardAvatar) && await HTTP.IsUrlLoadable(cardAvatar, cancellationToken).ConfigureAwait(false))
            {
                return cardAvatar;
            }

            // 3) CDN naming rule: <id> -> av/<id>.jpg.
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
        /// Last resort: the cover of one of her titles, accepted only when the card title ends with her name.
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
                    // Card image rule: covers/<first two code chars, lower case>/<code>.jpg.
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
        /// gfriends: portrait repository on GitHub; the file name is the actress name.
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

                // A missing headshot container returned an empty string and threw.
                if (!string.IsNullOrEmpty(actorImage) && !actorImage.Contains("nophoto", StringComparison.OrdinalIgnoreCase))
                {
                    image = actorImage;
                }
            }

            return image;
        }
    }
}
