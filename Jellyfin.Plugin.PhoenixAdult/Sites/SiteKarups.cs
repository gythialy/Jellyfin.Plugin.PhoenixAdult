using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Helpers.Utils;
using MediaBrowser.Model.Entities;


#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class SiteKarups : IProviderBase
    {
        private const string SiteName = "Karups";
        private const string BaseUrl = "https://www.karups.com";

        private IDictionary<string, string> GetCookies()
        {
            return new Dictionary<string, string> { { "warningHidden", "hide" } };
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var searchUrl = $"{BaseUrl}/search/{searchTitle.Replace(" ", "-")}/";
            var doc = await HTML.ElementFromURL(searchUrl, cancellationToken, null, GetCookies());

            var actressPageUrl = doc.SelectSingleNode("//div[@class='item-inside']//a").GetAttributeValue("href", string.Empty);
            doc = await HTML.ElementFromURL(actressPageUrl, cancellationToken, null, GetCookies());

            var searchResults = new List<RemoteSearchResult>();
            var nodes = doc.SelectNodes("//div[contains(@class, 'listing-videos')]//div[@class='item']");
            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    var titleNoFormatting = node.SelectSingleNode(".//span[@class='title']").InnerText;
                    var curId = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(node.SelectSingleNode(".//a").GetAttributeValue("href", string.Empty)));
                    var releaseDate = DateTime.Parse(node.SelectSingleNode(".//span[@class='date']").InnerText.Replace("th", string.Empty).Replace("st", string.Empty).Trim()).ToString("yyyy-MM-dd");

                    var subSiteRaw = node.SelectSingleNode(".//div[@class='meta']//span[@class='date-and-site']//span").InnerText;
                    var subSite = string.Empty;
                    if (subSiteRaw == "kha")
                    {
                        subSite = "KarupsHA";
                    }
                    else if (subSiteRaw == "kow")
                    {
                        subSite = "KarupsOW";
                    }
                    else if (subSiteRaw == "kpc")
                    {
                        subSite = "KarupsPC";
                    }

                    searchResults.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                        Name = $"{titleNoFormatting} [{subSite}] {releaseDate}",
                    });
                }
            }

            return searchResults;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var metadataId = sceneID[0].Split('|');
            var sceneUrl = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(metadataId[0]));
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = $"{BaseUrl}{sceneUrl}";
            }

            var doc = await HTML.ElementFromURL(sceneUrl, cancellationToken, null, GetCookies());

            var metadataResult = new MetadataResult<BaseItem>
            {
                Item = new Movie(),
                HasMetadata = true,
            };

            metadataResult.Item.Name = doc.SelectSingleNode("//h1//span[@class='title']").InnerText.Trim();
            var summaryNode = doc.SelectSingleNode("//div[@class='content-information-description']//p");
            if (summaryNode != null)
            {
                metadataResult.Item.Overview = summaryNode.InnerText.Trim();
            }

            metadataResult.Item.AddStudio(SiteName);

            var tagline = doc.SelectSingleNode("//h1//span[@class='sup-title']//span").InnerText.Trim();
            metadataResult.Item.Tagline = tagline;

            var date = doc.SelectSingleNode("//span[@class='date']/span[@class='content']").InnerText.Replace(tagline, string.Empty).Replace("Video added on", string.Empty).Trim();
            metadataResult.Item.PremiereDate = DateTime.Parse(date);

            if (tagline == "KarupsHA")
            {
                metadataResult.Item.AddGenre("Amateur");
            }
            else if (tagline == "KarupsOW")
            {
                metadataResult.Item.AddGenre("MILF");
            }

            var actors = doc.SelectNodes("//span[@class='models']//a");
            if (actors != null)
            {
                foreach (var actor in actors)
                {
                    var actorName = actor.InnerText.Trim();
                    var actorPageUrl = actor.GetAttributeValue("href", string.Empty);
                    var actorDoc = await HTML.ElementFromURL(actorPageUrl, cancellationToken, null, this.GetCookies());
                    var actorPhotoUrl = actorDoc.SelectSingleNode("//div[@class='model-thumb']//img").GetAttributeValue("src", string.Empty);
                    metadataResult.AddPerson(new PersonInfo { Name = actorName, ImageUrl = actorPhotoUrl, Type = PersonKind.Actor });
                }
            }

            return metadataResult;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var metadataId = sceneID[0].Split('|');
            var sceneUrl = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(metadataId[0]));
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = $"{BaseUrl}{sceneUrl}";
            }

            var doc = await HTML.ElementFromURL(sceneUrl, cancellationToken, null, GetCookies());

            var xpaths = new[]
            {
                "//div[@class='video-player']//video/@poster",
                "//img[@class='poster']/@src",
                "//div[@class='video-thumbs']//img/@src",
            };

            var art = new List<string>();
            foreach (var xpath in xpaths)
            {
                var nodes = doc.SelectNodes(xpath);
                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        art.Add(node.GetAttributeValue("src", node.GetAttributeValue("poster", string.Empty)));
                    }
                }
            }

            var list = new List<RemoteImageInfo>();
            foreach (var imageUrl in art)
            {
                list.Add(new RemoteImageInfo { Url = imageUrl, Type = ImageType.Primary });
            }

            return list;
        }

        private static int LevenshteinDistance(string source, string target)
        {
            if (string.IsNullOrEmpty(source))
            {
                return string.IsNullOrEmpty(target) ? 0 : target.Length;
            }

            if (string.IsNullOrEmpty(target))
            {
                return source.Length;
            }

            var distance = new int[source.Length + 1, target.Length + 1];

            for (var i = 0; i <= source.Length; distance[i, 0] = i++)
            {
            }

            for (var j = 0; j <= target.Length; distance[0, j] = j++)
            {
            }

            for (var i = 1; i <= source.Length; i++)
            {
                for (var j = 1; j <= target.Length; j++)
                {
                    var cost = (target[j - 1] == source[i - 1]) ? 0 : 1;
                    distance[i, j] = Math.Min(Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1), distance[i - 1, j - 1] + cost);
                }
            }

            return distance[source.Length, target.Length];
        }
    }
}
