using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
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
using Jellyfin.Data.Enums;

namespace PhoenixAdult.Sites
{
    public class SiteVIPissy : IProviderBase
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

                var nodes = doc.DocumentNode.SelectNodes("//div[@style='position:relative; background:black;']");
                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        var linkNode = node.SelectSingleNode(".//a");
                        var dateNode = node.SelectSingleNode(".//span[@class='date']");

                        if (linkNode != null)
                        {
                            var title = linkNode.GetAttributeValue("title", string.Empty).Trim();
                            var href = linkNode.GetAttributeValue("href", string.Empty);
                            var curID = Helper.Encode(href);
                            DateTime? releaseDateObj = null;
                            string dateStr = string.Empty;

                            if (dateNode != null)
                            {
                                dateStr = dateNode.InnerText.Trim();
                                if (DateTime.TryParse(dateStr, out var date))
                                {
                                    releaseDateObj = date;
                                }
                            }

                            if (!string.IsNullOrEmpty(dateStr))
                            {
                                curID += "|" + dateStr;
                            }

                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, curID } },
                                Name = $"{title} [VIPissy] {releaseDateObj?.ToString("yyyy-MM-dd")}",
                                PremiereDate = releaseDateObj,
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
            var idParts = sceneID[0].Split('|');
            var sceneURL = Helper.Decode(idParts[0]);
            var dateFromId = idParts.Length > 1 ? idParts[1] : null;

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
            movie.Name = doc.DocumentNode.SelectSingleNode("//section[@class='downloads']/strong")?.InnerText.Trim();

            var summaryNode = doc.DocumentNode.SelectSingleNode("//div/section[4]/div");
            var tagsSummaryNode = doc.DocumentNode.SelectSingleNode("//div/section[4]/div/p");
            if (summaryNode != null)
            {
                string allSummary = summaryNode.InnerText.Trim();
                string tagsSummary = tagsSummaryNode != null ? tagsSummaryNode.InnerText.Trim() : string.Empty;
                string summary = allSummary.Replace(tagsSummary, string.Empty).Split(new[] { "Show more..." }, StringSplitOptions.None)[0].Trim();
                movie.Overview = summary;
            }

            movie.AddStudio("VIPissy");
            movie.AddCollection("VIPissy");

            var dateNode = doc.DocumentNode.SelectSingleNode("//div/section[2]/dl/dd[2]");
            if (dateNode != null)
            {
                if (DateTime.TryParseExact(dateNode.InnerText.Trim(), "MMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    movie.PremiereDate = date;
                    movie.ProductionYear = date.Year;
                }
                else if (DateTime.TryParse(dateNode.InnerText.Trim(), out var date2))
                {
                    movie.PremiereDate = date2;
                    movie.ProductionYear = date2.Year;
                }
            }
            else if (!string.IsNullOrEmpty(dateFromId))
            {
                if (DateTime.TryParse(dateFromId, out var date))
                {
                    movie.PremiereDate = date;
                    movie.ProductionYear = date.Year;
                }
            }

            var genreNodes = doc.DocumentNode.SelectNodes("//div/section[4]/div/p/a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim().ToLowerInvariant());
                }
            }

            var actorNodes = doc.DocumentNode.SelectNodes("//div/section[2]/dl/dd[1]/a");
            if (actorNodes != null)
            {
                if (actorNodes.Count == 3)
                {
                    movie.AddGenre("Threesome");
                }
                else if (actorNodes.Count == 4)
                {
                    movie.AddGenre("Foursome");
                }
                else if (actorNodes.Count > 4)
                {
                    movie.AddGenre("Orgy");
                }

                foreach (var actorLink in actorNodes)
                {
                    var actorName = actorLink.InnerText.Trim();
                    var actorInfo = new PersonInfo { Name = actorName, Type = PersonKind.Actor };

                    var actorHref = actorLink.GetAttributeValue("href", string.Empty);
                    if (!string.IsNullOrEmpty(actorHref))
                    {
                        if (!actorHref.StartsWith("http"))
                        {
                            actorHref = Helper.GetSearchBaseURL(siteNum) + actorHref;
                        }

                        var actorHttp = await HTTP.Request(actorHref, cancellationToken);
                        if (actorHttp.IsOK)
                        {
                            var actorDoc = new HtmlDocument();
                            actorDoc.LoadHtml(actorHttp.Content);
                            var imgNode = actorDoc.DocumentNode.SelectSingleNode("//div/section[1]/div/div[1]/img");
                            if (imgNode != null)
                            {
                                var imgUrl = imgNode.GetAttributeValue("src", string.Empty);
                                if (!string.IsNullOrEmpty(imgUrl))
                                {
                                    if (!imgUrl.StartsWith("http"))
                                    {
                                        imgUrl = Helper.GetSearchBaseURL(siteNum) + imgUrl;
                                    }

                                    actorInfo.ImageUrl = imgUrl;
                                }
                            }
                        }
                    }

                    ((List<PersonInfo>)result.People).Add(actorInfo);
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var sceneURL = Helper.Decode(sceneID[0].Split('|')[0]);
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);

                var imgNodes = doc.DocumentNode.SelectNodes("//div[contains(@id, 'pics2')]//div//ul//li//div//div//img");
                if (imgNodes != null)
                {
                    foreach (var node in imgNodes)
                    {
                        var src = node.GetAttributeValue("src", string.Empty);
                        if (!string.IsNullOrEmpty(src))
                        {
                            images.Add(new RemoteImageInfo { Url = src });
                        }
                    }
                }

                try
                {
                    var updatesParts = sceneURL.Split(new[] { "/updates" }, StringSplitOptions.None);
                    if (updatesParts.Length > 1)
                    {
                        var twitterBG = $"https://media.vipissy.com/videos{updatesParts[1]}cover/l.jpg";
                        images.Insert(0, new RemoteImageInfo { Url = twitterBG });
                    }
                }
                catch
                {
                    // ignored
                }
            }

            if (images.Any())
            {
                images.First().Type = ImageType.Primary;
                foreach (var image in images.Skip(1))
                {
                    image.Type = ImageType.Backdrop;
                }
            }

            return images;
        }
    }
}
