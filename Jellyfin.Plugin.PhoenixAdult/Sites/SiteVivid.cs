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
using Newtonsoft.Json.Linq;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class SiteVivid : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var encodedTitle = searchTitle.Replace(" ", "+"); // Python just adds it to url string, probably default encoding or simple replace

            var sceneTypes = new[] { "videos", "dvds" };
            foreach (var sceneType in sceneTypes)
            {
                var url = Helper.GetSearchSearchURL(siteNum) + sceneType + "/api/?flagType=video&search=" + encodedTitle;
                var http = await HTTP.Request(url, cancellationToken);
                if (http.IsOK)
                {
                    try
                    {
                        var json = JObject.Parse(http.Content);
                        var responseData = json["responseData"] as JArray;
                        if (responseData != null)
                        {
                            foreach (var item in responseData)
                            {
                                var title = (string)item["name"];
                                var itemUrl = (string)item["url"];
                                var curID = Helper.Encode(itemUrl);

                                var subSite = "DVD";
                                if (item["site"] != null && item["site"]["name"] != null)
                                {
                                    subSite = (string)item["site"]["name"];
                                }

                                var releaseDateStr = (string)item["release_date"];
                                var videoBG = (string)item["placard_800"];
                                var encodedBG = Helper.Encode(videoBG);

                                curID += $"|{subSite}|{encodedBG}";

                                DateTime? releaseDateObj = null;
                                if (DateTime.TryParse(releaseDateStr, out var date))
                                {
                                    releaseDateObj = date;
                                }

                                var res = new RemoteSearchResult
                                {
                                    ProviderIds = { { Plugin.Instance.Name, curID } },
                                    Name = $"{title} [Vivid/{subSite}]",
                                    PremiereDate = releaseDateObj,
                                    SearchProviderName = Plugin.Instance.Name,
                                };

                                if (!string.IsNullOrEmpty(videoBG))
                                {
                                    res.ImageUrl = videoBG;
                                }

                                result.Add(res);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore parse errors
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
            var tagline = idParts.Length > 1 ? idParts[1] : string.Empty;
            var scenePoster = idParts.Length > 2 ? Helper.Decode(idParts[2]) : string.Empty;

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
            movie.Name = doc.DocumentNode.SelectSingleNode("//h2[@class='scene-h2-heading']")?.InnerText.Trim();

            var summaryNode = doc.DocumentNode.SelectSingleNode("//p[@class='indie-model-p']");
            if (summaryNode != null)
            {
                movie.Overview = summaryNode.InnerText.Trim();
            }

            movie.AddStudio("Vivid Entertainment");
            movie.AddCollection(tagline); // From ID (SubSite)
            if (!string.IsNullOrEmpty(tagline))
            {
                movie.Tagline = tagline;
            }

            var dateNode = doc.DocumentNode.SelectSingleNode("//h5[contains(text(), 'Released:')]");
            if (dateNode != null)
            {
                var dateText = dateNode.InnerText.Replace("Released:", string.Empty).Trim();
                if (DateTime.TryParse(dateText, out var date))
                {
                    movie.PremiereDate = date;
                    movie.ProductionYear = date.Year;
                }
            }

            var genreNodes = doc.DocumentNode.SelectNodes("//h5[contains(text(), 'Categories:')]/a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var actorNodes = doc.DocumentNode.SelectNodes("//h4[contains(text(), 'Starring:')]/a");
            if (actorNodes != null)
            {
                foreach (var actorLink in actorNodes)
                {
                    var actorName = actorLink.InnerText.Trim();
                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();

            var idParts = sceneID[0].Split('|');
            var scenePoster = idParts.Length > 2 ? Helper.Decode(idParts[2]) : string.Empty;

            if (!string.IsNullOrEmpty(scenePoster))
            {
                images.Add(new RemoteImageInfo { Url = scenePoster });
            }

            return images;
        }
    }
}
