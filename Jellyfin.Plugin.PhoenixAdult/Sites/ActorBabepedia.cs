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
    /// Babepedia 演员元数据源：姓名/别名（含汉字与罗马字互查）、简介、生日、出生地、国籍、头像。
    /// 站点有 Cloudflare 挑战，统一走 Fetcher（自动回落 FlareSolverr）。
    /// </summary>
    public class ActorBabepedia : IProviderBase
    {
        private static readonly Regex BornRegex = new Regex(
            @"(\d{1,2})(?:st|nd|rd|th)?\s+of\s+(\w+)\s+(\d{4})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string actorName, DateTime? actorDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(actorName))
            {
                return result;
            }

            var url = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(actorName);
            var actorData = await Fetcher.Html(url, cancellationToken).ConfigureAwait(false);

            foreach (var node in actorData.SelectNodesSafe("//div[contains(@class, 'results')]//div[contains(@class, 'thumbshot')]/a"))
            {
                var href = node.GetAttributeValue("href", string.Empty);
                if (string.IsNullOrEmpty(href))
                {
                    continue;
                }

                var name = node.SelectSingleText(".//img/@alt");
                if (string.IsNullOrEmpty(name))
                {
                    name = node.SelectSingleText(".//span[contains(@class, 'thumbtext')]");
                    name = name.Split('\n').First().Split("aka").First().Trim();
                }

                var img = node.SelectSingleText(".//img/@src");
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

            if (result.Count == 0)
            {
                // 精确姓名会 302 到个人资料页（没有 .results 卡片），直接取资料页姓名
                var profileName = actorData.SelectSingleText("//*[@id='babename']").Trim();
                if (!string.IsNullOrEmpty(profileName))
                {
                    var path = new Uri(url).AbsolutePath;
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, Helper.Encode(path) } },
                        Name = profileName,
                    });
                }
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
            var name = actorData.SelectSingleText("//*[@id='babename']").Trim();
            if (string.IsNullOrEmpty(name))
            {
                return result;
            }

            result.Item.Name = name;
            result.Item.ExternalId = actorURL;
            result.Item.Overview = actorData.SelectSingleText("//*[@id='biotext']").Trim();

            var aliases = new List<string> { name };
            foreach (var aliasNode in actorData.SelectNodesSafe("//span[@class='aliasname']"))
            {
                var alias = aliasNode.InnerText.Trim();
                if (!string.IsNullOrEmpty(alias) && !aliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
                {
                    aliases.Add(alias);
                }
            }

            result.Item.OriginalTitle = string.Join(", ", aliases);

            var bornText = this.GetInfoValue(actorData, "Born");
            var bornMatch = BornRegex.Match(bornText);
            if (bornMatch.Success)
            {
                var dateText = $"{bornMatch.Groups[1].Value} {bornMatch.Groups[2].Value} {bornMatch.Groups[3].Value}";
                if (DateTime.TryParseExact(dateText, "d MMMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var birthday))
                {
                    result.Item.PremiereDate = birthday;
                }
            }

            var birthplace = this.GetInfoValue(actorData, "Birthplace");
            var locations = new List<string>();
            if (!string.IsNullOrEmpty(birthplace))
            {
                locations.Add(birthplace);
            }

            var nationality = this.GetInfoValue(actorData, "Nationality").Trim('(', ')', ' ');
            if (!string.IsNullOrEmpty(nationality) && !locations.Any(o => o.Contains(nationality, StringComparison.OrdinalIgnoreCase)))
            {
                locations.Add(nationality);
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

            var img = actorData.SelectSingleText("//div[@id='profimg']/a/@href");
            if (!string.IsNullOrEmpty(img) && !img.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
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

        private string GetInfoValue(HtmlAgilityPack.HtmlNode node, string label)
        {
            var value = node.SelectSingleText(
                $"//div[contains(@class, 'info-item')][span[contains(@class, 'label')][starts-with(normalize-space(.), '{label}')]]/span[contains(@class, 'value')]");
            return Regex.Replace(value, @"\s+", " ").Trim();
        }
    }
}
