using System;
using System.Collections.Generic;
using System.Globalization;
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
    public class SiteMilfVR : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var sceneID = Regex.Match(searchTitle, @"^\d+").Value;
            var searchTitleNoID = string.IsNullOrEmpty(sceneID) ? searchTitle : searchTitle.Replace(sceneID, string.Empty).Trim();

            if (!string.IsNullOrEmpty(sceneID) && string.IsNullOrEmpty(searchTitleNoID))
            {
                var sceneURL = $"{Helper.GetSearchBaseURL(siteNum)}/{sceneID}";
                var http = await HTTP.Request(sceneURL, cancellationToken, new Dictionary<string, string> { { "sst", "ulang-en" } });
                if (http.IsOK)
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(http.Content);

                    var titleNoFormatting = doc.DocumentNode.SelectSingleNode("//h1[@class='detail__title']").InnerText;
                    var curID = Helper.Encode(sceneURL);

                    var dateNode = doc.DocumentNode.SelectSingleNode("//span[@class='detail__date']");
                    var releaseDate = string.Empty;
                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = $"[{Helper.GetSearchSiteName(siteNum)}] {titleNoFormatting} {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }
            }
            else
            {
                var searchURL = Helper.GetSearchSearchURL(siteNum) + searchTitle.Replace(" ", "+");
                var http = await HTTP.Request(searchURL, cancellationToken, new Dictionary<string, string> { { "sst", "ulang-en" } });
                if (http.IsOK)
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(http.Content);
                    foreach (var searchResult in doc.DocumentNode.SelectNodes("//ul[@class='cards-list']//li"))
                    {
                        var titleNoFormatting = searchResult.SelectSingleNode(".//div[@class='card__footer']//div[@class='card__h']").InnerText;
                        var sceneURL = searchResult.SelectSingleNode(".//a").GetAttributeValue("href", string.Empty);
                        var curID = Helper.Encode(sceneURL);
                        var releaseDate = string.Empty;
                        var dateNode = searchResult.SelectSingleNode(".//div[@class='card__date']");
                        if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                        {
                            releaseDate = parsedDate.ToString("yyyy-MM-dd");
                        }

                        var image = searchResult.SelectSingleNode(".//source").GetAttributeValue("srcset", string.Empty).Replace(".webp", ".jpg");

                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, curID } },
                            Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                            SearchProviderName = Plugin.Instance.Name,
                            ImageUrl = image,
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
            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);
            movie.ExternalId = sceneURL;
            movie.Name = doc.DocumentNode.SelectSingleNode("//h1[@class='detail__title']").InnerText;
            movie.Overview = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'detail__txt')]").InnerText.Trim();
            movie.AddStudio(Helper.GetSearchSiteName(siteNum));
            movie.AddCollection(Helper.GetSearchSiteName(siteNum));

            var dateNode = doc.DocumentNode.SelectSingleNode("//span[@class='detail__date']");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            foreach (var genreLink in doc.DocumentNode.SelectNodes("//div[contains(@class, 'tag-list')]//a"))
            {
                var genreName = genreLink.InnerText.Trim();
                movie.AddGenre(genreName);
            }

            foreach (var actorLink in doc.DocumentNode.SelectNodes("//div[@class='detail__models']//a"))
            {
                var actorName = actorLink.InnerText.Trim();
                var actorPageURL = Helper.GetSearchBaseURL(siteNum) + actorLink.GetAttributeValue("href", string.Empty);
                var actorHttp = await HTTP.Request(actorPageURL, cancellationToken);
                var actorPhotoURL = string.Empty;
                if (actorHttp.IsOK)
                {
                    var actorDoc = new HtmlDocument();
                    actorDoc.LoadHtml(actorHttp.Content);
                    var actorPhotoNodes = actorDoc.DocumentNode.SelectNodes("//div[@class='person__avatar']//source/@srcset");
                    if (actorPhotoNodes != null && actorPhotoNodes.Count > 1)
                    {
                        actorPhotoURL = actorPhotoNodes[1].GetAttributeValue("srcset", string.Empty).Replace(".webp", ".jpg");
                    }
                }

                result.AddPerson(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoURL });
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
                var posterNode = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']");
                if (posterNode != null)
                {
                    var posterUrl = posterNode.GetAttributeValue("content", string.Empty).Replace("cover", "hero").Replace("medium.jpg", "large.jpg");
                    if (!string.IsNullOrEmpty(posterUrl))
                    {
                        images.Add(new RemoteImageInfo { Url = posterUrl, Type = ImageType.Primary });
                    }
                }

                foreach (var actorLink in doc.DocumentNode.SelectNodes("//div[@class='detail__models']//a"))
                {
                    var actorPageURL = Helper.GetSearchBaseURL(siteNum) + actorLink.GetAttributeValue("href", string.Empty);
                    var actorHttp = await HTTP.Request(actorPageURL, cancellationToken);
                    if (actorHttp.IsOK)
                    {
                        var actorDoc = new HtmlDocument();
                        actorDoc.LoadHtml(actorHttp.Content);
                        var actorPhotoNodes = actorDoc.DocumentNode.SelectNodes("//div[@class='person__avatar']//source/@srcset");
                        if (actorPhotoNodes != null && actorPhotoNodes.Count > 1)
                        {
                            var actorPhotoURL = actorPhotoNodes[1].GetAttributeValue("srcset", string.Empty).Replace(".webp", ".jpg");
                            if (!string.IsNullOrEmpty(actorPhotoURL))
                            {
                                images.Add(new RemoteImageInfo { Url = actorPhotoURL, Type = ImageType.Backdrop });
                            }
                        }
                    }
                }
            }

            return images;
        }
    }
}
