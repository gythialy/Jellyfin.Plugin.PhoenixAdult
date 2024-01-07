using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    public class SitePurgatoryX : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            var searchResultsURLs = new List<string>();

            var url = Helper.GetSearchSearchURL(siteNum) + searchTitle.ToLower();
            Logger.Info($"Searching for scene: {url}");
            var data = await HTML.ElementFromURL(url, cancellationToken, additionalSuccessStatusCodes: HttpStatusCode.Redirect).ConfigureAwait(false);
            var siteResults = data.SelectNodesSafe("//div[contains(@class, 'content-item')]//h3[@class='title']/a");
            if (siteResults.Count > 0)
            {
                foreach (var searchResult in siteResults)
                {
                    var sceneURL = searchResult.Attributes["href"].Value;
                    Logger.Info($"Possible result {sceneURL}");
                    searchResultsURLs.Add(sceneURL);
                }
            }
            else
            {
                Logger.Info("Searching through Google");
                var rootUrl = Helper.GetSearchBaseURL(siteNum);
                var searchResults = await GoogleSearch.GetSearchResults(searchTitle, siteNum, cancellationToken).ConfigureAwait(false);
                foreach (var searchResult in searchResults)
                {
                    if (searchResult.StartsWith(rootUrl + "/view/"))
                    {
                        Logger.Info($"Possible result {searchResult}");
                        searchResultsURLs.Add(searchResult);
                    }
                }
            }

            foreach (var searchResult in searchResultsURLs)
            {
                var sceneID = new List<string> { Helper.Encode(searchResult) };

                if (searchDate.HasValue)
                {
                    sceneID.Add(searchDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                }

                var searchResultsFromUpdate = await Helper.GetSearchResultsFromUpdate(this, siteNum, sceneID.ToArray(), searchDate, cancellationToken).ConfigureAwait(false);
                if (searchResultsFromUpdate.Any())
                {
                    result.AddRange(searchResultsFromUpdate);
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

            if (sceneID == null)
            {
                return result;
            }

            var sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var sceneDate = string.Empty;
            if (sceneID.Length > 1)
            {
                sceneDate = sceneID[1];
            }

            var siteUrl = Helper.GetSearchBaseURL(siteNum);

            result.Item.ExternalId = sceneURL;
            result.Item.AddStudio("PurgatoryX");

            Logger.Info($"Loading scene {sceneURL}");
            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken, additionalSuccessStatusCodes: HttpStatusCode.Redirect).ConfigureAwait(false);

            var title = sceneData.SelectSingleText("//section[contains(@class, 'content-info-wrap')]//h1[@class='title']");
            result.Item.Name = title;

            var series = sceneData.SelectSingleText("//section[contains(@class, 'content-info-wrap')]//p[@class='series']/span");
            result.Item.AddStudio(series);

            var dateString = sceneData.SelectSingleText("//section[contains(@class, 'content-info-wrap')]//span[@class='date']");
            if (DateTime.TryParseExact(dateString, "dddd MMMM dd, YYYY", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
            {
                result.Item.PremiereDate = sceneDateObj;
            }

            var description = sceneData.SelectSingleText("//section[contains(@class, 'content-info-wrap')]//div[@class='description']/p").Trim();
            result.Item.Overview = description;

            // performers
            var performers = sceneData.SelectNodesSafe("//section[contains(@class, 'content-info-wrap')]//div[@class='model-wrap']//li//a");

            foreach (var performer in performers)
            {
                var performerURL = performer.Attributes["href"].Value;
                Logger.Info($"Loading performer page: {performerURL}");
                var performerData = await HTML.ElementFromURL(performerURL, cancellationToken, additionalSuccessStatusCodes: HttpStatusCode.Redirect).ConfigureAwait(false);
                var performerImage = performerData.SelectSingleNode("//span[@class='model-pic']/img");
                var performerName = performerData.SelectSingleText("//h1[@class='model-name']");
                result.AddPerson(new PersonInfo
                {
                    Name = performerName,
                    ImageUrl = performerImage.Attributes["src"].Value,
                });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();

            if (sceneID == null)
            {
                return result;
            }

            var sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            Logger.Info($"Loading scene for images {sceneURL}");
            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken, additionalSuccessStatusCodes: HttpStatusCode.Redirect).ConfigureAwait(false);

            var video = sceneData.SelectSingleNode("//video[@id='main-player']");
            result.Add(new RemoteImageInfo
            {
                Url = video.Attributes["poster"].Value,
                Type = ImageType.Primary,
            });
            result.Add(new RemoteImageInfo
            {
                Url = video.Attributes["poster"].Value,
                Type = ImageType.Backdrop,
            });

            var extraImages = sceneData.SelectNodesSafe("//div[contains(@class, 'photos-slider')]//img");
            foreach (var extraImage in extraImages)
            {
                result.Add(new RemoteImageInfo
                {
                    Url = extraImage.Attributes["src"].Value,
                    Type = ImageType.Primary,
                });
                result.Add(new RemoteImageInfo
                {
                    Url = extraImage.Attributes["src"].Value,
                    Type = ImageType.Backdrop,
                });
            }

            return result;
        }
    }
}
