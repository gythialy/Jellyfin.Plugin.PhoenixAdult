using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class SiteSinsLife : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var encodedTitle = searchTitle.Replace(" ", "+").ToLower();
            var url = Helper.GetSearchSearchURL(siteNum) + encodedTitle;

            var http = await HTTP.Request(url, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);

                // Python xpath: //div[4]/div/div[3]/div/div
                // This is very fragile. We will try to map it, but check for nulls.
                // It seems to be targeting grid items.
                var nodes = doc.DocumentNode.SelectNodes("//div[4]/div/div[3]/div/div");
                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        var linkNode = node.SelectSingleNode(".//a");
                        if (linkNode != null)
                        {
                            var title = linkNode.GetAttributeValue("title", string.Empty).Trim();
                            var href = linkNode.GetAttributeValue("href", string.Empty);
                            if (string.IsNullOrEmpty(title))
                            {
                                title = linkNode.InnerText.Trim();
                            }

                            var curID = Helper.Encode(href);

                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, curID } },
                                Name = title,
                                SearchProviderName = Plugin.Instance.Name,
                            });
                        }
                    }
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
            var movie = (Movie)result.Item;
            var sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = "http:" + sceneURL;
            }

            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);

            movie.ExternalId = sceneURL;
            movie.Name = doc.DocumentNode.SelectSingleNode("//div[@class='section']//h1")?.InnerText.Trim();

            var summaryNode = doc.DocumentNode.SelectSingleNode("//div[4]/div/div[2]/div/div/div[2]/div[2]/p");
            if (summaryNode != null)
            {
                movie.Overview = summaryNode.InnerText.Trim();
            }

            movie.AddStudio("SinsLife");
            movie.AddCollection(Helper.GetSearchSiteName(siteNum));

            var dateNode = doc.DocumentNode.SelectSingleNode("//div[4]/div/div[2]/div/div/div[2]/div[1]/div/div[1]");
            if (dateNode != null)
            {
                var dateText = dateNode.InnerText.Replace("Release Date:", string.Empty).Trim();

                // Format: %B %d, %Y => "December 31, 2020"
                if (DateTime.TryParseExact(dateText, "MMMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    movie.PremiereDate = date;
                    movie.ProductionYear = date.Year;
                }
                else if (DateTime.TryParse(dateText, out var date2))
                {
                    movie.PremiereDate = date2;
                    movie.ProductionYear = date2.Year;
                }
            }

            var actorNodes = doc.DocumentNode.SelectNodes("//div[4]/div/div[2]/div/div/div[2]/div[3]/ul/li/a");
            if (actorNodes != null)
            {
                if (actorNodes.Count == 3)
                {
                    movie.AddGenre("Threesome");
                }

                if (actorNodes.Count == 4)
                {
                    movie.AddGenre("Foursome");
                }

                if (actorNodes.Count > 4)
                {
                    movie.AddGenre("Orgy");
                }

                foreach (var actorLink in actorNodes)
                {
                    var actorName = actorLink.InnerText.Trim();
                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = "http:" + sceneURL;
            }

            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                var imgNode = doc.DocumentNode.SelectSingleNode("//div[4]/div/div[2]/div/div/div[1]/div[2]/div/div[1]/img");
                if (imgNode != null)
                {
                    var imgUrl = imgNode.GetAttributeValue("src", string.Empty);
                    if (!string.IsNullOrEmpty(imgUrl))
                    {
                        if (imgUrl.StartsWith("//"))
                        {
                            imgUrl = "http:" + imgUrl;
                        }

                        images.Add(new RemoteImageInfo { Url = imgUrl });
                    }
                }
            }

            return images;
        }
    }
}
