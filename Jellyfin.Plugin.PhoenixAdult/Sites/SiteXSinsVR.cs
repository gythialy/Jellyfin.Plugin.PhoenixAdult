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
    public class SiteXSinsVR : IProviderBase
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

                var nodes = doc.DocumentNode.SelectNodes("//div[@class='tn-video tn-video--horizontal']");
                if (nodes != null)
                    foreach (var node in nodes)
                    {
                        var infoNode = node.SelectSingleNode(".//div/a[@class='tn-video-name']");
                        var linkNode = node.SelectSingleNode(".//a[@class='tn-video-media']");
                        var actorNodes = node.SelectNodes(".//div/div[@class='tn-video-models']/a");

                        if (infoNode != null && linkNode != null)
                        {
                            var title = infoNode.InnerText.Trim();
                            var href = linkNode.GetAttributeValue("href", string.Empty);
                            if (!href.StartsWith("http"))
                            {
                                href = Helper.GetSearchBaseURL(siteNum) + href;
                            }
                            var curID = Helper.Encode(href);

                            var actorsList = new List<string>();
                            if (actorNodes != null)
                            {
                                foreach (var actor in actorNodes)
                                {
                                    actorsList.Add(actor.InnerText.Trim());
                                }
                            }
                            string actors = string.Join(", ", actorsList);

                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, curID } },
                                Name = $"{title} [{Helper.GetSearchSiteName(siteNum)}] with {actors}",
                                SearchProviderName = Plugin.Instance.Name,
                            });
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
            var titleNode = doc.DocumentNode.SelectSingleNode("//title");
            if (titleNode != null)
            {
                movie.Name = titleNode.InnerText.Trim().Split('•')[0].Trim();
            }

            var summaryNodes = doc.DocumentNode.SelectNodes("//li/div[@class='small']/p");
            if (summaryNodes != null)
            {
                string summary = string.Empty;
                foreach (var paragraph in summaryNodes)
                {
                    summary += paragraph.InnerText.Trim() + " ";
                }
                movie.Overview = summary.Trim();
            }

            var studio = Helper.GetSearchSiteName(siteNum);
            movie.AddStudio(studio);
            movie.AddCollection(studio);

            var dateNode = doc.DocumentNode.SelectSingleNode("//span//time");
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

            var genreNodes = doc.DocumentNode.SelectNodes("//div[@class='tags-item']");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var actorNodes = doc.DocumentNode.SelectNodes("//div/strong[contains(., 'Starring')]//following-sibling::span/a[@class='tiny-link']");
            if (actorNodes != null)
            {
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
                            var imgNode = actorDoc.DocumentNode.SelectSingleNode("//div[@class='model-header__photo']/img");
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

                var imgNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'tn-photo__container')]/div/a/div/img");
                if (imgNodes != null)
                {
                    foreach (var node in imgNodes)
                    {
                        var src = node.GetAttributeValue("src", string.Empty);
                        if (!string.IsNullOrEmpty(src))
                        {
                            var img = src.Replace("sceneGallerySmall", "sceneGallery");
                            images.Add(new RemoteImageInfo { Url = img });
                        }
                    }
                }

                var posterNode = doc.DocumentNode.SelectSingleNode("//dl8-video");
                if (posterNode != null)
                {
                    var posterUrl = posterNode.GetAttributeValue("poster", string.Empty);
                    if (!string.IsNullOrEmpty(posterUrl))
                    {
                        images.Add(new RemoteImageInfo { Url = posterUrl });
                    }
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
