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
            var siteResults = data.SelectNodesSafe("//h4[contains(@class, 'content-title-wrap')]/a[contains(@class, 'content-title')]");
            if (siteResults.Count > 0)
            {
                foreach (var searchResult in siteResults)
                {
                    var sceneURL = searchResult.Attributes["href"]?.Value ?? string.Empty;
                    if (string.IsNullOrEmpty(sceneURL))
                    {
                        continue;
                    }

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

            result.Item.ExternalId = sceneURL;
            result.Item.AddStudio("PurgatoryX");

            Logger.Info($"Loading scene {sceneURL}");
            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken, additionalSuccessStatusCodes: HttpStatusCode.Redirect).ConfigureAwait(false);

            var title = sceneData.SelectSingleText("//h1[contains(@class, 'title')]");
            result.Item.Name = title;

            var series = sceneData.SelectSingleText("//p[contains(@class, 'series')]");
            if (!string.IsNullOrEmpty(series))
            {
                result.Item.AddStudio(series);
            }

            var dateString = sceneData.SelectSingleText("//div[contains(@class, 'meta')]/span[1]");
            if (DateTime.TryParseExact(dateString, "dddd MMMM dd, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
            {
                result.Item.PremiereDate = sceneDateObj;
            }

            var description = sceneData.SelectSingleText("//div[contains(@class, 'description')]/p").Trim();
            result.Item.Overview = description;

            // performers
            var performerItems = sceneData.SelectNodesSafe("//ul[contains(@class, 'models-list')]/li");
            foreach (var performerItem in performerItems)
            {
                var performerName = performerItem.SelectSingleNode(".//h5")?.InnerText.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(performerName))
                {
                    continue;
                }

                var performerImage = performerItem.SelectSingleNode(".//img")?.GetAttributeValue("src", string.Empty) ?? string.Empty;
                result.AddPerson(new PersonInfo
                {
                    Name = performerName,
                    ImageUrl = performerImage,
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
            if (video == null)
            {
                return result;
            }

            var posterUrl = video.GetAttributeValue("poster", string.Empty);
            result.Add(new RemoteImageInfo
            {
                Url = posterUrl,
                Type = ImageType.Primary,
            });
            result.Add(new RemoteImageInfo
            {
                Url = posterUrl,
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
