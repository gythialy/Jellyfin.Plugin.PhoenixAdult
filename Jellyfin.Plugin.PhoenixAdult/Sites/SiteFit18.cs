using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    public class SiteFit18 : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            var rootUrl = Helper.GetSearchSearchURL(siteNum);
            var searchResultsURLs = new List<string>();

            var searchResults = await GoogleSearch.GetSearchResults(searchTitle, siteNum, cancellationToken).ConfigureAwait(false);
            foreach (var searchResult in searchResults)
            {
                if (searchResult.StartsWith(rootUrl))
                {
                    searchResultsURLs.Add(searchResult);
                }
            }

            foreach (var url in searchResultsURLs)
            {
                Logger.Info(url);
                var sceneURL = new Uri(url);
                var sceneID = new List<string> { Helper.Encode(sceneURL.AbsolutePath) };

                if (searchDate.HasValue)
                {
                    sceneID.Add(searchDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                }

                var searchResult = await Helper.GetSearchResultsFromUpdate(this, siteNum, sceneID.ToArray(), searchDate, cancellationToken).ConfigureAwait(false);
                if (searchResult.Any())
                {
                    result.AddRange(searchResult);
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

            if (sceneID == null || siteNum == null)
            {
                return result;
            }

            string sceneURL = Helper.Decode(sceneID[0]),
                sceneDate = string.Empty;

            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            if (sceneID.Length > 1)
            {
                sceneDate = sceneID[1];
            }

            result.Item.ExternalId = sceneURL;
            result.Item.AddStudio("Fit18");

            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken).ConfigureAwait(false);

            var title = sceneData.SelectSingleText("//div[@class='info']/h1").Trim();
            result.Item.Name = title;

            if (string.IsNullOrEmpty(sceneDate))
            {
                // get date from MetadataApi
                var metadataApiProvider = Helper.GetMetadataAPIProvider();
                var searchResults = await metadataApiProvider.Search(new int[] { 48, 0 }, title, null, cancellationToken);

                result.Item.PremiereDate = searchResults[0].PremiereDate;
            }
            else
            {
                result.Item.PremiereDate = DateTime.Parse(sceneDate);
            }

            var description = sceneData.SelectSingleText("//div[@class='info']/p").Trim();
            result.Item.Overview = description;

            // performers
            var performerNodes = sceneData.SelectNodesSafe("//div[@class='info']/h3//a");

            foreach (var performerNode in performerNodes)
            {
                var name = performerNode.InnerText;
                var performerUrl = Helper.GetSearchBaseURL(siteNum) + performerNode.Attributes["href"].Value;
                var performerPage = await HTML.ElementFromURL(performerUrl, cancellationToken).ConfigureAwait(false);

                var performerImageNode = performerPage.SelectSingleNode("//picture/img");
                result.AddPerson(new PersonInfo
                {
                    Name = name,
                    ImageUrl = performerImageNode.Attributes["src"].Value,
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

            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken).ConfigureAwait(false);
            var posterNode = sceneData.SelectSingleNode("//a[div[@class='play']]/img");
            result.Add(new RemoteImageInfo
            {
                Url = posterNode.Attributes["src"].Value,
                Type = ImageType.Primary,
            });

            return result;
        }
    }
}
