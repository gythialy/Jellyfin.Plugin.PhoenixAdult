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
using Jellyfin.Data.Enums;

namespace PhoenixAdult.Sites
{
    public class SiteKin8tengoku : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var sceneID = Regex.Match(searchTitle, @"^\d+").Value;
            var searchTitleNoID = string.IsNullOrEmpty(sceneID) ? searchTitle : searchTitle.Replace(sceneID, string.Empty).Trim();

            if (!string.IsNullOrEmpty(sceneID))
            {
                var sceneURL = $"{Helper.GetSearchBaseURL(siteNum)}/moviepages/{sceneID}/index.html";
                var http = await HTTP.Request(sceneURL, cancellationToken);
                if (http.IsOK)
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(http.Content);

                    var titleNode = doc.DocumentNode.SelectSingleNode("//p[@class='sub_title']|//p[@class='sub_title_vip']");
                    var titleNoFormatting = titleNode != null ? Helper.ParseTitle(titleNode.InnerText.Split('/')[0].Trim(), siteNum) : string.Empty;

                    var dateNode = doc.DocumentNode.SelectSingleNode("//tr[./*[contains(., 'Date')]]//td[@class='movie_table_td2']");
                    var releaseDate = string.Empty;
                    if (dateNode != null && DateTime.TryParseExact(dateNode.InnerText.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
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

            var searchURL = Helper.GetSearchSearchURL(siteNum) + searchTitleNoID.Replace(" ", "+");
            var searchHttp = await HTTP.Request(searchURL, cancellationToken);
            if (searchHttp.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(searchHttp.Content);
                foreach (var searchResult in doc.DocumentNode.SelectNodes("//div[@class='movie_list']"))
                {
                    var sceneURL = Helper.GetSearchBaseURL(siteNum) + searchResult.SelectSingleNode(".//div[@class='movielisttext03']/a").GetAttributeValue("href", string.Empty);
                    if (result.Any(r => Helper.Decode(r.ProviderIds[Plugin.Instance.Name]) == sceneURL))
                    {
                        continue;
                    }

                    var titleNoFormatting = Helper.ParseTitle(searchResult.SelectSingleNode(".//div[@class='movielisttext02']").InnerText.Trim(), siteNum);
                    var curID = Helper.Encode(sceneURL);
                    var image = searchResult.SelectSingleNode(".//img").GetAttributeValue("src", string.Empty);

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}]",
                        SearchProviderName = Plugin.Instance.Name,
                        ImageUrl = image,
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
            movie.Name = Helper.ParseTitle(doc.DocumentNode.SelectSingleNode("//p[@class='sub_title']|//p[@class='sub_title_vip']").InnerText.Split('/')[0].Trim(), siteNum);
            movie.AddStudio(Helper.GetSearchSiteName(siteNum));
            movie.AddCollection(Helper.GetSearchSiteName(siteNum));

            var dateNode = doc.DocumentNode.SelectSingleNode("//tr[./*[contains(., 'Date')]]//td[@class='movie_table_td2']");
            if (dateNode != null && DateTime.TryParseExact(dateNode.InnerText.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            foreach (var genreLink in doc.DocumentNode.SelectNodes("//tr[./*[contains(., 'Category')]]//a"))
            {
                var genreName = genreLink.InnerText.Trim();
                movie.AddGenre(genreName);
            }

            foreach (var actorLink in doc.DocumentNode.SelectNodes("//tr[./*[contains(., 'Model')]]//a"))
            {
                var actorName = actorLink.InnerText.Trim();
                result.AddPerson(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
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

                var posterNode = doc.DocumentNode.SelectSingleNode("//div[@id='movie_photo']//img");
                if (posterNode != null)
                {
                    var posterURL = posterNode.GetAttributeValue("src", string.Empty);
                    if (!string.IsNullOrEmpty(posterURL))
                    {
                        images.Add(new RemoteImageInfo { Url = posterURL, Type = ImageType.Primary });
                    }
                }

                var samplePhotoNodes = doc.DocumentNode.SelectNodes("//div[contains(@class,'photo-gallery')]//a");
                if (samplePhotoNodes != null)
                {
                    foreach (var node in samplePhotoNodes)
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
