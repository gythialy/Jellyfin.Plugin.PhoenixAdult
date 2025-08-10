using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
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

namespace PhoenixAdult.Sites
{
    public class NetworkJavBus : IProviderBase
    {
        private IDictionary<string, string> GetCookies()
        {
            return new Dictionary<string, string> { { "existmag", "all" }, { "dv", "1" } };
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null) return result;

            string searchJAVID = null;
            string directJAVID = null;
            var splitSearchTitle = searchTitle.Split(' ');
            if (splitSearchTitle.Length > 1 && int.TryParse(splitSearchTitle[1], out _))
            {
                searchJAVID = $"{splitSearchTitle[0]}%2B{splitSearchTitle[1]}";
                directJAVID = $"{splitSearchTitle[0]}-{splitSearchTitle[1]}";
            }

            string encodedSearchTitle = searchJAVID ?? Uri.EscapeDataString(searchTitle);

            foreach (var searchType in new[] { "Censored", "Uncensored" })
            {
                string url = (searchType == "Uncensored")
                    ? $"{Helper.GetSearchSearchURL(siteNum)}uncensored/search/{encodedSearchTitle}"
                    : $"{Helper.GetSearchSearchURL(siteNum)}search/{encodedSearchTitle}";

                var searchResultsNode = await HTML.ElementFromURL(url, cancellationToken, null, GetCookies());
                if (searchResultsNode == null) continue;

                var searchResults = searchResultsNode.SelectNodes("//a[@class='movie-box']");
                if (searchResults == null) continue;

                foreach (var searchResult in searchResults)
                {
                    string titleNoFormatting = searchResult.SelectSingleNode(".//span[1]")?.InnerText.Replace("\t", "").Replace("\r\n", "").Trim();
                    string javid = searchResult.SelectSingleNode(".//date[1]")?.InnerText.Trim();
                    string sceneURL = searchResult.GetAttributeValue("href", "");
                    string curID = Helper.Encode(sceneURL);

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}" } },
                        Name = $"[{searchType}][{javid}] {titleNoFormatting}",
                        SearchProviderName = Plugin.Instance.Name
                    });
                }
            }

            if (!string.IsNullOrEmpty(directJAVID))
            {
                string sceneURL = Helper.GetSearchSearchURL(siteNum) + directJAVID;
                var searchResultNode = await HTML.ElementFromURL(sceneURL, cancellationToken, null, GetCookies());
                if (searchResultNode != null)
                {
                    string javTitle = searchResultNode.SelectSingleNode("//head/title")?.InnerText.Trim().Replace(" - JavBus", "");
                    string curID = Helper.Encode(sceneURL);
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}" } },
                        Name = $"[Direct][{directJAVID}] {javTitle}",
                        SearchProviderName = Plugin.Instance.Name
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

            string sceneURL = Helper.Decode(sceneID[0].Split('|')[0]);
            if (!sceneURL.StartsWith("http"))
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken, null, GetCookies());
            if (detailsPageElements == null) return result;

            var movie = (Movie)result.Item;
            string javStudio = detailsPageElements.SelectSingleNode("//p/a[contains(@href, '/studio/')]")?.InnerText.Trim();
            string javTitle = detailsPageElements.SelectSingleNode("//head/title")?.InnerText.Trim().Replace(" - JavBus", "");
            movie.Name = javTitle;
            movie.AddStudio(javStudio);

            string label = detailsPageElements.SelectSingleNode("//p/a[contains(@href, '/label/')]")?.InnerText.Trim();
            string series = detailsPageElements.SelectSingleNode("//p/a[contains(@href, '/series/')]")?.InnerText.Trim();
            if (!string.IsNullOrEmpty(label))
                movie.AddTag(label);
            else
                movie.AddTag(javStudio);
            if (!string.IsNullOrEmpty(series))
                movie.AddTag(series);

            var dateNode = detailsPageElements.SelectSingleNode("//div[@class='col-md-3 info']/p[2]");
            if (dateNode != null)
            {
                string dateStr = dateNode.InnerText.Replace("Release Date:", "").Trim();
                if (dateStr != "0000-00-00" && DateTime.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                {
                    movie.PremiereDate = parsedDate;
                    movie.ProductionYear = parsedDate.Year;
                }
            }

            var genreLinks = detailsPageElements.SelectNodes("//span[@class='genre']//a[contains(@href, '/genre/')]");
            if (genreLinks != null)
            {
                foreach (var genre in genreLinks)
                    movie.AddGenre(genre.InnerText.ToLower().Trim());
            }

            var actorLinks = detailsPageElements.SelectNodes("//a[@class='avatar-box']");
            if (actorLinks != null)
            {
                foreach(var actorLink in actorLinks)
                {
                    string actorName = actorLink.SelectSingleNode("./div/img")?.GetAttributeValue("title", "");
                    string actorPhotoURL = actorLink.SelectSingleNode("./div[@class='photo-frame']/img")?.GetAttributeValue("src", "");
                    if (!string.IsNullOrEmpty(actorPhotoURL) && !actorPhotoURL.StartsWith("http"))
                        actorPhotoURL = Helper.GetSearchBaseURL(siteNum) + actorPhotoURL;
                    if (actorPhotoURL?.EndsWith("nowprinting.gif") == true)
                        actorPhotoURL = string.Empty;

                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonType.Actor, ImageUrl = actorPhotoURL });
                }
            }

            var directorLink = detailsPageElements.SelectSingleNode("//p/a[contains(@href, '/director/')]");
            if (directorLink != null)
                result.People.Add(new PersonInfo { Name = directorLink.InnerText.Trim(), Type = PersonType.Director });

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneURL = Helper.Decode(sceneID[0].Split('|')[0]);
            if (!sceneURL.StartsWith("http"))
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken, null, GetCookies());
            if (detailsPageElements == null) return images;

            var posterNodes = detailsPageElements.SelectNodes("//a[contains(@href, '/cover/') or @class='sample-box']");
            if (posterNodes != null)
            {
                foreach(var poster in posterNodes)
                {
                    string posterUrl = poster.GetAttributeValue("href", "");
                    if (!posterUrl.StartsWith("http"))
                        posterUrl = Helper.GetSearchBaseURL(siteNum) + posterUrl;
                    images.Add(new RemoteImageInfo { Url = posterUrl, Type = ImageType.Backdrop });
                }
            }

            var coverImageNode = detailsPageElements.SelectSingleNode("//a[contains(@href, '/cover/')]");
            if (coverImageNode != null)
            {
                string coverImageUrl = coverImageNode.GetAttributeValue("href", "");
                string coverImageCode = coverImageUrl.Split('/').Last().Split('.').First().Split('_').First();
                string imageHost = string.Join("/", coverImageUrl.Split('/').Take(coverImageUrl.Split('/').Length - 2));
                string thumbUrl = $"{imageHost}/thumb/{coverImageCode}.jpg";
                if(thumbUrl.Contains("/images."))
                    thumbUrl = thumbUrl.Replace("thumb", "thumbs");
                if (!thumbUrl.StartsWith("http"))
                    thumbUrl = Helper.GetSearchBaseURL(siteNum) + thumbUrl;
                images.Insert(0, new RemoteImageInfo { Url = thumbUrl, Type = ImageType.Primary });
            }

            return images;
        }
    }
}
