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
    public class SiteScrewMeToo : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var encodedTitle = searchTitle.Replace(" ", "+").Replace("--", "+").ToLower();
            var url = Helper.GetSearchSearchURL(siteNum) + encodedTitle;

            var http = await HTTP.Request(url, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);

                var nodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'fsp') and contains(@class, 'bor-r')]/article");
                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        var titleNode = node.SelectSingleNode(".//h4");
                        var linkNode = node.SelectSingleNode(".//a");
                        var dateNode = node.SelectSingleNode(".//div[contains(@class, 'fsdate')]/span");

                        if (titleNode != null && linkNode != null)
                        {
                            var title = titleNode.InnerText.Trim();
                            var sceneUrl = linkNode.GetAttributeValue("href", "");
                            var curID = Helper.Encode(sceneUrl);
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

            var summaryNode = doc.DocumentNode.SelectSingleNode("//div[h2]");
            if (summaryNode != null)
            {
                movie.Overview = summaryNode.InnerText.Replace("Read More ...Read Less", "").Trim();
            }

            movie.AddStudio(Helper.GetSearchSiteName(siteNum));
            movie.AddCollection(Helper.GetSearchSiteName(siteNum));

            var actors = doc.DocumentNode.SelectNodes("//a[contains(@title, 'Model Bio')]");
            if (actors != null)
            {
                if (actors.Count == 2) movie.AddGenre("Threesome");
                if (actors.Count == 3) movie.AddGenre("Foursome");
                if (actors.Count > 4) movie.AddGenre("Orgy");

                foreach (var actorLink in actors)
                {
                    var actorName = actorLink.InnerText.Trim();
                    var actorInfo = new PersonInfo { Name = actorName, Type = PersonKind.Actor };

                    var modelUrl = actorLink.GetAttributeValue("href", "");
                    if (!string.IsNullOrEmpty(modelUrl))
                    {
                        var actorHttp = await HTTP.Request(modelUrl, cancellationToken);
                        if (actorHttp.IsOK)
                        {
                            var actorDoc = new HtmlDocument();
                            actorDoc.LoadHtml(actorHttp.Content);
                            var imgNode = actorDoc.DocumentNode.SelectSingleNode("//div[@class='model-contr-colone']//img");
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

                            // Try to find date on actor page if not already found (Logic from Python)
                            // This logic is very specific and maybe fragile, but replicating:
                            // It looks for a link with 'fullTitle' and inside it a date.
                            if (movie.PremiereDate == null)
                            {
                                var fullTitleMatch = Regex.Match(sceneURL, @"(?<=content\/).*(?=\/)");
                                if (fullTitleMatch.Success)
                                {
                                    var fullTitle = fullTitleMatch.Value;
                                    var dateNode = actorDoc.DocumentNode.SelectSingleNode($"//a[contains(@href, '{fullTitle}')]//div[contains(@class, 'fsdate')]");
                                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var date))
                                    {
                                        movie.PremiereDate = date;
                                        movie.ProductionYear = date.Year;
                                    }
                                }
                            }
                        }
                    }
                    result.People.Add(actorInfo);
                }
            }

            if (movie.PremiereDate == null && !string.IsNullOrEmpty(dateFromId))
            {
                if (DateTime.TryParse(dateFromId, out var date))
                {
                    movie.PremiereDate = date;
                    movie.ProductionYear = date.Year;
                }
            }

            var genreContainer = doc.DocumentNode.SelectSingleNode("//div[@class='amp-category']");
            if (genreContainer != null)
            {
                var genres = genreContainer.InnerText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var genre in genres)
                {
                    movie.AddGenre(genre.Trim());
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
                var imgNodes = doc.DocumentNode.SelectNodes("//div[@class='amp-vis-mobile']//img");
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
                            images.Add(new RemoteImageInfo { Url = imgUrl });
                        }
                    }
                }
            }

            return images;
        }
    }
}
