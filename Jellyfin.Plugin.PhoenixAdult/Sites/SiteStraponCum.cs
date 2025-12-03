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
    public class SiteStraponCum : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var encodedTitle = searchTitle.Replace(" ", "-").ToLower();
            var url = Helper.GetSearchSearchURL(siteNum) + encodedTitle + ".html";

            var http = await HTTP.Request(url, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);

                var nodes = doc.DocumentNode.SelectNodes("//div[@class='card']");
                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        var titleNode = node.SelectSingleNode(".//h1[@class='card-title']");

                        if (titleNode != null)
                        {
                            var title = titleNode.InnerText.Trim();
                            var curID = Helper.Encode(url);
                            DateTime? releaseDateObj = null;

                            // Date logic: //i[contains(@class, "fa-clock")]/following-sibling::text() -> split '•'
                            var clockNode = node.SelectSingleNode(".//i[contains(@class, 'fa-clock')]");
                            if (clockNode != null && clockNode.NextSibling != null)
                            {
                                var dateText = clockNode.NextSibling.InnerText;
                                var parts = dateText.Split('•');
                                if (parts.Length > 1)
                                {
                                    if (DateTime.TryParse(parts[1].Trim(), out var date))
                                    {
                                        releaseDateObj = date;
                                    }
                                }
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
            movie.Name = doc.DocumentNode.SelectSingleNode("//h1[@class='card-title']")?.InnerText.Trim();

            var summaryNode = doc.DocumentNode.SelectSingleNode("//p[@class='card-text mb-2']");
            if (summaryNode != null)
            {
                movie.Overview = summaryNode.InnerText.Trim();
            }

            movie.AddStudio("Strapon Cum");
            movie.AddCollection("Strapon Cum");

            // Date
            var clockNode = doc.DocumentNode.SelectSingleNode("//div[@class='card']//i[contains(@class, 'fa-clock')]");
            if (clockNode != null && clockNode.NextSibling != null)
            {
                var dateText = clockNode.NextSibling.InnerText.Split('•').LastOrDefault()?.Trim();
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

            movie.AddGenre("Strap-On");
            movie.AddGenre("Lesbian");

            var genreNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'tag-cloud')]//a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var actorNodes = doc.DocumentNode.SelectNodes("//div[@class='card']//span[contains(text(), 'Featuring:')]/following-sibling::a");
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
                    var actorInfo = new PersonInfo { Name = actorName, Type = PersonKind.Actor };

                    var actorHref = actorLink.GetAttributeValue("href", string.Empty);
                    if (!string.IsNullOrEmpty(actorHref))
                    {
                        var actorHttp = await HTTP.Request(actorHref, cancellationToken);
                        if (actorHttp.IsOK)
                        {
                            var actorDoc = new HtmlDocument();
                            actorDoc.LoadHtml(actorHttp.Content);
                            var imgNode = actorDoc.DocumentNode.SelectSingleNode("//img[starts-with(@id, 'set-target')]");
                            if (imgNode != null)
                            {
                                var imgUrl = imgNode.GetAttributeValue("data-src0_1x", string.Empty);
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
                var imgNode = doc.DocumentNode.SelectSingleNode("//div[@class='trailer']//img");
                if (imgNode != null)
                {
                    var alt = imgNode.GetAttributeValue("alt", string.Empty);
                    if (!string.IsNullOrEmpty(alt))
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            var imgUrl = $"{Helper.GetSearchBaseURL(siteNum)}/content/{alt}/{i}.jpg";
                            images.Add(new RemoteImageInfo { Url = imgUrl });
                        }
                    }
                }
            }

            return images;
        }
    }
}
