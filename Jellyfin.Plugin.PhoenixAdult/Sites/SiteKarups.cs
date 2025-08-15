using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Jellyfin.Plugin.PhoenixAdult.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;

namespace PhoenixAdult.Sites
{
    public class SiteKarups : IProviderBase
    {
        private const string SiteName = "Karups";
        private const string BaseUrl = "https://www.karups.com";

        public Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var searchUrl = $"{BaseUrl}/search/{searchTitle.Replace(" ", "-")}/";
            var doc = new HtmlDocument();
            var cookies = new CookieContainer();
            cookies.Add(new Cookie("warningHidden", "hide", "/", ".karups.com"));
            doc.LoadHtml(new PhoenixAdultHttpClient(cookies).Get(searchUrl));

            var actressPageUrl = doc.DocumentNode.SelectSingleNode("//div[@class='item-inside']//a").GetAttributeValue("href", string.Empty);
            doc.LoadHtml(new PhoenixAdultHttpClient(cookies).Get(actressPageUrl));

            var searchResults = new List<RemoteSearchResult>();
            var nodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'listing-videos')]//div[@class='item']");
            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    var titleNoFormatting = node.SelectSingleNode(".//span[@class='title']").InnerText;
                    var curId = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(node.SelectSingleNode(".//a").GetAttributeValue("href", string.Empty)));
                    var releaseDate = DateTime.Parse(node.SelectSingleNode(".//span[@class='date']").InnerText.Replace("th", "").Replace("st", "").Trim()).ToString("yyyy-MM-dd");

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

                    var score = 100 - LevenshteinDistance(searchDate?.ToString("yyyy-MM-dd") ?? string.Empty, releaseDate);

                    searchResults.Add(new RemoteSearchResult
                    {
                        Id = $"{curId}|{siteNum[0]}",
                        Name = $"{titleNoFormatting} [{subSite}] {releaseDate}",
                        Score = score
                    });
                }
            }

            return Task.FromResult(searchResults);
        }

        public Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var metadataId = sceneID[0].Split('|');
            var sceneUrl = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(metadataId[0]));
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = $"{BaseUrl}{sceneUrl}";
            }

            var doc = new HtmlDocument();
            var cookies = new CookieContainer();
            cookies.Add(new Cookie("warningHidden", "hide", "/", ".karups.com"));
            doc.LoadHtml(new PhoenixAdultHttpClient(cookies).Get(sceneUrl));

            var metadataResult = new MetadataResult<BaseItem>
            {
                Item = new BaseItem(),
                HasMetadata = true
            };

            metadataResult.Item.Name = doc.DocumentNode.SelectSingleNode("//h1//span[@class='title']").InnerText.Trim();
            var summaryNode = doc.DocumentNode.SelectSingleNode("//div[@class='content-information-description']//p");
            if (summaryNode != null)
            {
                metadataResult.Item.Overview = summaryNode.InnerText.Trim();
            }

            metadataResult.Item.AddStudio(SiteName);

            var tagline = doc.DocumentNode.SelectSingleNode("//h1//span[@class='sup-title']//span").InnerText.Trim();
            metadataResult.Item.Tagline = tagline;
            metadataResult.Item.AddCollection(tagline);

            var date = doc.DocumentNode.SelectSingleNode("//span[@class='date']/span[@class='content']").InnerText.Replace(tagline, "").Replace("Video added on", "").Trim();
            metadataResult.Item.PremiereDate = DateTime.Parse(date);

            if (tagline == "KarupsHA")
            {
                metadataResult.Item.AddGenre("Amateur");
            }
            else if (tagline == "KarupsOW")
            {
                metadataResult.Item.AddGenre("MILF");
            }

            var actors = doc.DocumentNode.SelectNodes("//span[@class='models']//a");
            if (actors != null)
            {
                foreach (var actor in actors)
                {
                    var actorName = actor.InnerText.Trim();
                    var actorPageUrl = actor.GetAttributeValue("href", string.Empty);
                    var actorDoc = new HtmlDocument();
                    actorDoc.LoadHtml(new PhoenixAdultHttpClient(cookies).Get(actorPageUrl));
                    var actorPhotoUrl = actorDoc.DocumentNode.SelectSingleNode("//div[@class='model-thumb']//img").GetAttributeValue("src", string.Empty);
                    metadataResult.AddPerson(new PersonInfo { Name = actorName, ImageUrl = actorPhotoUrl, Type = PersonType.Actor });
                }
            }

            return Task.FromResult(metadataResult);
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var metadataId = sceneID[0].Split('|');
            var sceneUrl = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(metadataId[0]));
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = $"{BaseUrl}{sceneUrl}";
            }

            var doc = new HtmlDocument();
            var cookies = new CookieContainer();
            cookies.Add(new Cookie("warningHidden", "hide", "/", ".karups.com"));
            doc.LoadHtml(new PhoenixAdultHttpClient(cookies).Get(sceneUrl));

            var xpaths = new[]
            {
                "//div[@class='video-player']//video/@poster",
                "//img[@class='poster']/@src",
                "//div[@class='video-thumbs']//img/@src"
            };

            var art = new List<string>();
            foreach (var xpath in xpaths)
            {
                var nodes = doc.DocumentNode.SelectNodes(xpath);
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

            return Task.FromResult<IEnumerable<RemoteImageInfo>>(list);
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
