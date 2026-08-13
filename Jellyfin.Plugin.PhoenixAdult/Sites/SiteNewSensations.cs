using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
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
    public class SiteNewSensations : IProviderBase
    {
        private enum Site
        {
            Default,
            FamilyXXX,
            HotWifeXXX,
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            // 站内搜索不可用（无搜索接口），改分页浏览 + 标题过滤（TPDB 同款模式）
            var titleTokens = searchTitle.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (var page = 1; page <= 5; page++)
            {
                var pageUrl = page == 1
                    ? $"{Helper.GetSearchSearchURL(siteNum)}"
                    : $"{Helper.GetSearchSearchURL(siteNum)}?page={page}";
                var httpResult = await HTTP.Request(pageUrl, HttpMethod.Get, cancellationToken);
                if (!httpResult.IsOK)
                {
                    break;
                }

                var pageDoc = HTML.ElementFromString(httpResult.Content);
                var links = pageDoc.SelectNodesSafe("//a[contains(@href, '/updates/')]");
                if (!links.Any())
                {
                    break;
                }

                foreach (var link in links)
                {
                    var href = link.GetAttributeValue("href", string.Empty);
                    if (string.IsNullOrEmpty(href) || !href.Contains("/updates/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!titleTokens.All(t => href.ToLowerInvariant().Contains(t, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var sceneURL = new Uri(href);

                    // 更新页链接无 tour 前缀（/updates/...），访问会 301 到 /tour_ns/updates/... → 补前缀
                    var tourRoot = new Uri(Helper.GetSearchSearchURL(siteNum)).AbsolutePath;
                    var tourIdx = tourRoot.LastIndexOf("/updates/", StringComparison.OrdinalIgnoreCase);
                    var tourPrefix = tourIdx > 0 ? tourRoot.Substring(0, tourIdx) : string.Empty;
                    var scenePath = sceneURL.AbsolutePath;
                    if (!string.IsNullOrEmpty(tourPrefix) && !scenePath.StartsWith(tourPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        scenePath = tourPrefix + scenePath;
                    }

                    var curId = Helper.Encode(scenePath);
                    if (result.Any(r => r.ProviderIds.First().Value == curId))
                    {
                        continue;
                    }

                    // /tour_ns/updates/New-Sensations-Title.html -> "New Sensations Title"
                    var lastSegment = scenePath.Trim('/').Split('/').Last().Replace(".html", string.Empty, StringComparison.OrdinalIgnoreCase);
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curId } },
                        Name = lastSegment.Replace('-', ' '),
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }

                if (result.Count >= 10)
                {
                    break;
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

            if (sceneID == null || siteNum == null)
            {
                return result;
            }

            string sceneURL = Helper.Decode(sceneID[0]),
                sceneDate = string.Empty;

            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            // 兼容旧 ProviderId：/updates/... 补 tour 前缀（如 /tour_ns/updates/...），否则 301
            var tourRoot = new Uri(Helper.GetSearchSearchURL(siteNum)).AbsolutePath;
            var tourIdx = tourRoot.LastIndexOf("/updates/", StringComparison.OrdinalIgnoreCase);
            var tourPrefix = tourIdx > 0 ? tourRoot.Substring(0, tourIdx) : string.Empty;
            if (!string.IsNullOrEmpty(tourPrefix)
                && sceneURL.Contains("/updates/", StringComparison.OrdinalIgnoreCase)
                && !sceneURL.Contains(tourPrefix, StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = sceneURL.Replace("/updates/", tourPrefix + "/updates/", StringComparison.OrdinalIgnoreCase);
            }

            var searchSite = Helper.GetSearchSiteName(siteNum);
            Logger.Info($"Search site: {searchSite}");
            var site = searchSite switch
            {
                "Family XXX" => Site.FamilyXXX,
                "HotwifeXXX" => Site.HotWifeXXX,
                _ => Site.Default
            };
            Logger.Info(site.ToString());

            if (sceneID.Length > 1)
            {
                sceneDate = sceneID[1];
            }

            result.Item.ExternalId = sceneURL;
            result.HasMetadata = true;
            result.Item.AddStudio("New Sensations");
            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken).ConfigureAwait(false);

            switch (site)
            {
                case Site.FamilyXXX:
                    {
                        result.Item.AddStudio("Family XXX");
                        result.Item.Name = sceneData.SelectSingleText("//div[@class='sceneRight']//div[@class='indScene']//h2");

                        var dateNode = sceneData.SelectSingleText("//div[@class='sceneRight']//div[@class='indScene']//div[@class='sceneDateP']//span").TrimEnd(',');
                        if (DateTime.TryParseExact(dateNode, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
                        {
                            result.Item.PremiereDate = sceneDateObj;
                        }

                        var descriptionNode = sceneData.SelectNodesSafe("//div[@class='description']//p");
                        var overview = descriptionNode[0].InnerText.Trim();

                        // remove <span>Description:</span> from beginning
                        overview = overview.Substring(13);
                        result.Item.Overview = overview;

                        // performers
                        var performerNodes = sceneData.SelectNodesSafe("//div[@class='sceneRight']//div[@class='indScene']//div[@class='sceneTextLink']//p//span//a");

                        foreach (var performerNode in performerNodes)
                        {
                            var performerUrl = performerNode.Attributes["href"].Value;
                            var performerPage = await HTML.ElementFromURL(performerUrl, cancellationToken).ConfigureAwait(false);
                            var performerImg = performerPage.SelectSingleNode("//div[contains(@class, 'modelBioPic')]/img");
                            result.AddPerson(new PersonInfo
                            {
                                Name = performerNode.InnerText,
                                ImageUrl = performerImg.Attributes["src0_1x"].Value,
                            });
                        }

                        break;
                    }

                case Site.HotWifeXXX:
                    {
                        result.Item.AddStudio("HotwifeXXX");
                        result.Item.Name = sceneData.SelectSingleText("//div[@class='trailerInfo']/h2");

                        var dateNodeText = sceneData.SelectSingleText("//div[@class='trailerInfo']//div[contains(@class, 'released2')]");
                        var date = dateNodeText.Substring(0, dateNodeText.IndexOf(','));
                        if (DateTime.TryParseExact(date, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
                        {
                            result.Item.PremiereDate = sceneDateObj;
                        }

                        var descriptionNode = sceneData.SelectNodesSafe("//div[@class='dvdDescription']//p");
                        var overview = descriptionNode[0].InnerText.Trim();

                        // remove <span>Description:</span> from beginning
                        overview = overview.Substring(13);
                        result.Item.Overview = overview;

                        // performers
                        var performerNodes = sceneData.SelectNodesSafe("//div[@class='trailerInfo']//span[@class='tour_update_models']/a");

                        foreach (var performerNode in performerNodes)
                        {
                            result.AddPerson(new PersonInfo
                            {
                                Name = performerNode.InnerText,
                            });
                        }

                        break;
                    }

                default:
                    {
                        result.Item.Name = sceneData.SelectSingleText("//div[@class='sceneRight']//div[@class='indScene']//h1");

                        var dateNode = sceneData.SelectSingleText("//div[@class='sceneRight']//div[@class='indScene']//div[@class='sceneDateP']//span").TrimEnd(',');
                        if (DateTime.TryParseExact(dateNode, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
                        {
                            result.Item.PremiereDate = sceneDateObj;
                        }

                        var descriptionNodes = sceneData.SelectNodesSafe("//div[@class='description']//h2");
                        if (descriptionNodes.Count > 0)
                        {
                            var overview = descriptionNodes[0].InnerText.Trim();
                            result.Item.Overview = overview;
                        }

                        // performers
                        var performerNodes = sceneData.SelectNodesSafe("//div[@class='sceneRight']//div[@class='indScene']//div[@class='sceneTextLink']//p//span//a");

                        foreach (var performerNode in performerNodes)
                        {
                            result.AddPerson(new PersonInfo
                            {
                                Name = performerNode.InnerText,
                            });
                        }

                        break;
                    }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();

            if (sceneID == null)
            {
                return result;
            }

            var sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken).ConfigureAwait(false);

            var posterNode = sceneData.SelectSingleNode("//span[@id='trailer_thumb']//span//img");
            var posterSrc = posterNode?.Attributes["src"]?.Value;
            if (!string.IsNullOrEmpty(posterSrc))
            {
                result.Add(new RemoteImageInfo
                {
                    Url = posterSrc,
                    Type = ImageType.Primary,
                });
            }

            return result;
        }
    }
}
