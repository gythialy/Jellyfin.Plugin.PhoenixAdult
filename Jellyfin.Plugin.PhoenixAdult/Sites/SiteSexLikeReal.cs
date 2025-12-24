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
using Jellyfin.Data.Enums;

namespace PhoenixAdult.Sites
{
    public class SiteSexLikeReal : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var searchResults = new HashSet<string>();

            var directURL = Helper.GetSearchSearchURL(siteNum) + searchTitle.Replace(" ", "-").ToLower();
            searchResults.Add(directURL);

            var googleResults = await WebSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            foreach (var url in googleResults)
            {
                if (url.Contains("/scenes/") && !searchResults.Contains(url))
                {
                    searchResults.Add(url);
                }
            }

            foreach (var sceneURL in searchResults)
            {
                var http = await HTTP.Request(sceneURL, cancellationToken);
                if (http.IsOK)
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(http.Content);
                    var titleNode = doc.DocumentNode.SelectSingleNode("//h1");

                    if (titleNode != null)
                    {
                        var title = titleNode.InnerText.Trim();
                        var curID = Helper.Encode(sceneURL);
                        DateTime? releaseDateObj = null;

                        var dateNode = doc.DocumentNode.SelectSingleNode("//time");
                        if (dateNode != null)
                        {
                            var dateVal = dateNode.GetAttributeValue("datetime", string.Empty);
                            if (DateTime.TryParse(dateVal, out var date))
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
            movie.Name = doc.DocumentNode.SelectSingleNode("//h1")?.InnerText.Trim();

            var summaryNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'u-lh--opt u-fs--fo u-wh')]");
            if (summaryNodes != null && summaryNodes.Count == 1)
            {
                movie.Overview = summaryNodes[0].InnerText.Trim();
            }

            var studioNode = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'u-inline-block u-align-y--m u-relative u-fw--bold')]");
            if (studioNode != null)
            {
                var studio = studioNode.InnerText.Trim();
                movie.AddStudio(studio);
                movie.AddCollection(studio);
            }

            var dateNode = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'u-flex u-flex-wrap u-align-i--center u-fs--fo u-dw u-mt--three u-flex-grow--one desktop:u-mt--four desktop:u-fs--si')]/time")
                           ?? doc.DocumentNode.SelectSingleNode("//time[contains(@class, 'u-inline-block u-align-y--m')]");

            if (dateNode != null)
            {
                var dateVal = dateNode.GetAttributeValue("datetime", string.Empty);
                if (DateTime.TryParse(dateVal, out var date))
                {
                    movie.PremiereDate = date;
                    movie.ProductionYear = date.Year;
                }
            }

            var genreNodes = doc.DocumentNode.SelectNodes("//meta[@property='video:tag']");
            if (genreNodes != null)
            {
                foreach (var meta in genreNodes)
                {
                    var genre = meta.GetAttributeValue("content", string.Empty);
                    if (!string.IsNullOrEmpty(genre))
                    {
                        movie.AddGenre(genre);
                    }
                }
            }

            var actorNodes = doc.DocumentNode.SelectNodes("//meta[@property='video:actor']");
            if (actorNodes != null)
            {
                foreach (var meta in actorNodes)
                {
                    var actorName = meta.GetAttributeValue("content", string.Empty);
                    if (!string.IsNullOrEmpty(actorName))
                    {
                        var actorInfo = new PersonInfo { Name = actorName, Type = PersonKind.Actor };

                        var actorSlug = actorName.Replace(" ", "-").ToLower();
                        var actorUrl = Helper.GetSearchBaseURL(siteNum) + "/pornstars/" + actorSlug;

                        var actorHttp = await HTTP.Request(actorUrl, cancellationToken);
                        if (actorHttp.IsOK)
                        {
                            var actorDoc = new HtmlDocument();
                            actorDoc.LoadHtml(actorHttp.Content);
                            var imgNode = actorDoc.DocumentNode.SelectSingleNode("//div[contains(@class, 'u-ratio--model')]//img");
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

                var ogImage = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']");
                if (ogImage != null)
                {
                    var url = ogImage.GetAttributeValue("content", string.Empty);
                    if (!string.IsNullOrEmpty(url))
                    {
                        images.Add(new RemoteImageInfo { Url = url });
                    }
                }

                var lightboxImages = doc.DocumentNode.SelectNodes("//a[contains(@class, 'u-ratio--lightbox')]");
                if (lightboxImages != null)
                {
                    foreach (var link in lightboxImages)
                    {
                        var url = link.GetAttributeValue("href", string.Empty);
                        if (!string.IsNullOrEmpty(url))
                        {
                            images.Add(new RemoteImageInfo { Url = url });
                        }
                    }
                }
            }

            return images;
        }
    }
}
