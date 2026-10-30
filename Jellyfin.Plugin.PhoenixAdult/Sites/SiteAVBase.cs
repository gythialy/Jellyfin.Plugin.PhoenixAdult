using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    /// <summary>
    /// AVBASE（www.avbase.net）：站点是 Next.js，正文全在 __NEXT_DATA__ 里，
    /// 因此走 /_next/data/{buildId}/works.json 数据接口而不是解析 HTML：
    /// buildId 从首页缓存 2 小时，搜索 works.json?q=，详情 works/{id}.json。
    /// work.work_id 就是番号（带 '-'，如 ABP-588），前缀（prestige/tameike/…）拼成
    /// "prefix:work_id" 才是详情接口能接受的 id。
    /// </summary>
    public class SiteAVBase : IProviderBase
    {
        private const string DefaultBaseUrl = "https://www.avbase.net";

        private static readonly TimeSpan BuildIdLifetime = TimeSpan.FromHours(2);

        private static readonly SemaphoreSlim BuildIdLock = new SemaphoreSlim(1, 1);

        /// <summary>封面优先来源：mgstage 的图最稳（duga 的 pic.duga.jp 经常 404）。</summary>
        private static readonly string[] CoverSourcePriority = { "mgstage", "fanza", "duga", "getchu", "pcolle" };

        /// <summary>剧照优先来源：fanza/dmm 的 samples 最全。</summary>
        private static readonly string[] SampleSourcePriority = { "fanza", "mgstage", "duga", "getchu", "pcolle" };

        /// <summary>Next.js 日期："Thu Apr 13 2017 09:00:00 GMT+0900 (Japan Standard Time)"。</summary>
        private static readonly Regex NextDateRegex = new Regex(
            @"(?<mon>[A-Za-z]{3})\s+(?<day>[0-9]{1,2})\s+(?<year>[0-9]{4})",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>AVBASE 不收录 FC2 / 无码番号。</summary>
        private static readonly Regex Fc2Regex = new Regex(
            @"^(?i)FC2[-_\s]*(?:PPV)?[-_\s]*[0-9]{6,8}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>收录时长（分钟）："172" / "120分"。</summary>
        private static readonly Regex VolumeRegex = new Regex(
            @"^(?<minutes>[0-9]{1,4})\s*分?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex DateRegex = new Regex(
            @"(?<year>[0-9]{4})[-/.](?<mon>[0-9]{1,2})[-/.](?<day>[0-9]{1,2})",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static string cachedBuildId;

        private static DateTime cachedBuildIdTime = DateTime.MinValue;

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            try
            {
                var keyword = searchTitle.Trim();
                if (keyword.Length == 0 || Fc2Regex.IsMatch(keyword))
                {
                    return result;
                }

                keyword = keyword.ToUpperInvariant();
                var json = await RequestSearch(siteNum, keyword, cancellationToken).ConfigureAwait(false);
                var works = json?["pageProps"]?["works"] as JArray;
                if (works == null)
                {
                    return result;
                }

                var wantedId = JavId.TryParse(keyword, out var javId) ? javId : keyword;
                foreach (var work in works)
                {
                    var workId = (string)work?["work_id"];
                    if (string.IsNullOrEmpty(workId))
                    {
                        continue;
                    }

                    workId = workId.Trim();
                    var id = JoinPrefixId((string)work?["prefix"], workId);
                    var title = (string)work?["title"];
                    var name = string.IsNullOrEmpty(title) ? workId : $"{workId} {title.Trim()}";

                    var res = new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, Helper.Encode(id) } },
                        Name = name,
                        ImageUrl = PickCover(work),
                        PremiereDate = ParseNextDate((string)work?["min_date"]),
                    };

                    if (string.Equals(workId, wantedId, StringComparison.OrdinalIgnoreCase))
                    {
                        // 精确命中排到最前（MovieProvider 会把带 IndexNumber 的结果按它排序）。
                        res.IndexNumber = 100;
                    }

                    result.Add(res);
                }
            }
            catch (Exception e)
            {
                Logger.Debug($"AVBASE search failed: {e.GetType().Name}: {e.Message}");
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

            var workId = ExtractWorkId(sceneID[0]);
            if (string.IsNullOrEmpty(workId))
            {
                return result;
            }

            // 详情接口只认 "prefix:work_id"；只有番号时先用搜索补齐前缀。
            if (!workId.Contains(':'))
            {
                workId = await ResolvePrefixedWorkId(siteNum, workId, cancellationToken).ConfigureAwait(false);
            }

            var json = await RequestWork(siteNum, workId, cancellationToken).ConfigureAwait(false);
            var work = json?["pageProps"]?["work"];
            if (work == null || work.Type == JTokenType.Null)
            {
                Logger.Debug($"AVBASE work not found: {workId}");
                return result;
            }

            var workIdValue = (string)work["work_id"];
            if (string.IsNullOrEmpty(workIdValue))
            {
                workIdValue = workId;
            }

            workIdValue = workIdValue.Trim();
            var id = JoinPrefixId((string)work["prefix"], workIdValue);
            var title = (string)work["title"];

            result.Item.Name = string.IsNullOrEmpty(title) ? workIdValue : title.Trim();
            result.Item.OriginalTitle = workIdValue;
            result.Item.ExternalId = $"{GetBaseUrl(siteNum)}/works/{id}";

            var description = PickField(work, "iteminfo", "description");
            if (!string.IsNullOrEmpty(description))
            {
                result.Item.Overview = description;
            }

            var releaseDate = ParseNextDate((string)work["min_date"]);
            if (!releaseDate.HasValue)
            {
                releaseDate = ParseDate(PickField(work, "date"));
            }

            if (releaseDate.HasValue)
            {
                result.Item.PremiereDate = releaseDate;
            }

            var cover = PickCover(work);
            if (!string.IsNullOrEmpty(cover))
            {
                result.Item.SetImage(new ItemImageInfo { Path = cover, Type = ImageType.Primary }, 0);
            }

            var runtime = PickRuntime(work);
            if (runtime.HasValue && runtime.Value.Ticks > 0)
            {
                result.Item.RunTimeTicks = runtime.Value.Ticks;
            }

            var maker = PickField(work, "maker", "name");
            if (string.IsNullOrEmpty(maker))
            {
                maker = PickField(work, "label", "name");
            }

            if (!string.IsNullOrEmpty(maker))
            {
                result.Item.AddStudio(maker);
            }

            var genres = work["genres"] as JArray;
            if (genres == null || genres.Count == 0)
            {
                genres = work["tags"] as JArray;
            }

            if (genres != null)
            {
                foreach (var genre in genres)
                {
                    var name = (string)genre?["name"];
                    if (!string.IsNullOrEmpty(name))
                    {
                        result.Item.AddGenre(name.Trim());
                    }
                }
            }

            var casts = work["casts"] as JArray;
            if (casts != null)
            {
                foreach (var cast in casts)
                {
                    var name = (string)cast?["actor"]?["name"];
                    if (string.IsNullOrEmpty(name))
                    {
                        name = (string)cast?["name"];
                    }

                    if (!string.IsNullOrEmpty(name))
                    {
                        result.AddPerson(new PersonInfo { Name = name.Trim() });
                    }
                }
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

            try
            {
                var workId = ExtractWorkId(sceneID[0]);
                if (string.IsNullOrEmpty(workId))
                {
                    return result;
                }

                if (!workId.Contains(':'))
                {
                    workId = await ResolvePrefixedWorkId(siteNum, workId, cancellationToken).ConfigureAwait(false);
                }

                var json = await RequestWork(siteNum, workId, cancellationToken).ConfigureAwait(false);
                var work = json?["pageProps"]?["work"];
                if (work == null || work.Type == JTokenType.Null)
                {
                    return result;
                }

                var cover = PickCover(work);
                var samples = PickSamples(work);

                if (!string.IsNullOrEmpty(cover))
                {
                    result.Add(new RemoteImageInfo { Url = cover, Type = ImageType.Primary });
                }
                else if (samples.Count > 0)
                {
                    result.Add(new RemoteImageInfo { Url = samples[0], Type = ImageType.Primary });
                }

                foreach (var sample in samples.Take(10))
                {
                    result.Add(new RemoteImageInfo { Url = sample, Type = ImageType.Backdrop });
                }
            }
            catch (Exception e)
            {
                Logger.Debug($"AVBASE images failed: {e.GetType().Name}: {e.Message}");
            }

            return result;
        }

        private static string GetBaseUrl(int[] siteNum)
        {
            var baseUrl = siteNum == null ? string.Empty : Helper.GetSearchBaseURL(siteNum);
            if (string.IsNullOrEmpty(baseUrl))
            {
                baseUrl = DefaultBaseUrl;
            }

            return baseUrl.TrimEnd('/');
        }

        private static async Task<string> GetBuildId(int[] siteNum, bool forceRefresh, CancellationToken cancellationToken)
        {
            if (!forceRefresh && !string.IsNullOrEmpty(cachedBuildId) && DateTime.UtcNow - cachedBuildIdTime < BuildIdLifetime)
            {
                return cachedBuildId;
            }

            await BuildIdLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!forceRefresh && !string.IsNullOrEmpty(cachedBuildId) && DateTime.UtcNow - cachedBuildIdTime < BuildIdLifetime)
                {
                    return cachedBuildId;
                }

                var homeData = await Fetcher.Html($"{GetBaseUrl(siteNum)}/", cancellationToken).ConfigureAwait(false);
                var nextData = homeData?.SelectSingleText("//script[@id='__NEXT_DATA__']");
                if (string.IsNullOrEmpty(nextData))
                {
                    return cachedBuildId;
                }

                JObject json;
                try
                {
                    json = JObject.Parse(nextData);
                }
                catch (JsonException)
                {
                    return cachedBuildId;
                }

                var buildId = (string)json["buildId"];
                if (string.IsNullOrEmpty(buildId))
                {
                    return cachedBuildId;
                }

                cachedBuildId = buildId;
                cachedBuildIdTime = DateTime.UtcNow;
                return buildId;
            }
            finally
            {
                BuildIdLock.Release();
            }
        }

        private static async Task<JObject> RequestSearch(int[] siteNum, string keyword, CancellationToken cancellationToken)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var buildId = await GetBuildId(siteNum, attempt > 0, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(buildId))
                {
                    return null;
                }

                var url = $"{GetBaseUrl(siteNum)}/_next/data/{buildId}/works.json?q={Uri.EscapeDataString(keyword)}";
                var json = await Fetcher.Json(url, cancellationToken).ConfigureAwait(false);
                if (json?["pageProps"] != null)
                {
                    return json;
                }
            }

            return null;
        }

        private static async Task<JObject> RequestWork(int[] siteNum, string workId, CancellationToken cancellationToken)
        {
            var escaped = Uri.EscapeDataString(workId);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var buildId = await GetBuildId(siteNum, attempt > 0, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(buildId))
                {
                    return null;
                }

                var url = $"{GetBaseUrl(siteNum)}/_next/data/{buildId}/works/{escaped}.json?id={escaped}";
                var json = await Fetcher.Json(url, cancellationToken).ConfigureAwait(false);
                if (json?["pageProps"]?["work"] != null)
                {
                    return json;
                }

                if (json?["notFound"] != null)
                {
                    // 站点明确回了 notFound，换 buildId 也没用。
                    return null;
                }
            }

            return null;
        }

        /// <summary>用搜索接口把裸番号补成 "prefix:work_id"（详情接口不接受裸番号）。</summary>
        private static async Task<string> ResolvePrefixedWorkId(int[] siteNum, string workId, CancellationToken cancellationToken)
        {
            var json = await RequestSearch(siteNum, workId.ToUpperInvariant(), cancellationToken).ConfigureAwait(false);
            var works = json?["pageProps"]?["works"] as JArray;
            if (works == null)
            {
                return workId;
            }

            foreach (var work in works)
            {
                var candidate = (string)work?["work_id"];
                if (string.IsNullOrEmpty(candidate) || !candidate.Trim().Equals(workId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var prefix = (string)work?["prefix"];
                if (string.IsNullOrEmpty(prefix))
                {
                    return workId;
                }

                return JoinPrefixId(prefix, workId.ToUpperInvariant());
            }

            return workId;
        }

        private static string ExtractWorkId(string sceneID)
        {
            if (string.IsNullOrEmpty(sceneID))
            {
                return string.Empty;
            }

            var decoded = DecodeSafe(sceneID);
            decoded = decoded.Trim();
            if (decoded.StartsWith("http", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(decoded, UriKind.Absolute, out var uri))
            {
                decoded = uri.AbsolutePath.TrimEnd('/').Split('/').LastOrDefault() ?? string.Empty;
            }
            else if (decoded.EndsWith("/", StringComparison.Ordinal))
            {
                decoded = decoded.TrimEnd('/');
            }

            return Uri.UnescapeDataString(decoded).Trim();
        }

        /// <summary>sceneID 由本 handler 编码；也兼容直接传入的裸 workId（手工 Identify）。</summary>
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
                // 非 Base58 输入（手工 Identify 直接给 workId）会抛 FormatException，按裸值处理。
            }

            return value;
        }

        private static string JoinPrefixId(string prefix, string workId)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                return workId;
            }

            return $"{prefix.Trim()}:{workId}";
        }

        private static List<JToken> GetProducts(JToken work, string[] priority)
        {
            var products = work?["products"] as JArray;
            if (products == null)
            {
                return new List<JToken>();
            }

            return products.OrderBy(o => RankSource((string)o?["source"], priority)).ToList();
        }

        private static int RankSource(string source, string[] priority)
        {
            var index = Array.FindIndex(priority, o => o.Equals(source ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            return index < 0 ? priority.Length : index;
        }

        private static string PickCover(JToken work)
        {
            foreach (var product in GetProducts(work, CoverSourcePriority))
            {
                var image = (string)product?["image_url"];
                if (!string.IsNullOrEmpty(image))
                {
                    return image.Trim();
                }
            }

            foreach (var product in GetProducts(work, CoverSourcePriority))
            {
                var image = (string)product?["thumbnail_url"];
                if (!string.IsNullOrEmpty(image))
                {
                    return image.Trim();
                }
            }

            return string.Empty;
        }

        /// <summary>iteminfo.volume 是收录时长（分钟），只在视频商品上使用。</summary>
        private static TimeSpan? PickRuntime(JToken work)
        {
            foreach (var product in GetProducts(work, CoverSourcePriority))
            {
                var contentType = (string)product?["content_type"];
                if (!string.Equals(contentType, "video", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var volume = (string)product?["iteminfo"]?["volume"];
                var match = VolumeRegex.Match(volume ?? string.Empty);
                if (match.Success
                    && int.TryParse(match.Groups["minutes"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
                    && minutes > 0)
                {
                    return TimeSpan.FromMinutes(minutes);
                }
            }

            return null;
        }

        private static List<string> PickSamples(JToken work)
        {
            foreach (var product in GetProducts(work, SampleSourcePriority))
            {
                var samples = product?["sample_image_urls"] as JArray;
                if (samples == null || samples.Count == 0)
                {
                    continue;
                }

                var images = new List<string>();
                foreach (var sample in samples)
                {
                    var url = (string)sample?["l"];
                    if (string.IsNullOrEmpty(url))
                    {
                        url = (string)sample?["s"];
                    }

                    if (!string.IsNullOrEmpty(url) && !images.Contains(url.Trim()))
                    {
                        images.Add(url.Trim());
                    }
                }

                if (images.Count > 0)
                {
                    return images;
                }
            }

            return new List<string>();
        }

        /// <summary>按来源优先级取第一个非空字段（如 maker.name / iteminfo.description）。</summary>
        private static string PickField(JToken work, params string[] path)
        {
            foreach (var product in GetProducts(work, CoverSourcePriority))
            {
                JToken token = product;
                foreach (var segment in path)
                {
                    token = token?[segment];
                }

                var value = (string)token;
                if (!string.IsNullOrEmpty(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        private static DateTime? ParseNextDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var match = NextDateRegex.Match(value);
            if (match.Success)
            {
                var text = $"{match.Groups["mon"].Value} {match.Groups["day"].Value} {match.Groups["year"].Value}";
                if (DateTime.TryParseExact(text, "MMM d yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    return date;
                }
            }

            return ParseDate(value);
        }

        private static DateTime? ParseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var match = DateRegex.Match(value);
            if (match.Success)
            {
                var text = $"{match.Groups["year"].Value}-{match.Groups["mon"].Value}-{match.Groups["day"].Value}";
                if (DateTime.TryParseExact(text, "yyyy-M-d", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    return date;
                }
            }

            return null;
        }
    }
}
