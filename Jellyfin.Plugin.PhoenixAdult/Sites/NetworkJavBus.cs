using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
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
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    public class NetworkJavBus : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null)
            {
                return result;
            }

            string searchJAVID = null;
            string directJAVID = null;
            if (JavId.TryParse(searchTitle, out var javId))
            {
                var javIdParts = JavId.Split(javId);
                directJAVID = javId;
                if (javIdParts.Length == 2)
                {
                    searchJAVID = $"{javIdParts[0]}%2B{javIdParts[1]}";
                }
            }

            string encodedSearchTitle = searchJAVID ?? Uri.EscapeDataString(searchTitle);

            foreach (var searchType in new[] { "Censored", "Uncensored" })
            {
                string url = (searchType == "Uncensored")
                    ? $"{Helper.GetSearchSearchURL(siteNum)}uncensored/search/{encodedSearchTitle}"
                    : $"{Helper.GetSearchSearchURL(siteNum)}search/{encodedSearchTitle}";

                var searchResultsNode = await HTML.ElementFromURL(url, cancellationToken, null, this.GetCookies());
                if (searchResultsNode == null)
                {
                    continue;
                }

                var searchResults = searchResultsNode.SelectNodes("//a[@class='movie-box']");
                if (searchResults == null)
                {
                    continue;
                }

                foreach (var searchResult in searchResults)
                {
                    string titleNoFormatting = ParseSearchTitle(searchResult);
                    string javid = searchResult.SelectSingleNode(".//date[1]")?.InnerText.Trim();
                    string sceneURL = searchResult.GetAttributeValue("href", string.Empty);
                    string curID = Helper.Encode(sceneURL);

                    // 列表卡片里 <date>[1] 是番号、<date>[2] 是发行日期：
                    // 填进 PremiereDate 才能让识别对话框按日期排序/显示，而不是只把日期混在标题串里。
                    var searchDateValue = searchResult.SelectSingleNode(".//date[2]")?.InnerText.Trim();

                    // 站点名由 MovieProvider 统一加来源前缀（[JavBus]），这里只保留有码/无码与番号
                    var searchResultItem = new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = $"[{searchType}] {javid} {titleNoFormatting}",
                        SearchProviderName = Plugin.Instance.Name,
                    };

                    if (!string.IsNullOrEmpty(searchDateValue)
                        && DateTime.TryParseExact(searchDateValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var searchDateObj))
                    {
                        searchResultItem.PremiereDate = searchDateObj;
                        searchResultItem.ProductionYear = searchDateObj.Year;
                    }

                    // 卡片缩略图同样要 Referer，浏览器直连会 403：填对话框缩略图时必须换成同源代理地址
                    var thumbSrc = searchResult.SelectSingleNode(".//div[contains(@class, 'photo-frame')]//img")?.GetAttributeValue("src", string.Empty)
                        ?? searchResult.SelectSingleNode(".//img")?.GetAttributeValue("src", string.Empty);
                    if (!string.IsNullOrEmpty(thumbSrc))
                    {
                        if (thumbSrc.StartsWith("//", StringComparison.Ordinal))
                        {
                            thumbSrc = "https:" + thumbSrc;
                        }
                        else if (!thumbSrc.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            thumbSrc = Helper.GetSearchBaseURL(siteNum) + thumbSrc;
                        }

                        searchResultItem.ImageUrl = ImageProxyUrl.ForBrowser(thumbSrc);
                    }

                    result.Add(searchResultItem);
                }
            }

            // 搜索页偶尔漏收录详情页仍可访问的番号（metatube #382 的同类修复）：
            // 只在两个搜索方向都 0 结果时才直接试详情页，且必须确认详情页真的是作品页
            // （有 col-md-3 info / bigImage 面板）——否则 404 页的 <title> 会被当成结果，
            // 变成"[Direct][XXX] 404 Page Not Found!"这种幽灵条目。
            if (!string.IsNullOrEmpty(directJAVID) && result.Count == 0)
            {
                string sceneURL = Helper.GetSearchSearchURL(siteNum) + directJAVID;
                var searchResultNode = await HTML.ElementFromURL(sceneURL, cancellationToken, null, this.GetCookies());
                if (searchResultNode != null
                    && (searchResultNode.SelectSingleNode("//div[@class='col-md-3 info']") != null
                        || searchResultNode.SelectSingleNode("//a[@class='bigImage']") != null))
                {
                    string javTitle = searchResultNode.SelectSingleNode("//head/title")?.InnerText.Trim().Replace(" - JavBus", string.Empty);
                    if (!string.IsNullOrEmpty(javTitle))
                    {
                        string curID = Helper.Encode(sceneURL);
                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, curID } },
                            Name = $"[Direct][{directJAVID}] {javTitle}",
                            SearchProviderName = Plugin.Instance.Name,
                        });
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 从搜索结果卡片里取干净的标题。
        /// 卡片的 &lt;span&gt; 里除了标题还塞了 &lt;button&gt;（"高清"等）、&lt;br/&gt; 和两个 &lt;date&gt;
        /// （番号、发行日期）——直接取 InnerText 会把番号和日期重复带进标题，识别对话框因此出现多个时间标记。
        /// </summary>
        /// <param name="searchResult">卡片节点。</param>
        /// <returns>压成单空格的标题。</returns>
        internal static string ParseSearchTitle(HtmlNode searchResult)
        {
            var span = searchResult?.SelectSingleNode(".//span[1]");
            if (span == null)
            {
                return string.Empty;
            }

            var clone = span.CloneNode(true);
            foreach (var node in clone.SelectNodesSafe(".//date|.//button|.//br|.//script"))
            {
                node.Remove();
            }

            var text = Regex.Replace(clone.InnerText.Replace("\t", " ").Replace("\r", " ").Replace("\n", " "), @"\s{2,}", " ").Trim();

            // 两个 <date> 之间那个孤零零的 "/" 会留在末尾（"标题 /"），去掉
            return text.TrimEnd(' ', '/', '\u00a0');
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem>()
            {
                Item = new Movie(),
                People = new List<PersonInfo>(),
            };

            string sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken, null, this.GetCookies());
            if (detailsPageElements == null)
            {
                return result;
            }

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneURL;
            string javStudio = detailsPageElements.SelectSingleNode("//p/a[contains(@href, '/studio/')]")?.InnerText.Trim();
            string javTitle = detailsPageElements.SelectSingleNode("//head/title")?.InnerText.Trim().Replace(" - JavBus", string.Empty);
            movie.Name = javTitle;
            movie.AddStudio(javStudio);

            string label = detailsPageElements.SelectSingleNode("//p/a[contains(@href, '/label/')]")?.InnerText.Trim();
            string series = detailsPageElements.SelectSingleNode("//p/a[contains(@href, '/series/')]")?.InnerText.Trim();
            if (!string.IsNullOrEmpty(label))
            {
                movie.AddStudio(label);
            }
            else
            {
                movie.AddStudio(javStudio);
            }

            if (!string.IsNullOrEmpty(series))
            {
                movie.AddStudio(series);
            }

            var dateNode = detailsPageElements.SelectSingleNode("//div[@class='col-md-3 info']/p[2]");
            if (dateNode != null)
            {
                // 站点按请求语种本地化标签：英文页是 "Release Date: 2015-08-16"，
                // 中文页是 "發行日期: 2015-08-16"（插件只带 UA、不带 Accept-Language，实际拿到中文）。
                // 直接正则取日期本体，避免为每种语言维护一份去标签逻辑。
                var dateMatch = Regex.Match(dateNode.InnerText, @"[0-9]{4}-[0-9]{2}-[0-9]{2}");
                if (dateMatch.Success
                    && dateMatch.Value != "0000-00-00"
                    && DateTime.TryParseExact(dateMatch.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                {
                    movie.PremiereDate = parsedDate;
                    movie.ProductionYear = parsedDate.Year;
                }
            }

            var genreLinks = detailsPageElements.SelectNodes("//span[@class='genre']//a[contains(@href, '/genre/')]");
            if (genreLinks != null)
            {
                foreach (var genre in genreLinks)
                {
                    movie.AddGenre(genre.InnerText.ToLower().Trim());
                }
            }

            var actorLinks = detailsPageElements.SelectNodes("//a[@class='avatar-box']");
            if (actorLinks != null)
            {
                foreach (var actorLink in actorLinks)
                {
                    string actorName = actorLink.SelectSingleNode("./div/img")?.GetAttributeValue("title", string.Empty);
                    string actorPhotoURL = actorLink.SelectSingleNode("./div[@class='photo-frame']/img")?.GetAttributeValue("src", string.Empty);
                    if (!string.IsNullOrEmpty(actorPhotoURL) && !actorPhotoURL.StartsWith("http"))
                    {
                        actorPhotoURL = Helper.GetSearchBaseURL(siteNum) + actorPhotoURL;
                    }

                    if (actorPhotoURL?.EndsWith("nowprinting.gif") == true)
                    {
                        actorPhotoURL = string.Empty;
                    }

                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoURL });
                }
            }

            var directorLink = detailsPageElements.SelectSingleNode("//p/a[contains(@href, '/director/')]");
            if (directorLink != null)
            {
                ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = directorLink.InnerText.Trim(), Type = PersonKind.Director });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken, null, this.GetCookies());
            if (detailsPageElements == null)
            {
                return images;
            }

            // 只有 sample-box 才是真正的截图；/cover/ 是封面本身（无码页只有封面），
            // 再把它当 Backdrop 会与 Primary 撞 URL，被 Jellyfin 按 URL 去重后横幅就消失了。
            var posterNodes = detailsPageElements.SelectNodes("//a[@class='sample-box' or contains(@href, '/sample/')]");
            if (posterNodes != null)
            {
                foreach (var poster in posterNodes)
                {
                    string posterUrl = poster.GetAttributeValue("href", string.Empty);
                    if (!posterUrl.StartsWith("http"))
                    {
                        posterUrl = Helper.GetSearchBaseURL(siteNum) + posterUrl;
                    }

                    images.Add(new RemoteImageInfo { Url = posterUrl, Type = ImageType.Backdrop });
                }
            }

            var coverImageNode = detailsPageElements.SelectSingleNode("//a[contains(@href, '/cover/')]");
            if (coverImageNode != null)
            {
                string coverImageUrl = coverImageNode.GetAttributeValue("href", string.Empty);

                // 无码（/imgs/cover/*_b.jpg）页面没有对应的缩略图目录，推导出来的
                // /imgs/thumb/<code>.jpg 实测 404，直接用封面本身当 Primary。
                // 有码页面（/pics/cover/*_b.jpg）推导出的 /pics/thumb/<code>.jpg 实测 200。
                string primaryUrl = coverImageUrl.StartsWith("/imgs/", StringComparison.OrdinalIgnoreCase)
                    ? coverImageUrl
                    : BuildCoverThumbUrl(coverImageUrl);

                if (string.IsNullOrEmpty(primaryUrl))
                {
                    return images;
                }

                if (!primaryUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    primaryUrl = Helper.GetSearchBaseURL(siteNum) + primaryUrl;
                }

                images.Insert(0, new RemoteImageInfo { Url = primaryUrl, Type = ImageType.Primary });
            }

            return images;
        }

        /// <summary>
        /// 由封面原图派生缩略图：/pics/cover/8tdj_b.jpg → /pics/thumb/8tdj.jpg（实测 200）。
        /// 取不到可用路径时返回 null，由调用方决定是否退回原图。
        /// </summary>
        private static string BuildCoverThumbUrl(string coverImageUrl)
        {
            if (string.IsNullOrEmpty(coverImageUrl))
            {
                return null;
            }

            var parts = coverImageUrl.Split('/');
            if (parts.Length < 3)
            {
                return null;
            }

            var coverImageCode = parts[parts.Length - 1].Split('.')[0].Split('_')[0];
            if (string.IsNullOrEmpty(coverImageCode))
            {
                return null;
            }

            var imageHost = string.Join("/", parts.Take(parts.Length - 2));
            var thumbUrl = $"{imageHost}/thumb/{coverImageCode}.jpg";
            if (thumbUrl.Contains("/images."))
            {
                thumbUrl = thumbUrl.Replace("thumb", "thumbs");
            }

            return thumbUrl;
        }

        private IDictionary<string, string> GetCookies()
        {
            // age=verified：JavBus 新版年龄验证，缺失时搜索页只返回年龄门（0 结果）
            return new Dictionary<string, string> { { "existmag", "all" }, { "dv", "1" }, { "age", "verified" } };
        }
    }
}
