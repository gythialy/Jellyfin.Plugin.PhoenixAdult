using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
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
    public class SiteTSPlayground : IProviderBase
    {
        private static Regex posterUrlRegex = new Regex(@"<(.*?)>");

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            var searchResultsURLs = new List<string>();

            var urlEncodedSearchTitle = searchTitle.Replace(" ", "-", StringComparison.OrdinalIgnoreCase);
            var url = Helper.GetSearchSearchURL(siteNum) + urlEncodedSearchTitle;
            Logger.Info($"Searching for scene: {url}");
            var data = await HTML.ElementFromURL(url, cancellationToken, additionalSuccessStatusCodes: HttpStatusCode.Redirect).ConfigureAwait(false);
            var siteResults = data.SelectNodesSafe("//div[contains(@class, 'inner-col')]//a[./span[@class='item-name']]");

            foreach (var searchResult in siteResults)
            {
                var sceneURL = searchResult.Attributes["href"].Value;
                Logger.Info($"Possible result {sceneURL}");
                searchResultsURLs.Add(sceneURL);
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

            result.Item.ExternalId = sceneURL;
            result.Item.AddStudio("TS Playground");

            Logger.Info($"Loading scene {sceneURL}");
            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken, additionalSuccessStatusCodes: HttpStatusCode.Redirect).ConfigureAwait(false);

            var title = sceneData.SelectSingleText("//aside[contains(@class, 'content-aside-col')]/div[@class='content-title']/h1");
            result.Item.Name = title;

            var dateString = sceneData.SelectSingleText("//aside[contains(@class, 'content-aside-col')]//div[@class='content-date']/div");

            // example: 'Date: 09.01.2024'
            dateString = dateString.Substring(6); // remove 'Date: '
            if (DateTime.TryParseExact(dateString, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
            {
                result.Item.PremiereDate = sceneDateObj;
            }

            // performers
            var performers = sceneData.SelectNodesSafe("//aside[contains(@class, 'content-aside-col')]//div[@class='content-models']/a");

            foreach (var performer in performers)
            {
                var performerURL = performer.Attributes["href"].Value;
                Logger.Info($"Loading performer page: {performerURL}");
                var performerData = await HTML.ElementFromURL(performerURL, cancellationToken, additionalSuccessStatusCodes: HttpStatusCode.Redirect).ConfigureAwait(false);
                var performerImage = performerData.SelectSingleNode("//div[@class='model-avatar']/img");
                var performerName = performerData.SelectSingleText("//h1");
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

            var poster = sceneData.SelectSingleNode("//div[@class='fluid_pseudo_poster']");
            var posterStyle = poster.Attributes["style"].Value;
            var posterUrl = posterUrlRegex.Match(posterStyle).Groups[1].Value;

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

            return result;
        }
    }
}
