using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
#if !__EMBY__
using Jellyfin.Data.Enums;
#endif
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
    public class SiteWatch4Beauty : IProviderBase
    {
        private const string BaseUrl = "https://www.watch4beauty.com";

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            // 1. 搜索模型: /search?q= 返回内嵌 JSON models.byId（全量模型，需按搜索词过滤）
            var searchUrl = $"{BaseUrl}/search?q={Uri.EscapeDataString(searchTitle)}";
            var searchHttp = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (!searchHttp.IsOK)
            {
                return result;
            }

            var modelIds = ExtractModelIds(searchHttp.Content, searchTitle);
            if (modelIds.Count == 0)
            {
                return result;
            }

            // 2. 对每个模型拉 issues（场景视频）
            var seen = new HashSet<string>();
            foreach (var modelId in modelIds.Take(5))
            {
                var apiUrl = $"{BaseUrl}/api/issues?model_id={modelId}";
                var apiHttp = await HTTP.Request(apiUrl, HttpMethod.Get, cancellationToken);
                if (!apiHttp.IsOK)
                {
                    continue;
                }

                try
                {
                    var issues = JArray.Parse(apiHttp.Content);
                    foreach (var issue in issues)
                    {
                        var title = issue["issue_title"]?.ToString();
                        if (string.IsNullOrEmpty(title))
                        {
                            continue;
                        }

                        var issueId = issue["issue_id"]?.ToString();
                        if (string.IsNullOrEmpty(issueId) || !seen.Add(issueId))
                        {
                            continue;
                        }

                        // 模型已按演员名过滤，场景全部返回（场景标题通常不含演员名）
                        var releaseDate = string.Empty;
                        if (DateTime.TryParse(issue["issue_datetime"]?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                        {
                            releaseDate = parsedDate.ToString("yyyy-MM-dd");
                        }

                        // curID = issue_id，Update 时通过 API 反查
                        var curID = Helper.Encode(issueId);

                        var res = new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, curID } },
                            Name = $"{title} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}".Trim(),
                            PremiereDate = parsedDate,
                            SearchProviderName = Plugin.Instance.Name,
                        };

                        var cover = BuildCoverUrl(issue);
                        if (!string.IsNullOrEmpty(cover))
                        {
                            res.ImageUrl = cover;
                        }

                        result.Add(res);
                    }
                }
                catch (Exception)
                {
                    // 单模型失败不影响其他
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

            if (sceneID == null || sceneID.Length == 0)
            {
                return result;
            }

            var issueId = Helper.Decode(sceneID[0]);
            if (string.IsNullOrEmpty(issueId))
            {
                return result;
            }

            var apiUrl = $"{BaseUrl}/api/issues?issue_id={issueId}";
            var apiHttp = await HTTP.Request(apiUrl, HttpMethod.Get, cancellationToken);
            if (!apiHttp.IsOK)
            {
                return result;
            }

            try
            {
                var issues = JArray.Parse(apiHttp.Content);
                var issue = issues.FirstOrDefault(i => i["issue_id"]?.ToString() == issueId)
                            ?? (issues.Count > 0 ? issues[0] : null);
                if (issue == null)
                {
                    return result;
                }

                var movie = (Movie)result.Item;
                result.HasMetadata = true;
                movie.ExternalId = $"{BaseUrl}/videos/{issue["issue_simple_title"]}";

                movie.Name = issue["issue_title"]?.ToString();
                movie.Overview = Regex.Replace(issue["issue_text"]?.ToString() ?? string.Empty, @"<.*?>", string.Empty).Trim();
                movie.AddStudio("Watch4Beauty");

                if (DateTime.TryParse(issue["issue_datetime"]?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                {
                    movie.PremiereDate = parsedDate;
                    movie.ProductionYear = parsedDate.Year;
                }

                var tags = issue["issue_tags"]?.ToString();
                if (!string.IsNullOrEmpty(tags))
                {
                    foreach (var tag in tags.Split(','))
                    {
                        var t = tag.Trim();
                        if (!string.IsNullOrEmpty(t))
                        {
                            movie.AddGenre(t);
                        }
                    }
                }

                // 演员: 从 issue 的 models/actors 字段
                var actorNames = new List<string>();
                if (issue["models"] is JArray actorArray)
                {
                    foreach (var a in actorArray)
                    {
                        var name = a["model_nickname"]?.ToString() ?? a["name"]?.ToString();
                        if (!string.IsNullOrEmpty(name))
                        {
                            actorNames.Add(name);
                        }
                    }
                }

                if (actorNames.Count == 0 && issue["model_nickname"] != null)
                {
                    actorNames.Add(issue["model_nickname"].ToString());
                }

                foreach (var actorName in actorNames)
                {
                    result.AddPerson(new PersonInfo
                    {
                        Name = actorName,
                        Type = PersonKind.Actor,
                    });
                }
            }
            catch (Exception)
            {
                return result;
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            if (sceneID == null || sceneID.Length == 0)
            {
                return images;
            }

            var issueId = Helper.Decode(sceneID[0]);
            if (string.IsNullOrEmpty(issueId))
            {
                return images;
            }

            var apiUrl = $"{BaseUrl}/api/issues?issue_id={issueId}";
            var apiHttp = await HTTP.Request(apiUrl, HttpMethod.Get, cancellationToken);
            if (!apiHttp.IsOK)
            {
                return images;
            }

            try
            {
                var issues = JArray.Parse(apiHttp.Content);
                var issue = issues.FirstOrDefault(i => i["issue_id"]?.ToString() == issueId)
                            ?? (issues.Count > 0 ? issues[0] : null);
                if (issue == null)
                {
                    return images;
                }

                var cover = BuildCoverUrl(issue);
                if (!string.IsNullOrEmpty(cover))
                {
                    images.Add(new RemoteImageInfo
                    {
                        Url = cover,
                        Type = ImageType.Primary,
                    });
                }
            }
            catch (Exception)
            {
            }

            return images;
        }

        private static List<string> ExtractModelIds(string html, string searchTitle)
        {
            var result = new List<string>();
            var byIdPos = html.IndexOf("\"byId\"", StringComparison.Ordinal);
            if (byIdPos < 0)
            {
                return result;
            }

            var start = html.IndexOf('{', byIdPos);
            if (start < 0)
            {
                return result;
            }

            var depth = 0;
            var end = start;
            for (var i = start; i < html.Length; i++)
            {
                if (html[i] == '{')
                {
                    depth++;
                }
                else if (html[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        end = i + 1;
                        break;
                    }
                }
            }

            try
            {
                var byId = JObject.Parse(html.Substring(start, end - start));
                var terms = searchTitle.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(t => t.Length >= 3)
                    .Select(t => t.ToLowerInvariant())
                    .ToArray();

                foreach (var prop in byId.Properties())
                {
                    var nickname = prop.Value["model_nickname"]?.ToString() ?? string.Empty;
                    var simple = prop.Value["model_simple_nickname"]?.ToString() ?? string.Empty;
                    var haystack = $"{nickname} {simple}".ToLowerInvariant();
                    if (terms.Any() && !terms.All(haystack.Contains))
                    {
                        continue;
                    }

                    result.Add(prop.Name);
                }
            }
            catch (Exception)
            {
            }

            return result;
        }

        private static string BuildCoverUrl(JToken issue)
        {
            var prefix = issue["prefix"]?.ToString();
            if (string.IsNullOrEmpty(prefix))
            {
                return string.Empty;
            }

            // 封面 URL 模式: /api/covers/{prefix}/000-cover-issue-text_320.jpg
            // （302 重定向到 covers.watch4beauty.com，去掉 _320 取原图）
            return $"{BaseUrl}/api/covers/{prefix}/000-cover-issue-text.jpg";
        }
    }
}
