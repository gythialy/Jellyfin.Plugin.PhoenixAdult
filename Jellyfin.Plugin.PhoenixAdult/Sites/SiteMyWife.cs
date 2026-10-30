using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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
    /// セレブクラブ舞ワイフ (mywife.cc)。条目 ID 是纯数字的作品号（如 2320），
    /// 详情页 https://mywife.cc/teigaku/model/no/{id}，页面很轻、无站内搜索，
    /// 只能从标题里提取数字后直连详情。
    /// </summary>
    public class SiteMyWife : IProviderBase
    {
        private const string MoviePath = "/teigaku/model/no/";

        private static readonly Regex MyWifeIdRegex = new Regex(
            @"(?i)(?:mywife|舞ワイフ)[\s\-_]*(?<id>[0-9]{3,5})(?![0-9])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex NumberTokenRegex = new Regex(
            @"(?<![0-9])(?<id>[0-9]{3,5})(?![0-9])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex PathIdRegex = new Regex(
            @"(?:/no/|/)(?<id>[0-9]{3,5})/?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrWhiteSpace(searchTitle))
            {
                return result;
            }

            var movieId = ResolveMovieId(searchTitle);
            if (string.IsNullOrEmpty(movieId))
            {
                return result;
            }

            try
            {
                var searchBaseURL = Helper.GetSearchBaseURL(siteNum).TrimEnd('/');
                var sceneURL = $"{searchBaseURL}{MoviePath}{movieId}";
                var direct = await Helper.GetSearchResultsFromUpdate(this, siteNum, new[] { Helper.Encode(sceneURL) }, searchDate, cancellationToken).ConfigureAwait(false);
                if (direct.Any())
                {
                    result.AddRange(direct);
                }
            }
            catch (Exception e)
            {
                Logger.Error($"[MyWife] Search failed for '{searchTitle}': {e.Message}");
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

            var sceneURL = Helper.Decode(sceneID[0]);
            if (string.IsNullOrWhiteSpace(sceneURL))
            {
                return result;
            }

            var baseURL = Helper.GetSearchBaseURL(siteNum).TrimEnd('/');
            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = sceneURL.StartsWith("/", StringComparison.Ordinal)
                    ? baseURL + sceneURL
                    : $"{baseURL}/{sceneURL}";
            }

            var sceneData = await Fetcher.Html(sceneURL, cancellationToken).ConfigureAwait(false);
            if (sceneData == null)
            {
                return result;
            }

            var name = sceneData.SelectSingleText("//title");
            if (string.IsNullOrWhiteSpace(name))
            {
                return result;
            }

            // 页面标题形如 "No.1689　真中 えりか|セレブクラブ舞ワイフ"，去掉站点后缀。
            name = Regex.Replace(name, @"\s*[|｜].*$", string.Empty, RegexOptions.CultureInvariant).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return result;
            }

            result.Item.ExternalId = sceneURL;
            result.Item.Name = name;
            result.Item.AddStudio("舞ワイフ");

            var movieId = ParseIdFromURL(sceneURL);
            if (!string.IsNullOrEmpty(movieId))
            {
                result.Item.OriginalTitle = $"MYWIFE-{movieId}";
            }

            var overview = sceneData.SelectSingleText("//div[contains(@class, 'modelsamplephototop')]//span[contains(@class, 'text_overflow')]");
            if (!string.IsNullOrEmpty(overview))
            {
                result.Item.Overview = overview.Trim();
            }

            var cover = NormalizeUrl(sceneData.SelectSingleText("//video[@id='video']/@poster"), baseURL);
            if (!string.IsNullOrEmpty(cover))
            {
                result.Item.SetImage(new ItemImageInfo { Path = cover, Type = ImageType.Primary }, 0);
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

            var sceneURL = Helper.Decode(sceneID[0]);
            if (string.IsNullOrWhiteSpace(sceneURL))
            {
                return result;
            }

            var baseURL = Helper.GetSearchBaseURL(siteNum).TrimEnd('/');
            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = sceneURL.StartsWith("/", StringComparison.Ordinal)
                    ? baseURL + sceneURL
                    : $"{baseURL}/{sceneURL}";
            }

            HtmlAgilityPack.HtmlNode sceneData = null;
            try
            {
                sceneData = await Fetcher.Html(sceneURL, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Logger.Error($"[MyWife] GetImages failed for '{sceneURL}': {e.Message}");
            }

            if (sceneData == null)
            {
                return result;
            }

            var cover = NormalizeUrl(sceneData.SelectSingleText("//video[@id='video']/@poster"), baseURL);
            if (string.IsNullOrEmpty(cover))
            {
                // 兜底：头像缩略图（页面顶部 /common/thumb/02320/05/1.png）。
                cover = NormalizeUrl(sceneData.SelectSingleText("//div[contains(@class, 'modelwaku0')]/img/@src"), baseURL);
            }

            if (!string.IsNullOrEmpty(cover))
            {
                result.Add(new RemoteImageInfo { Type = ImageType.Primary, Url = cover });
                result.Add(new RemoteImageInfo { Type = ImageType.Backdrop, Url = cover });
            }

            foreach (var imageNode in sceneData.SelectNodesSafe("//div[contains(@class, 'modelsamplephoto')]//div[contains(@class, 'modelsample_photowaku')]/img").Take(16))
            {
                var imageURL = NormalizeUrl(imageNode.GetAttributeValue("src", string.Empty), baseURL);
                if (!string.IsNullOrEmpty(imageURL))
                {
                    result.Add(new RemoteImageInfo { Type = ImageType.Backdrop, Url = imageURL });
                }
            }

            return result;
        }

        private static string ResolveMovieId(string searchTitle)
        {
            if (string.IsNullOrWhiteSpace(searchTitle))
            {
                return string.Empty;
            }

            var match = MyWifeIdRegex.Match(searchTitle);
            if (match.Success)
            {
                return match.Groups["id"].Value;
            }

            foreach (Match token in NumberTokenRegex.Matches(searchTitle))
            {
                var value = token.Groups["id"].Value;
                if (int.TryParse(value, out var numericValue) && numericValue >= 1900 && numericValue <= 2099)
                {
                    // 年份，不是作品号。
                    continue;
                }

                return value;
            }

            return string.Empty;
        }

        private static string ParseIdFromURL(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            var segments = url.Split(new[] { '/', '?' }, StringSplitOptions.RemoveEmptyEntries);
            var candidate = segments.LastOrDefault();
            return candidate != null && Regex.IsMatch(candidate, "^[0-9]{1,6}$") ? candidate : string.Empty;
        }

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

            return url.StartsWith("/", StringComparison.Ordinal)
                ? baseURL + url
                : $"{baseURL}/{url}";
        }
    }
}
