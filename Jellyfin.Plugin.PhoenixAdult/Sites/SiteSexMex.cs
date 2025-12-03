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
    public class SiteSexMex : IProviderBase
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

                var nodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'thumbnail')]");
                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        var titleNode = node.SelectSingleNode(".//h5");
                        var linkNode = node.SelectSingleNode(".//a");
                        var dateNode = node.SelectSingleNode(".//p[@class='scene-date']");

                        if (titleNode != null && linkNode != null)
                        {
                            var title = Helper.ParseTitle(titleNode.InnerText.Trim(), siteNum);
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
            var titleNode = doc.DocumentNode.SelectSingleNode("//h4");
            if (titleNode != null)
            {
                movie.Name = Helper.ParseTitle(titleNode.InnerText.Trim(), siteNum);
            }

            var summaryNode = doc.DocumentNode.SelectSingleNode("//div[@class='panel-body']/p");
            if (summaryNode != null)
            {
                movie.Overview = summaryNode.InnerText.Trim();
            }

            movie.AddStudio(Helper.GetSearchSiteName(siteNum));
            movie.AddCollection(Helper.GetSearchSiteName(siteNum));

            if (!string.IsNullOrEmpty(dateFromId) && DateTime.TryParse(dateFromId, out var date))
            {
                movie.PremiereDate = date;
                movie.ProductionYear = date.Year;
            }

            var keywords = doc.DocumentNode.SelectSingleNode("//meta[@name='keywords']");
            if (keywords != null)
            {
                var content = keywords.GetAttributeValue("content", "");
                foreach (var genre in content.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    movie.AddGenre(genre.Trim());
                }
            }

            var actorNodes = doc.DocumentNode.SelectNodes("//p[@class]/a");
            if (actorNodes != null)
            {
                foreach (var actorLink in actorNodes)
                {
                    var actorName = actorLink.InnerText.Trim();
                    var actorInfo = new PersonInfo { Name = actorName, Type = PersonKind.Actor };

                    var href = actorLink.GetAttributeValue("href", "");
                    var modelUrl = $"{Helper.GetSearchBaseURL(siteNum)}/tour/{href}";

                    var actorHttp = await HTTP.Request(modelUrl, cancellationToken);
                    if (actorHttp.IsOK)
                    {
                        var actorDoc = new HtmlDocument();
                        actorDoc.LoadHtml(actorHttp.Content);
                        var imgNode = actorDoc.DocumentNode.SelectSingleNode("//img");
                        if (imgNode != null)
                        {
                            var imgUrl = imgNode.GetAttributeValue("src", "");
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

                    result.People.Add(actorInfo);
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var idParts = sceneID[0].Split('|');
            var sceneURL = Helper.Decode(idParts[0]);
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                var imgNodes = doc.DocumentNode.SelectNodes("//div[@class='thumbnail']//img");
                if (imgNodes != null)
                {
                    foreach (var img in imgNodes)
                    {
                        var imgUrl = img.GetAttributeValue("src", "");
                        if (!string.IsNullOrEmpty(imgUrl))
                        {
                            if (!imgUrl.StartsWith("http"))
                            {
                                imgUrl = Helper.GetSearchBaseURL(siteNum) + imgUrl;
                            }
                            imgUrl = imgUrl.Split('?')[0];
                            images.Add(new RemoteImageInfo { Url = imgUrl });
                        }
                    }
                }

                var poster = doc.DocumentNode.SelectSingleNode("//video");
                if (poster != null)
                {
                     var posterUrl = poster.GetAttributeValue("poster", "");
                     if (!string.IsNullOrEmpty(posterUrl))
                     {
                        if (!posterUrl.StartsWith("http"))
                        {
                             posterUrl = Helper.GetSearchBaseURL(siteNum) + posterUrl;
                        }
                        posterUrl = posterUrl.Split('?')[0];
                        images.Add(new RemoteImageInfo { Url = posterUrl });
                     }
                }
            }

            return images;
        }
    }
}
