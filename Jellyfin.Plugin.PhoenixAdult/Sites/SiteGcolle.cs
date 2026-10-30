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
    /// Gcolle（ジーコレ）：2026-10 起整站改为 Vite SPA（/adult），旧商品页
    /// /product_info.php/products_id/{id} 全部 302 到 /adult/unavailable/content。
    /// 本实现保留 ID 直连与搜索页解析能力：站点恢复服务端页面后即可工作；
    /// 当前不可用时优雅返回空结果（不抛异常）。
    /// </summary>
    public class SiteGcolle : IProviderBase
    {
        private static readonly Regex IdRegex = new Regex(@"(?:gcolle[-_ ]?)?(?<id>[0-9]{4,9})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            var url = $"{Helper.GetSearchBaseURL(siteNum)}/search?keyword={Uri.EscapeDataString(searchTitle)}";
            var searchData = await Fetcher.Html(url, cancellationToken).ConfigureAwait(false);

            foreach (var node in searchData.SelectNodesSafe("//a[contains(@href, 'products_id/')]"))
            {
                var href = node.GetAttributeValue("href", string.Empty);
                var match = Regex.Match(href, @"products_id/(?<id>[0-9]+)");
                if (!match.Success)
                {
                    continue;
                }

                var name = node.InnerText.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, Helper.Encode(match.Groups["id"].Value) } },
                    Name = name,
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

            var id = Helper.Decode(sceneID[0]);
            if (!IdRegex.IsMatch(id))
            {
                return result;
            }

            id = IdRegex.Match(id).Groups["id"].Value;
            var sceneURL = $"{Helper.GetSearchBaseURL(siteNum)}/product_info.php/products_id/{id}";
            var sceneData = await Fetcher.Html(sceneURL, cancellationToken).ConfigureAwait(false);

            var title = sceneData.SelectSingleText("//title").Trim();
            if (string.IsNullOrEmpty(title) || title.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
            {
                return result;
            }

            result.Item.Name = title;
            result.Item.ExternalId = sceneURL;
            result.Item.AddStudio(Helper.GetSearchSiteName(siteNum));

            var cover = sceneData.SelectSingleText("//meta[@property='og:image']/@content");
            if (!string.IsNullOrEmpty(cover))
            {
                result.Item.SetImage(new ItemImageInfo { Path = cover, Type = ImageType.Primary }, 0);
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

            var id = Helper.Decode(sceneID[0]);
            var match = IdRegex.Match(id);
            if (!match.Success)
            {
                return result;
            }

            var sceneURL = $"{Helper.GetSearchBaseURL(siteNum)}/product_info.php/products_id/{match.Groups["id"].Value}";
            var sceneData = await Fetcher.Html(sceneURL, cancellationToken).ConfigureAwait(false);
            var cover = sceneData.SelectSingleText("//meta[@property='og:image']/@content");
            if (!string.IsNullOrEmpty(cover))
            {
                result.Add(new RemoteImageInfo { Type = ImageType.Primary, Url = cover });
            }

            return result;
        }
    }
}
