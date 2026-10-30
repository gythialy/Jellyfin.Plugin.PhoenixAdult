using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
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

#if !__EMBY__
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    /// <summary>
    /// 麻豆区（MadouQu）：中文站点，条目 ID 为 slug（如 mdx-0267），详情页 meta 里带
    /// 番号 / 片名 / 女郎 / 封面 / 发布日期，比抓正文更稳。
    /// </summary>
    public class SiteMadouQu : IProviderBase
    {
        private static readonly Regex NumberRegex = new Regex(@"番[號号][：:]\s*([^\s]+)", RegexOptions.CultureInvariant);

        private static readonly Regex TitleRegex = new Regex(@"片名[：:]\s*(.+?)(?=\s+[^\s：:]{1,10}[：:]|$)", RegexOptions.CultureInvariant);

        private static readonly Regex ActressRegex = new Regex(@"女郎[：:]\s*(.+?)(?=\s+[^\s：:]{1,10}[：:]|$)", RegexOptions.CultureInvariant);

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            var url = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(searchTitle);
            var searchData = await Fetcher.Html(url, cancellationToken).ConfigureAwait(false);

            // 2026-10 起列表改用 resource-card 卡片（article 无 id，post-<id> 在 class 上），旧模板仍兼容。
            foreach (var node in searchData.SelectNodesSafe("//article[contains(@class, 'resource-card') or starts-with(@id, 'post-')]"))
            {
                var href = node.SelectSingleText(".//h2//a/@href");
                if (string.IsNullOrEmpty(href))
                {
                    href = node.SelectSingleText(".//a[contains(@class, 'cover-link')]/@href");
                }

                if (string.IsNullOrEmpty(href))
                {
                    href = node.SelectSingleText(".//div[contains(@class, 'entry-media')]//a/@href");
                }

                if (string.IsNullOrEmpty(href))
                {
                    continue;
                }

                var name = WebUtility.HtmlDecode(node.SelectSingleText(".//img/@alt")).Trim();
                if (string.IsNullOrEmpty(name))
                {
                    name = WebUtility.HtmlDecode(node.SelectSingleText(".//h2//a")).Trim();
                }

                var img = node.SelectSingleText(".//img/@src");
                if (string.IsNullOrEmpty(img))
                {
                    img = node.SelectSingleText(".//img/@data-src");
                }

                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, Helper.Encode(new Uri(href).AbsolutePath) } },
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
                Item = new Movie(),
                People = new List<PersonInfo>(),
            };

            if (sceneID == null || sceneID.Length == 0)
            {
                return result;
            }

            var sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var sceneData = await Fetcher.Html(sceneURL, cancellationToken).ConfigureAwait(false);

            var description = sceneData.SelectSingleText("//meta[@property='og:description']/@content");
            if (string.IsNullOrEmpty(description))
            {
                description = sceneData.SelectSingleText("//meta[@name='description']/@content");
            }

            var number = NumberRegex.Match(description).Groups[1].Value.Trim();
            var title = TitleRegex.Match(description).Groups[1].Value.Trim();
            var actresses = ActressRegex.Match(description).Groups[1].Value.Trim();

            if (string.IsNullOrEmpty(title))
            {
                title = sceneData.SelectSingleText("//meta[@property='og:title']/@content").Trim();
            }

            if (string.IsNullOrEmpty(title))
            {
                title = sceneData.SelectSingleText("//title").Trim();
            }

            result.Item.Name = title;
            result.Item.ExternalId = sceneURL;
            result.Item.AddStudio("MadouQu");

            if (!string.IsNullOrEmpty(number))
            {
                result.Item.SetProviderId(Plugin.Instance.Name, Helper.Encode(new Uri(sceneURL).AbsolutePath));
                result.Item.OfficialRating = null;
                result.Item.OriginalTitle = number;
            }

            var cover = sceneData.SelectSingleText("//meta[@property='og:image']/@content");
            if (!string.IsNullOrEmpty(cover))
            {
                result.Item.SetImage(new ItemImageInfo { Path = cover, Type = ImageType.Primary }, 0);
            }

            var publishedAt = sceneData.SelectSingleText("//meta[@property='article:published_time']/@content");
            if (DateTime.TryParse(publishedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var releaseDate))
            {
                result.Item.PremiereDate = releaseDate;
            }

            foreach (var tag in sceneData.SelectNodesSafe("//div[contains(@class, 'entry-tags')]/a"))
            {
                var genre = tag.InnerText.Trim();
                if (!string.IsNullOrEmpty(genre))
                {
                    result.Item.AddGenre(genre);
                }
            }

            foreach (var actress in actresses.Split(new[] { '、', '，', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = actress.Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = name, Type = PersonKind.Actor });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            if (sceneID == null || sceneID.Length == 0)
            {
                return result;
            }

            var sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var sceneData = await Fetcher.Html(sceneURL, cancellationToken).ConfigureAwait(false);
            var cover = sceneData.SelectSingleText("//meta[@property='og:image']/@content");
            if (!string.IsNullOrEmpty(cover))
            {
                result.Add(new RemoteImageInfo { Type = ImageType.Primary, Url = cover });
                result.Add(new RemoteImageInfo { Type = ImageType.Backdrop, Url = cover });
            }

            return result;
        }
    }
}
