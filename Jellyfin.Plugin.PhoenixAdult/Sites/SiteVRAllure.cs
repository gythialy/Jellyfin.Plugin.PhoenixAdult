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

namespace PhoenixAdult.Sites
{
    public class SiteVRAllure : IProviderBase
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

                var nodes = doc.DocumentNode.SelectNodes("//div[@class='item-video']");
                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        var infoNode = node.SelectSingleNode(".//div[@class='item-info']//a");
                        var linkNode = node.SelectSingleNode(".//a");
                        var dateNode = node.SelectSingleNode(".//div[@class='date']");

                        if (infoNode != null && linkNode != null)
                        {
                            var title = infoNode.InnerText.Trim();
                            var href = linkNode.GetAttributeValue("href", "");
                            var curID = Helper.Encode(href);
                            DateTime? releaseDateObj = null;
                            string dateStr = "";

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
                                Name = title,
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
            movie.Name = doc.DocumentNode.SelectSingleNode("//h1")?.InnerText.Trim();

            var summaryNode = doc.DocumentNode.SelectSingleNode("//div[@class='description']//p");
            if (summaryNode != null)
            {
                movie.Overview = summaryNode.InnerText.Trim();
            }

            movie.AddStudio("VRAllure");
            movie.AddCollection("VRAllure");

            var dateNode = doc.DocumentNode.SelectSingleNode("//div[@class='date']");
            if (dateNode != null)
            {
                if (DateTime.TryParse(dateNode.InnerText.Trim(), out var date))
                {
                    movie.PremiereDate = date;
                    movie.ProductionYear = date.Year;
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

            var genreNodes = doc.DocumentNode.SelectNodes("//div[@class='tags']//a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var actorNodes = doc.DocumentNode.SelectNodes("//div[@class='models']//a");
            if (actorNodes != null)
            {
                foreach (var actorLink in actorNodes)
                {
                    var actorName = actorLink.InnerText.Trim();
                    var actorInfo = new PersonInfo { Name = actorName, Type = PersonKind.Actor };

                    var actorHref = actorLink.GetAttributeValue("href", "");
                    if (!string.IsNullOrEmpty(actorHref))
                    {
                        var actorHttp = await HTTP.Request(actorHref, cancellationToken);
                        if (actorHttp.IsOK)
                        {
                            var actorDoc = new HtmlDocument();
                            actorDoc.LoadHtml(actorHttp.Content);
                            var styleNode = actorDoc.DocumentNode.SelectSingleNode("//div[@class='model-img']");
                            if (styleNode != null)
                            {
                                var style = styleNode.GetAttributeValue("style", "");
                                var match = Regex.Match(style, @"url\((.*?)\)");
                                if (match.Success)
                                {
                                    var imgUrl = match.Groups[1].Value.Replace("'", "").Replace("\"", "");
                                    if (!string.IsNullOrEmpty(imgUrl))
                                    {
                                        if (!imgUrl.StartsWith("http")) imgUrl = Helper.GetSearchBaseURL(siteNum) + imgUrl;
                                        actorInfo.ImageUrl = imgUrl;
                                    }
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
                var playerNode = doc.DocumentNode.SelectSingleNode("//div[@id='trailer-player']");
                if (playerNode != null)
                {
                    var style = playerNode.GetAttributeValue("style", "");
                    var match = Regex.Match(style, @"url\((.*?)\)");
                    if (match.Success)
                    {
                        var imgUrl = match.Groups[1].Value.Replace("'", "").Replace("\"", "");
                        if (!string.IsNullOrEmpty(imgUrl))
                        {
                            if (!imgUrl.StartsWith("http"))
                            {
                                imgUrl = Helper.GetSearchBaseURL(siteNum) + imgUrl;
                            }
                            images.Add(new RemoteImageInfo { Url = imgUrl });
                        }
                    }
                }
            }

            return images;
        }
    }
}
