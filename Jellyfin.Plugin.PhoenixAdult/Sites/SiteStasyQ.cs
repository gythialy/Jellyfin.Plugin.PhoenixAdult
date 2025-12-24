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
    public class SiteStasyQ : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var encodedTitle = searchTitle.Replace(" ", "+").ToLower();
            var url = Helper.GetSearchSearchURL(siteNum) + "search/" + encodedTitle;

            var http = await HTTP.Request(url, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);

                var nodes = doc.DocumentNode.SelectNodes("//div[@class='row']//div[contains(@class, 'col-12') and contains(@class, 'col-sm-6') and contains(@class, 'col-lg-4')]");
                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        var linkNode = node.SelectSingleNode(".//a[contains(@class, 'sec-tit')]");
                        var titleNode = linkNode?.SelectSingleNode("span");

                        if (linkNode != null)
                        {
                            var title = titleNode?.InnerText.Trim() ?? linkNode.InnerText.Trim();
                            var href = linkNode.GetAttributeValue("href", string.Empty);
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
            movie.Name = doc.DocumentNode.SelectSingleNode("//h2[@class='sec-tit']")?.InnerText.Trim();

            var summaryNode = doc.DocumentNode.SelectSingleNode("//div[@class='sh-para']/p");
            if (summaryNode != null)
            {
                movie.Overview = summaryNode.InnerText.Trim();
            }

            movie.AddStudio("StasyQ");
            movie.AddCollection("StasyQ");

            var dateNode = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'row')]//div[contains(@class, 'col-12') and contains(@class, 'col-md-6') and contains(@class, 'text-right')]/p");
            if (dateNode != null)
            {
                var dateText = dateNode.InnerText.Replace("Release date:", string.Empty).Trim();
                if (DateTime.TryParse(dateText, out var date))
                {
                    movie.PremiereDate = date;
                    movie.ProductionYear = date.Year;
                }
            }

            var tagsHeader = doc.DocumentNode.SelectNodes("//h5[contains(@class, 'sec-tit')]")
                ?.FirstOrDefault(n => n.InnerText.Contains("Tags"));
            if (tagsHeader != null)
            {
                var genreNodes = tagsHeader.ParentNode.SelectNodes(".//div//a");
                if (genreNodes != null)
                {
                    foreach (var genre in genreNodes)
                    {
                        movie.AddGenre(genre.InnerText.Trim());
                    }
                }
            }

            var modelsHeader = doc.DocumentNode.SelectNodes("//h5[contains(@class, 'sec-tit')]")
                ?.FirstOrDefault(n => n.InnerText.Contains("Models"));

            if (modelsHeader != null)
            {
                var actorNodes = modelsHeader.ParentNode.SelectNodes(".//div//a");
                if (actorNodes != null)
                {
                    foreach (var actorLink in actorNodes)
                    {
                        var actorName = actorLink.InnerText.Trim();
                        var actorInfo = new PersonInfo { Name = actorName, Type = PersonKind.Actor };

                        var actorHref = actorLink.GetAttributeValue("href", string.Empty);
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
                                    var style = styleNode.GetAttributeValue("style", string.Empty);
                                    var match = Regex.Match(style, @"url\((.*?)\)");
                                    if (match.Success)
                                    {
                                        var imgUrl = match.Groups[1].Value.Replace("'", string.Empty).Replace("\"", string.Empty);
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
                        }

                        ((List<PersonInfo>)result.People).Add(actorInfo);
                    }
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
                var imgNodes = doc.DocumentNode.SelectNodes("//div[@class='sh-section']//img");
                if (imgNodes != null)
                {
                    foreach (var img in imgNodes)
                    {
                        var imgUrl = img.GetAttributeValue("src", string.Empty);
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
