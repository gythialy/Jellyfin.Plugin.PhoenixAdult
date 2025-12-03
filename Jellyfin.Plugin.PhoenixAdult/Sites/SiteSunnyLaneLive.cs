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
    public class SiteSunnyLaneLive : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var encodedTitle = searchTitle.Replace(" ", "+").ToLower();
            var url = Helper.GetSearchSearchURL(siteNum) + "search.php?query=" + encodedTitle;

            var http = await HTTP.Request(url, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);

                var nodes = doc.DocumentNode.SelectNodes("//div[@class='update_details']");
                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        var linkNode = node.SelectSingleNode(".//div[@class='update_title']/a");
                        var dateNode = node.SelectSingleNode(".//div[contains(text(), 'Added')]");

                        if (linkNode != null)
                        {
                            var title = linkNode.InnerText.Trim();
                            var href = linkNode.GetAttributeValue("href", "");
                            var curID = Helper.Encode(href);
                            DateTime? releaseDateObj = null;

                            if (dateNode != null)
                            {
                                var dateText = dateNode.InnerText.Replace("Added:", "").Trim();
                                if (DateTime.TryParse(dateText, out var date))
                                {
                                    releaseDateObj = date;
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
            movie.Name = doc.DocumentNode.SelectSingleNode("//div[@class='title_bar']")?.InnerText.Trim();

            var summaryNode = doc.DocumentNode.SelectSingleNode("//div[@class='video_description']");
            if (summaryNode != null)
            {
                movie.Overview = summaryNode.InnerText.Trim();
            }

            movie.AddStudio("Sunny Lane Live");
            movie.AddCollection("Sunny Lane Live");

            var infoRows = doc.DocumentNode.SelectNodes("//div[@class='gallery_info']//div[@class='table']//div[@class='row']");
            if (infoRows != null)
            {
                foreach (var row in infoRows)
                {
                    var text = row.InnerText.Trim();
                    if (text.Contains("Added:"))
                    {
                        var dateText = text.Replace("Added:", "").Trim();
                        if (DateTime.TryParse(dateText, out var date))
                        {
                            movie.PremiereDate = date;
                            movie.ProductionYear = date.Year;
                        }
                    }
                    else if (text.Contains("Tags:"))
                    {
                        var tagsText = text.Replace("Tags:", "").Trim();
                        foreach (var tag in tagsText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            movie.AddGenre(tag.Trim());
                        }
                    }
                    else if (text.Contains("Models:"))
                    {
                        var modelsText = text.Replace("Models:", "").Trim();
                         // Models might be links? Python uses text content replace.
                         // Check for links
                        var links = row.SelectNodes(".//a");
                        if (links != null)
                        {
                            foreach (var link in links)
                            {
                                result.People.Add(new PersonInfo { Name = link.InnerText.Trim(), Type = PersonKind.Actor });
                            }
                        }
                        else
                        {
                             // If no links, just split by comma? Python assumes links usually or specific parsing.
                             // Let's assume comma separated text if no links.
                             foreach (var name in modelsText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                             {
                                 result.People.Add(new PersonInfo { Name = name.Trim(), Type = PersonKind.Actor });
                             }
                        }
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
                var imgNode = doc.DocumentNode.SelectSingleNode("//div[@class='update_image']//img");
                if (imgNode != null)
                {
                    var imgUrl = imgNode.GetAttributeValue("src0_3x", "");
                    if (string.IsNullOrEmpty(imgUrl)) imgUrl = imgNode.GetAttributeValue("src0_2x", "");
                    if (string.IsNullOrEmpty(imgUrl)) imgUrl = imgNode.GetAttributeValue("src0_1x", "");
                    if (string.IsNullOrEmpty(imgUrl)) imgUrl = imgNode.GetAttributeValue("src", "");

                    if (!string.IsNullOrEmpty(imgUrl))
                    {
                         if (!imgUrl.StartsWith("http")) imgUrl = Helper.GetSearchBaseURL(siteNum) + imgUrl;
                         images.Add(new RemoteImageInfo { Url = imgUrl });
                    }
                }
            }

            return images;
        }
    }
}
