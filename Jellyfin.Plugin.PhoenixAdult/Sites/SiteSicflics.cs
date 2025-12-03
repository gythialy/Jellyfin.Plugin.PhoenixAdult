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

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class SiteSicflics : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var encodedTitle = searchTitle.Replace(" ", "+").ToLower();
            var url = Helper.GetSearchSearchURL(siteNum) + encodedTitle + "/page1.html";

            var http = await HTTP.Request(url, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);

                var nodes = doc.DocumentNode.SelectNodes("//li[contains(@class, 'col-sm-6') and contains(@class, 'col-lg-4')]");
                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        var titleNode = node.SelectSingleNode(".//div[@class='vidtitle']/p[1]");
                        var idNode = node.SelectSingleNode(".//a[@href='#']");
                        var descNode = node.SelectSingleNode(".//div[@class='collapse']/p");
                        var dateNode = node.SelectSingleNode(".//div[@class='vidtitle']/p[2]");
                        var imgNode = node.SelectSingleNode(".//div[@class='vidthumb']/a[@class='diagrad']/img");

                        if (titleNode != null && idNode != null)
                        {
                            var title = titleNode.InnerText.Trim();
                            var sceneID = idNode.GetAttributeValue("data-movie", string.Empty);
                            var imgUrl = imgNode?.GetAttributeValue("src", string.Empty);

                            var description = string.Empty;
                            if (descNode != null)
                            {
                                var descText = descNode.InnerText;
                                var parts = descText.Split(':');
                                if (parts.Length > 1)
                                {
                                    description = parts[1].Trim();
                                }
                                else
                                {
                                    description = descText.Trim();
                                }
                            }

                            // Encode extra data in ID
                            var curID = sceneID;
                            if (!string.IsNullOrEmpty(description))
                            {
                                curID += "|" + Helper.Encode(description);
                            }
                            else
                            {
                                curID += "|";
                            }

                            if (!string.IsNullOrEmpty(imgUrl))
                            {
                                curID += "|" + Helper.Encode(imgUrl);
                            }
                            else
                            {
                                curID += "|";
                            }

                            DateTime? releaseDateObj = null;
                            if (dateNode != null)
                            {
                                if (DateTime.TryParse(dateNode.InnerText.Trim(), out var date))
                                {
                                    releaseDateObj = date;
                                }
                            }

                            var res = new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, curID } },
                                Name = title,
                                PremiereDate = releaseDateObj,
                                SearchProviderName = Plugin.Instance.Name,
                            };

                            if (!string.IsNullOrEmpty(imgUrl))
                            {
                                if (!imgUrl.StartsWith("http"))
                                {
                                    imgUrl = Helper.GetSearchBaseURL(siteNum) + imgUrl;
                                }

                                res.ImageUrl = imgUrl;
                            }

                            result.Add(res);
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
            var realSceneID = idParts[0];
            var encodedDesc = idParts.Length > 1 ? idParts[1] : string.Empty;
            var encodedImg = idParts.Length > 2 ? idParts[2] : string.Empty;

            var pageURL = Helper.GetSearchBaseURL(siteNum) + "v6/v6.pop.php?id=" + realSceneID;

            var http = await HTTP.Request(pageURL, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);

            movie.ExternalId = pageURL; // Not exact scene URL but closest thing
            movie.Name = doc.DocumentNode.SelectSingleNode("//h4[@class='red']")?.InnerText.Trim();

            var description = string.Empty;
            if (!string.IsNullOrEmpty(encodedDesc))
            {
                description = Helper.Decode(encodedDesc);
                movie.Overview = description.Replace("\n", string.Empty).Trim();
            }

            movie.AddStudio("Sicflics");
            movie.AddCollection(Helper.GetSearchSiteName(siteNum));

            var dateNode = doc.DocumentNode.SelectSingleNode("//span[@title='Date Added']");
            if (dateNode != null)
            {
                var dateText = dateNode.InnerText.Split(':').LastOrDefault()?.Trim();
                if (DateTime.TryParse(dateText, out var date))
                {
                    movie.PremiereDate = date;
                    movie.ProductionYear = date.Year;
                }
            }

            var genreNodes = doc.DocumentNode.SelectNodes("//div[@class='vidwrap']/p/a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Replace("#", string.Empty).Trim());
                }
            }

            // Actor parsing from description
            if (!string.IsNullOrEmpty(description))
            {
                // Logic: actorName = re.split(r'[\'?]', description)[1].strip()
                // C# Regex split
                var split = Regex.Split(description, @"['?]");
                if (split.Length > 1)
                {
                    var actorName = split[1].Trim();
                    if (!string.IsNullOrEmpty(actorName))
                    {
                        ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
                    }
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var idParts = sceneID[0].Split('|');
            var encodedImg = idParts.Length > 2 ? idParts[2] : string.Empty;

            if (!string.IsNullOrEmpty(encodedImg))
            {
                var imgUrl = Helper.Decode(encodedImg);
                if (!string.IsNullOrEmpty(imgUrl))
                {
                    if (!imgUrl.StartsWith("http"))
                    {
                        imgUrl = Helper.GetSearchBaseURL(siteNum) + imgUrl;
                    }

                    images.Add(new RemoteImageInfo { Url = imgUrl });
                }
            }

            return images;
        }
    }
}
