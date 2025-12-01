using System;
using System.Collections.Generic;
using System.Linq;
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
    public class SiteLustReality : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var searchURL = Helper.GetSearchSearchURL(siteNum) + searchTitle.Replace(" ", "-").ToLower();

            var searchResults = new List<string> { searchURL };
            var googleResults = await WebSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            searchResults.AddRange(googleResults.Where(url => url.Contains("/scene/") && !searchResults.Contains(url)));

            foreach (var sceneURL in searchResults)
            {
                var http = await HTTP.Request(sceneURL, cancellationToken);
                if (http.IsOK)
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(http.Content);
                    var titleNode = doc.DocumentNode.SelectSingleNode("//h1");
                    if (titleNode == null)
                    {
                        continue;
                    }

                    var titleNoFormatting = titleNode.InnerText.Trim();
                    var dateNode = doc.DocumentNode.SelectSingleNode("//span[@class='date-display-single'] | //span[@class='u-inline-block u-mr--nine'] | //div[@class='video-meta-date'] | //div[@class='date']");
                    var releaseDate = string.Empty;
                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    var curID = Helper.Encode(sceneURL);
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name,
                    });
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
            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);
            movie.ExternalId = sceneURL;
            movie.Name = doc.DocumentNode.SelectSingleNode("//h1").InnerText.Trim();
            movie.Overview = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'u-mb--six ')]").InnerText.Trim();
            movie.AddStudio(Helper.GetSearchSiteName(siteNum));
            movie.AddCollection(Helper.GetSearchSiteName(siteNum));

            var dateNode = doc.DocumentNode.SelectSingleNode("//span[@class='date-display-single'] | //span[@class='u-inline-block u-mr--nine'] | //div[@class='video-meta-date'] | //div[@class='date']");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            foreach (var genreLink in doc.DocumentNode.SelectNodes("//a[contains(@href, '/list/category/')]"))
            {
                var genreName = genreLink.InnerText.Trim();
                movie.AddGenre(genreName);
            }

            foreach (var actorLink in doc.DocumentNode.SelectNodes("//a[contains(@href, '/pornstars/model/')]"))
            {
                var actorName = actorLink.InnerText.Trim();
                var actorPageURL = Helper.GetSearchBaseURL(siteNum) + actorLink.GetAttributeValue("href", string.Empty);
                var actorHttp = await HTTP.Request(actorPageURL, cancellationToken);
                if (actorHttp.IsOK)
                {
                    var actorDoc = new HtmlDocument();
                    actorDoc.LoadHtml(actorHttp.Content);
                    var actorPhotoNode = actorDoc.DocumentNode.SelectSingleNode("//div[contains(@class, 'u-ratio--model-poster')]//img");
                    var actorPhotoURL = actorPhotoNode?.GetAttributeValue("data-src", string.Empty);
                    result.AddPerson(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoURL });
                }
                else
                {
                    result.AddPerson(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var sceneURL = Helper.Decode(sceneID[0]);
            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);

                var posterNode = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'splash-screen')]");
                if (posterNode != null)
                {
                    var style = posterNode.GetAttributeValue("style", string.Empty);
                    var match = Regex.Match(style, @"url\('?(.*?)'?\)");
                    if (match.Success)
                    {
                        images.Add(new RemoteImageInfo { Url = match.Groups[1].Value, Type = ImageType.Primary });
                    }
                }

                var backdropNodes = doc.DocumentNode.SelectNodes("//a[contains(@class, 'u-ratio--lightbox')]");
                if (backdropNodes != null)
                {
                    foreach (var node in backdropNodes)
                    {
                        var imageURL = node.GetAttributeValue("href", string.Empty);
                        if (!string.IsNullOrEmpty(imageURL))
                        {
                            images.Add(new RemoteImageInfo { Url = imageURL, Type = ImageType.Backdrop });
                        }
                    }
                }
            }

            return images;
        }
    }
}
