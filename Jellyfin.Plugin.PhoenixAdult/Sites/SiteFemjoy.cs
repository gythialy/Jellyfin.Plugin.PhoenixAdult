using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Newtonsoft.Json.Linq;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    public class SiteFemjoy : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            // Femjoy 没有站内搜索 API（旧 /api/v2/search/videos 已死）。
            // 用 FlareSolverr 分页浏览 /videos，按标题/演员名过滤。
            var searchTerms = searchTitle.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var needTerms = searchTerms.Where(t => t.Length >= 3).Select(t => t.ToLowerInvariant()).ToArray();
            if (needTerms.Length == 0)
            {
                needTerms = searchTerms.Select(t => t.ToLowerInvariant()).ToArray();
            }

            for (var page = 1; page <= 3; page++)
            {
                string pageUrl = page == 1
                    ? "https://www.femjoy.com/videos"
                    : $"https://www.femjoy.com/videos?page={page}";

                string html;
                if (FlareSolverr.IsConfigured)
                {
                    html = await FlareSolverr.GetHtml(pageUrl, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var httpResult = await HTTP.Request(pageUrl, HttpMethod.Get, cancellationToken);
                    if (!httpResult.IsOK)
                    {
                        continue;
                    }

                    html = httpResult.Content;
                }

                if (string.IsNullOrEmpty(html))
                {
                    continue;
                }

                var items = Regex.Matches(html, @"<div class=""_results_item[^""]*""(.*?)<div class=""_results_item", RegexOptions.Singleline);
                foreach (Match itemMatch in items)
                {
                    var itemHtml = itemMatch.Groups[1].Value;
                    var idMatch = Regex.Match(itemHtml, @"data-post-id=""(\d+)""");
                    var titleMatch = Regex.Match(itemHtml, @"<h1><a[^>]*title=""([^""]*)""");
                    var actorMatch = Regex.Match(itemHtml, @"<h2><a[^>]*title=""([^""]*)""");
                    var dateMatch = Regex.Match(itemHtml, @"posted_on[^>]*>([^<]+)<");

                    if (!idMatch.Success || !titleMatch.Success)
                    {
                        continue;
                    }

                    var title = System.Net.WebUtility.HtmlDecode(titleMatch.Groups[1].Value).Trim();
                    var actor = System.Net.WebUtility.HtmlDecode(actorMatch.Groups[1].Value).Trim();
                    var haystack = $"{title} {actor}".ToLowerInvariant();
                    if (needTerms.Any() && !needTerms.All(haystack.Contains))
                    {
                        continue;
                    }

                    var sceneId = idMatch.Groups[1].Value;
                    var sceneUrl = $"https://www.femjoy.com/post/{sceneId}";
                    string curId = Helper.Encode(sceneUrl);

                    var releaseDate = string.Empty;
                    if (dateMatch.Success)
                    {
                        if (DateTime.TryParse(dateMatch.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                        {
                            releaseDate = parsedDate.ToString("yyyy-MM-dd");
                        }
                    }

                    var imageMatch = Regex.Match(itemHtml, @"item_cover[^>]*src=""([^""]*)""");
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curId } },
                        Name = $"{title} - {actor} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}".Trim(),
                        ImageUrl = imageMatch.Success ? imageMatch.Groups[1].Value : string.Empty,
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }
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

            if (sceneID == null)
            {
                return result;
            }

            var sceneUrl = Helper.Decode(sceneID[0]);
            if (!sceneUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneUrl = "https://www.femjoy.com" + sceneUrl;
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var html = httpResult.Content;
            result.Item.ExternalId = sceneUrl;
            result.HasMetadata = true;

            // 场景页 h1: <a title="演员">演员</a><small>in</small><span>标题</span>
            var titleMatch = Regex.Match(html, @"<h1[^>]*>(.*?)</h1>", RegexOptions.Singleline);
            if (titleMatch.Success)
            {
                var spanMatch = Regex.Match(titleMatch.Groups[1].Value, @"<span[^>]*>(.*?)</span>", RegexOptions.Singleline);
                if (spanMatch.Success)
                {
                    result.Item.Name = System.Net.WebUtility.HtmlDecode(Regex.Replace(spanMatch.Groups[1].Value, @"<.*?>", string.Empty)).Trim();
                }
            }

            if (string.IsNullOrEmpty(result.Item.Name))
            {
                var t = Regex.Match(html, @"<title>([^<]*)</title>");
                if (t.Success)
                {
                    var parts = t.Groups[1].Value.Split('-');
                    result.Item.Name = parts.Length >= 2 ? parts[1].Trim() : t.Groups[1].Value.Trim();
                }
            }

            var dateMatch = Regex.Match(html, @"posted_on"">([^<]+)<");
            if (dateMatch.Success && DateTime.TryParse(dateMatch.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                result.Item.PremiereDate = parsedDate;
                result.Item.ProductionYear = parsedDate.Year;
            }

            // 描述: post_text / description 区块
            var descMatch = Regex.Match(html, @"class=""[^""]*(?:post_text|description|entry-content)[^""]*"">(.*?)</div>", RegexOptions.Singleline);
            if (descMatch.Success)
            {
                result.Item.Overview = System.Net.WebUtility.HtmlDecode(Regex.Replace(descMatch.Groups[1].Value, @"<.*?>", string.Empty)).Trim();
            }

            // 演员: h2 里 by 前面的链接
            var actorMatches = Regex.Matches(html, @"<h2><a[^>]*title=""([^""]*)""[^>]*>([^<]*)</a>");
            foreach (Match actorMatch in actorMatches)
            {
                var actorName = System.Net.WebUtility.HtmlDecode(actorMatch.Groups[1].Value).Trim();
                if (string.IsNullOrEmpty(actorName) || actorName.Equals("by", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.AddPerson(new PersonInfo
                {
                    Name = actorName,
                    Type = PersonKind.Actor,
                });
            }

            result.Item.AddStudio("Femjoy");
            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();

            if (sceneID == null)
            {
                return images;
            }

            var sceneUrl = Helper.Decode(sceneID[0]);
            if (!sceneUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneUrl = "https://www.femjoy.com" + sceneUrl;
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var html = httpResult.Content;
            var coverMatches = Regex.Matches(html, @"item_cover[^>]*src=""([^""]*)""");
            var seen = new HashSet<string>();
            foreach (Match m in coverMatches)
            {
                var url = m.Groups[1].Value;
                if (!seen.Add(url))
                {
                    continue;
                }

                images.Add(new RemoteImageInfo
                {
                    Url = url,
                    Type = images.Count == 0 ? ImageType.Primary : ImageType.Backdrop,
                });
            }

            return images;
        }
    }
}
