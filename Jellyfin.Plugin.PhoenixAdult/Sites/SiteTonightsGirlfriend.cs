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
    public class SiteTonightsGirlfriend : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            searchTitle = searchTitle.Replace(" ", "+", StringComparison.OrdinalIgnoreCase);
            var url = Helper.GetSearchSearchURL(siteNum) + searchTitle;
            var data = await HTML.ElementFromURL(url, cancellationToken).ConfigureAwait(false);

            var searchResults = data.SelectNodesSafe("//div[@class='scene-thumbnail']/a");
            foreach (var searchResult in searchResults)
            {
                var sceneURL = searchResult.Attributes["href"].Value;

                var sceneID = new List<string> { Helper.Encode(sceneURL) };

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
            result.Item.AddStudio("Tonight's Girlfriend");

            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken).ConfigureAwait(false);

            var title = sceneData.SelectSingleText("//div[@id='content']//h1");
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

            var genreNodes = sceneData.SelectNodesSafe("//div[@class='scenepage-info']//div[@class='category desktop-only']/a");
            foreach (var genreNode in genreNodes)
            {
                result.Item.AddGenre(genreNode.InnerText.Trim());
            }

            var description = sceneData.SelectSingleText("//div[@class='scenepage-info']//p[@class='scene-description']").Trim();
            result.Item.Overview = description;

            // performers
            var performers = sceneData.SelectNodesSafe("//div[@id='content']//p[@class='grey-performers']/a");

            foreach (var performer in performers)
            {
                var performerURL = performer.Attributes["href"].Value;
                Logger.Info($"Loading performer page: {performerURL}");
                var performerData = await HTML.ElementFromURL(performerURL, cancellationToken).ConfigureAwait(false);
                var performerImage = performerData.SelectSingleNode("//div[@class='performer-details']/img");
                result.AddPerson(new PersonInfo
                {
                    Name = performer.InnerText,
                    ImageUrl = "https:" + performerImage.Attributes["src"].Value,
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

            var poster = sceneData.SelectSingleNode("//div[@class='scenepage-video']//picture//img[@class='playcard']");

            result.Add(new RemoteImageInfo
            {
                Url = "https:" + poster.Attributes["src"].Value,
                Type = ImageType.Primary,
            });

            return result;
        }
    }
}
