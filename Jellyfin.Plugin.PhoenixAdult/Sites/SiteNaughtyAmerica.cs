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
    public class SiteNaughtyAmerica : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            var searchURL = Helper.GetSearchSearchURL(siteNum) + searchTitle;
            var searchData = await HTML.ElementFromURL(searchURL, cancellationToken).ConfigureAwait(false);

            var searchResultNodes = searchData.SelectNodesSafe("//div[@class='scene-grid-item']/a[@class='contain-img']");

            foreach (var node in searchResultNodes)
            {
                var sceneUrl = node.Attributes["href"].Value;
                Logger.Info($"Possible result: {sceneUrl}");
                var sceneID = new List<string> { Helper.Encode(sceneUrl) };

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

            if (sceneID == null)
            {
                return result;
            }

            var sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            Logger.Info($"Loading scene: {sceneURL}");
            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken).ConfigureAwait(false);

            result.Item.ExternalId = sceneURL;
            result.Item.AddStudio("Naughty America");
            result.Item.Name = sceneData.SelectSingleText("//div[@class='scene-info']/h1");

            var date = sceneData.SelectSingleText("//span[contains(@class, 'entry-date')]");
            if (DateTime.TryParseExact(date, "MMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
            {
                result.Item.PremiereDate = sceneDateObj;
            }

            var subSite = sceneData.SelectSingleNode("//div[@class='scene-info']//h2/a");
            if (subSite != null)
            {
                result.Item.AddStudio(subSite.InnerText);
            }

            result.Item.Overview = sceneData.SelectSingleText("//div[contains(@class, 'synopsis')]").Substring("Synopsis".Length);

            var categories = sceneData.SelectNodesSafe("//div[contains(@class, 'categories')]/a");
            foreach (var category in categories)
            {
                result.Item.AddGenre(category.InnerText);
            }

            var performers = sceneData.SelectNodesSafe("//div[@class='performer-list']/a");
            foreach (var performer in performers)
            {
                var performerName = performer.InnerText;
                result.People.Add(new PersonInfo
                {
                    Name = performerName,
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

            var galleryImages = sceneData.SelectNodesSafe("//div[@class='contain-scene-images desktop-only']/a");
            foreach (var image in galleryImages)
            {
                var imageUrl = "https:" + image.Attributes["href"].Value;
                Logger.Info($"Adding image: {imageUrl}");
                result.Add(new RemoteImageInfo
                {
                    Url = imageUrl,
                    Type = ImageType.Primary,
                });
                result.Add(new RemoteImageInfo
                {
                    Url = imageUrl,
                    Type = ImageType.Backdrop,
                });
            }

            return result;
        }
    }
}
