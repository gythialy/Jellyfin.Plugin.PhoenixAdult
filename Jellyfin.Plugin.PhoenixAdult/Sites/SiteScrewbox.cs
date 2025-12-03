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

namespace PhoenixAdult.Sites
{
    public class SiteScrewbox : IProviderBase
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

                var nodes = doc.DocumentNode.SelectNodes("//div[@class='item']");
                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        var titleNode = node.SelectSingleNode(".//h4//a");
                        var linkNode = node.SelectSingleNode(".//a");

                        if (titleNode != null && linkNode != null)
                        {
                            var title = titleNode.InnerText.Trim();
                            var href = linkNode.GetAttributeValue("href", "");
                            if (href.StartsWith("//")) href = "https:" + href;
                            else if (href.StartsWith("/")) href = Helper.GetSearchBaseURL(siteNum) + href;

                            var curID = Helper.Encode(href);

                            // Try to get actors for display
                            var actorNodes = node.SelectNodes(".//div[@class='item-featured']//a");
                            var actorDisplay = "";
                            if (actorNodes != null && actorNodes.Count > 0)
                            {
                                actorDisplay = actorNodes[0].InnerText.Trim();
                            }

                            var displayName = title;
                            if (!string.IsNullOrEmpty(actorDisplay))
                            {
                                displayName = $"{actorDisplay} in {title}";
                            }

                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, curID } },
                                Name = displayName,
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
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);

            movie.ExternalId = sceneURL;
            movie.Name = doc.DocumentNode.SelectSingleNode("//div[@class='item-details-right']//h1")?.InnerText.Trim();

            var summaryNode = doc.DocumentNode.SelectSingleNode("//p[@class='shorter']");
            if (summaryNode != null)
            {
                movie.Overview = summaryNode.InnerText.Trim();
            }

            movie.AddStudio("Screwbox");
            movie.AddCollection("Screwbox");

            var dateNode = doc.DocumentNode.SelectSingleNode("//ul[@class='more-info']//li[2]");
            if (dateNode != null)
            {
                var dateText = dateNode.InnerText.Replace("RELEASE DATE:", "").Trim();
                if (DateTime.TryParse(dateText, out var date))
                {
                    movie.PremiereDate = date;
                    movie.ProductionYear = date.Year;
                }
            }

            var genreNodes = doc.DocumentNode.SelectNodes("//ul[@class='more-info']//li[3]//a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var actorNodes = doc.DocumentNode.SelectNodes("//ul[@class='more-info']//li[1]//a");
            if (actorNodes != null)
            {
                foreach (var actorLink in actorNodes)
                {
                    var actorName = actorLink.InnerText.Trim();
                    var actorInfo = new PersonInfo { Name = actorName, Type = PersonKind.Actor };

                    var actorHref = actorLink.GetAttributeValue("href", "");
                    if (actorHref.StartsWith("//")) actorHref = "http:" + actorHref;

                    if (!string.IsNullOrEmpty(actorHref))
                    {
                        var actorHttp = await HTTP.Request(actorHref, cancellationToken);
                        if (actorHttp.IsOK)
                        {
                            var actorDoc = new HtmlDocument();
                            actorDoc.LoadHtml(actorHttp.Content);
                            var imgNode = actorDoc.DocumentNode.SelectSingleNode("//img[contains(@class, 'model_bio_thumb')]");
                            if (imgNode != null)
                            {
                                var imgUrl = imgNode.GetAttributeValue("src0_1x", "");
                                if (!string.IsNullOrEmpty(imgUrl))
                                {
                                    if (imgUrl.StartsWith("//")) imgUrl = "http:" + imgUrl;
                                    actorInfo.ImageUrl = imgUrl;
                                }
                            }
                        }
                    }

                    result.People.Add(actorInfo);
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
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                var imgNode = doc.DocumentNode.SelectSingleNode("//div[@class='fakeplayer']//img");
                if (imgNode != null)
                {
                    var imgUrl = imgNode.GetAttributeValue("src0_1x", "");
                    if (!string.IsNullOrEmpty(imgUrl))
                    {
                        if (imgUrl.StartsWith("//")) imgUrl = "http:" + imgUrl;
                        images.Add(new RemoteImageInfo { Url = imgUrl });
                    }
                }
            }

            return images;
        }
    }
}
