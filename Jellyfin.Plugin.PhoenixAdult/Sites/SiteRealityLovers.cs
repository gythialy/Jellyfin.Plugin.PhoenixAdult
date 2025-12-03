using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
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

namespace PhoenixAdult.Sites
{
    public class SiteRealityLovers : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var searchUrl = Helper.GetSearchSearchURL(siteNum);

            var jsonBody = new JObject
            {
                { "sortBy", "MOST_RELEVANT" },
                { "searchQuery", searchTitle },
                { "videoView", "MEDIUM" }
            };

            var content = new StringContent(jsonBody.ToString(), Encoding.UTF8, "application/json");
            var http = await HTTP.Request(searchUrl, HttpMethod.Post, content, cancellationToken);

            if (http.IsOK)
            {
                var json = JObject.Parse(http.Content);
                var contents = json["contents"] as JArray;
                if (contents != null)
                {
                    foreach (var item in contents)
                    {
                        var title = (string)item["title"];
                        var released = (string)item["released"];
                        var videoUri = (string)item["videoUri"];
                        var mainImageSrcset = (string)item["mainImageSrcset"];

                        var curID = Helper.Encode(videoUri);
                        var siteName = Helper.GetSearchSiteName(siteNum);

                        DateTime? releaseDateObj = null;
                        if (DateTime.TryParse(released, out var date))
                        {
                            releaseDateObj = date;
                        }

                        var res = new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, curID } },
                            Name = title,
                            PremiereDate = releaseDateObj,
                            SearchProviderName = Plugin.Instance.Name,
                        };

                        if (!string.IsNullOrEmpty(mainImageSrcset))
                        {
                            var parts = mainImageSrcset.Split(',');
                            if (parts.Length > 1)
                            {
                                var imgUrl = parts[1].Trim().Split(' ')[0];
                                if (imgUrl.StartsWith("https"))
                                {
                                    imgUrl = imgUrl.Replace("https", "http");
                                }
                                res.ImageUrl = imgUrl;
                            }
                        }

                        result.Add(res);
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
                sceneURL = Helper.GetSearchBaseURL(siteNum) + "/" + sceneURL;
            }

            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);

            movie.ExternalId = sceneURL;
            movie.Name = doc.DocumentNode.SelectSingleNode("//h1[@class='video-detail-name']")?.InnerText.Trim();

            var summaryNode = doc.DocumentNode.SelectSingleNode("//p[@itemprop='description']");
            if (summaryNode != null)
            {
                var summary = summaryNode.InnerText.Replace("…", "").Replace("Read more", "").Trim();
                movie.Overview = summary;
            }

            movie.AddStudio(Helper.GetSearchSiteName(siteNum));
            movie.AddCollection(Helper.GetSearchSiteName(siteNum));

            var dateNode = doc.DocumentNode.SelectSingleNode("//span[@class='videoClip__Details-infoValue']");
            if (dateNode != null)
            {
                if (DateTime.TryParse(dateNode.InnerText.Trim(), out var date))
                {
                    movie.PremiereDate = date;
                    movie.ProductionYear = date.Year;
                }
            }

            var genreNodes = doc.DocumentNode.SelectNodes("//span[@itemprop='keywords']/a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var actorNodes = doc.DocumentNode.SelectNodes("//span[@itemprop='actors']/a");
            if (actorNodes != null)
            {
                foreach (var actorLink in actorNodes)
                {
                    var actorName = actorLink.InnerText.Trim();
                    var actorInfo = new PersonInfo { Name = actorName, Type = PersonKind.Actor };

                    var actorPageURL = actorLink.GetAttributeValue("href", "");
                    if (!string.IsNullOrEmpty(actorPageURL))
                    {
                        if (!actorPageURL.StartsWith("http"))
                        {
                            actorPageURL = new Uri(new Uri(Helper.GetSearchBaseURL(siteNum)), actorPageURL).ToString();
                        }

                        var actorHttp = await HTTP.Request(actorPageURL, cancellationToken);
                        if (actorHttp.IsOK)
                        {
                            var actorDoc = new HtmlDocument();
                            actorDoc.LoadHtml(actorHttp.Content);
                            var actorPhotoNode = actorDoc.DocumentNode.SelectSingleNode("//img[@class='girlDetails-posterImage']");
                            if (actorPhotoNode != null)
                            {
                                var srcset = actorPhotoNode.GetAttributeValue("srcset", "");
                                if (!string.IsNullOrEmpty(srcset))
                                {
                                    var parts = srcset.Split(',');
                                    if (parts.Length > 1)
                                    {
                                        var imgUrl = parts[1].Trim().Split(' ')[0];
                                        if (imgUrl.StartsWith("https"))
                                        {
                                            imgUrl = imgUrl.Replace("https", "http");
                                        }
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
                sceneURL = Helper.GetSearchBaseURL(siteNum) + "/" + sceneURL;
            }

            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                var photoNodes = doc.DocumentNode.SelectNodes("//img[contains(@class, 'videoClip__Details--galleryItem')]");
                if (photoNodes != null)
                {
                    foreach (var photo in photoNodes)
                    {
                        var photoUrl = photo.GetAttributeValue("data-big", "");
                        if (!string.IsNullOrEmpty(photoUrl))
                        {
                            var parts = photoUrl.Split(',');
                            if (parts.Length > 0)
                            {
                                var imgUrl = parts[parts.Length - 1].Trim().Split(' ')[0];
                                if (imgUrl.StartsWith("https"))
                                {
                                    imgUrl = imgUrl.Replace("https", "http");
                                }
                                images.Add(new RemoteImageInfo { Url = imgUrl });
                            }
                        }
                    }
                }
            }

            return images;
        }
    }
}
