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
using static System.Net.Mime.MediaTypeNames;

namespace PhoenixAdult.Sites
{
    public class SiteNewSensations : IProviderBase
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
            result.Item.AddStudio("New Sensations");

            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken).ConfigureAwait(false);

            result.Item.Name = sceneData.SelectSingleText("//div[@class='sceneRight']//div[@class='indScene']//h1");

            var dateNode = sceneData.SelectSingleText("//div[@class='sceneRight']//div[@class='indScene']//div[@class='sceneDateP']//span").TrimEnd(',');
            if (DateTime.TryParseExact(dateNode, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
            {
                result.Item.PremiereDate = sceneDateObj;
            }

            var descriptionNodes = sceneData.SelectNodesSafe("//div[@class='description']//h2");
            var overview = descriptionNodes[0].InnerText.Trim();
            result.Item.Overview = overview;

            // performers
            var performerNodes = sceneData.SelectNodesSafe("//div[@class='sceneRight']//div[@class='indScene']//div[@class='sceneTextLink']//p//span//a");

            foreach (var performerNode in performerNodes)
            {
                result.People.Add(new PersonInfo
                {
                    Name = performerNode.InnerText,
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

            var posterNode = sceneData.SelectSingleNode("//span[@id='trailer_thumb']//span//img");
            Logger.Info($"Found poster: {posterNode}");
            result.Add(new RemoteImageInfo
            {
                Url = posterNode.Attributes["src"].Value,
                Type = ImageType.Primary,
            });

            return result;
        }
    }
}
