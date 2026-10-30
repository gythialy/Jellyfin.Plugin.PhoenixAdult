using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    /// <summary>
    /// AV-LEAGUE 女优资料源：姓名（含假名）、别名、生日、出身、身高/三围、头像。
    /// </summary>
    public class ActorAvLeague : IProviderBase
    {
        private static readonly Regex BirthRegex = new Regex(@"([0-9]{4})年([0-9]{1,2})月([0-9]{1,2})日", RegexOptions.CultureInvariant);

        private static readonly Regex FuriganaRegex = new Regex(@"[（(]([^）)]+)[）)]", RegexOptions.CultureInvariant);

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string actorName, DateTime? actorDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(actorName))
            {
                return result;
            }

            var url = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(actorName);
            var searchData = await Fetcher.Html(url, cancellationToken).ConfigureAwait(false);

            foreach (var node in searchData.SelectNodesSafe("//div[@id='contents']//div[contains(@class, 'l-box')]"))
            {
                var href = node.SelectSingleText(".//div[contains(@class, 'l-name')]/a/@href");
                if (string.IsNullOrEmpty(href))
                {
                    continue;
                }

                var name = node.SelectSingleText(".//div[contains(@class, 'l-name')]/a").Trim();
                var img = node.SelectSingleText(".//div[contains(@class, 'l-pic')]//img/@src");
                if (!string.IsNullOrEmpty(img) && !img.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    img = Helper.GetSearchBaseURL(siteNum) + img;
                }

                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, Helper.Encode(new Uri(new Uri(Helper.GetSearchBaseURL(siteNum)), href).AbsolutePath) } },
                    Name = name,
                    ImageUrl = img,
                });
            }

            return result;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem>()
            {
                Item = new Person(),
            };

            if (sceneID == null || sceneID.Length == 0)
            {
                return result;
            }

            var actorURL = Helper.Decode(sceneID[0]);
            if (!actorURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                actorURL = Helper.GetSearchBaseURL(siteNum) + actorURL;
            }

            var actorData = await Fetcher.Html(actorURL, cancellationToken).ConfigureAwait(false);
            var panText = actorData.SelectSingleText("//*[@id='pan']//span").Trim();
            var titleText = actorData.SelectSingleText("//*[@id='j-prof']").Trim();

            var name = panText;
            if (string.IsNullOrEmpty(name))
            {
                name = FuriganaRegex.Replace(titleText, string.Empty).Trim();
            }

            if (string.IsNullOrEmpty(name))
            {
                return result;
            }

            result.Item.Name = name;
            result.Item.ExternalId = actorURL;

            var aliases = new List<string> { name };
            foreach (Match match in FuriganaRegex.Matches(titleText))
            {
                var alias = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(alias) && !aliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
                {
                    aliases.Add(alias);
                }
            }

            result.Item.OriginalTitle = string.Join(", ", aliases);
            result.Item.Overview = string.Empty;

            var rows = new Dictionary<string, string>();
            foreach (var tr in actorData.SelectNodesSafe("//table[contains(@class, 'info-tbl')]//tr"))
            {
                var key = tr.SelectSingleText(".//th").Trim();
                var value = tr.SelectSingleText(".//td").Trim();
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value) && value != "不明")
                {
                    rows[key] = value;
                }
            }

            if (rows.TryGetValue("生年月日", out var birthdayText))
            {
                var match = BirthRegex.Match(birthdayText);
                if (match.Success
                    && DateTime.TryParseExact(
                        $"{match.Groups[1].Value}-{match.Groups[2].Value}-{match.Groups[3].Value}",
                        "yyyy-M-d",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var birthday))
                {
                    result.Item.PremiereDate = birthday;
                }
            }

            var locations = new List<string>();
            if (rows.TryGetValue("出身", out var birthplace) && !string.IsNullOrEmpty(birthplace))
            {
                locations.Add(birthplace);
            }

            result.Item.ProductionLocations = new[] { string.Join(", ", locations) };

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            if (sceneID == null || sceneID.Length == 0)
            {
                return result;
            }

            var actorURL = Helper.Decode(sceneID[0]);
            if (!actorURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                actorURL = Helper.GetSearchBaseURL(siteNum) + actorURL;
            }

            var actorData = await Fetcher.Html(actorURL, cancellationToken).ConfigureAwait(false);

            var img = actorData.SelectSingleText("//div[contains(@class, 'i-pic-box')]//img/@src");
            if (string.IsNullOrEmpty(img))
            {
                img = actorData.SelectSingleText("//meta[@property='og:image']/@content");
            }

            if (!string.IsNullOrEmpty(img))
            {
                if (!img.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    img = Helper.GetSearchBaseURL(siteNum) + img;
                }

                result.Add(new RemoteImageInfo
                {
                    Type = ImageType.Primary,
                    Url = img,
                });
            }

            return result;
        }
    }
}
