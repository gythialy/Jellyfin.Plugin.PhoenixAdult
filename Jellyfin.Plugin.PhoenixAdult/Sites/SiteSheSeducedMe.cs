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
    public class SiteSheSeducedMe : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            searchTitle = searchTitle.Replace(" ", "+", StringComparison.OrdinalIgnoreCase)
                .Replace("'", string.Empty, StringComparison.OrdinalIgnoreCase);
            var url = Helper.GetSearchSearchURL(siteNum) + searchTitle;
            Logger.Info($"Searching for scene: {url}");
            var data = await HTML.ElementFromURL(url, cancellationToken, additionalSuccessStatusCodes: HttpStatusCode.Redirect).ConfigureAwait(false);

            var searchResults = data.SelectNodesSafe("//div[@class='updateItem']/a");
            foreach (var searchResult in searchResults)
            {
                var sceneURL = searchResult.Attributes["href"].Value;
                Logger.Info($"Possible result {sceneURL}");
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

            var siteUrl = Helper.GetSearchBaseURL(siteNum);

            result.Item.ExternalId = sceneURL;
            result.Item.AddStudio("She Seduced Me");

            Logger.Info($"Loading scene {sceneURL}");
            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken, additionalSuccessStatusCodes: HttpStatusCode.Redirect).ConfigureAwait(false);

            var title = sceneData.SelectSingleText("//span[@class='update_title']");
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

            var genreNodes = sceneData.SelectNodesSafe("//span[@class='update_tags']/a");
            foreach (var genreNode in genreNodes)
            {
                result.Item.AddGenre(genreNode.InnerText.Trim());
            }

            var description = sceneData.SelectSingleText("//span[@class='latest_update_description']").Trim();
            result.Item.Overview = description;

            // performers
            var performers = sceneData.SelectNodesSafe("//div[@class='update_block_info']/span[@class='tour_update_models']/a");

            foreach (var performer in performers)
            {
                var performerURL = performer.Attributes["href"].Value;
                Logger.Info($"Loading performer page: {performerURL}");
                var performerData = await HTML.ElementFromURL(performerURL, cancellationToken, additionalSuccessStatusCodes: HttpStatusCode.Redirect).ConfigureAwait(false);
                var performerImage = performerData.SelectSingleNode("//img[contains(@class, 'model_bio_thumb')]");
                result.AddPerson(new PersonInfo
                {
                    Name = performer.InnerText,
                    ImageUrl = siteUrl + performerImage.Attributes["src0_2x"].Value,
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

            var siteUrl = Helper.GetSearchBaseURL(siteNum);

            Logger.Info($"Loading scene for images {sceneURL}");
            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken, additionalSuccessStatusCodes: HttpStatusCode.Redirect).ConfigureAwait(false);

            var imagesRootNode = sceneData.SelectSingleNode("//div[@class='update_image']");

            var poster = imagesRootNode.SelectSingleNode("./a[@class='featured']/img");
            result.Add(new RemoteImageInfo
            {
                Url = siteUrl + poster.Attributes["src0_2x"].Value,
                Type = ImageType.Primary,
            });
            result.Add(new RemoteImageInfo
            {
                Url = siteUrl + poster.Attributes["src0_3x"].Value,
                Type = ImageType.Backdrop,
            });

            var extraImages = imagesRootNode.SelectNodesSafe("./div[@class='left']/a/img");
            foreach (var extraImage in extraImages)
            {
                if (extraImage.Attributes.Contains("src0_2x"))
                {
                    result.Add(new RemoteImageInfo
                    {
                        Url = siteUrl + extraImage.Attributes["src0_2x"].Value,
                        Type = ImageType.Primary,
                    });
                    result.Add(new RemoteImageInfo
                    {
                        Url = siteUrl + extraImage.Attributes["src0_3x"].Value,
                        Type = ImageType.Backdrop,
                    });
                }
                else
                {
                    result.Add(new RemoteImageInfo
                    {
                        Url = siteUrl + extraImage.Attributes["src"].Value,
                        Type = ImageType.Primary,
                    });
                    result.Add(new RemoteImageInfo
                    {
                        Url = siteUrl + extraImage.Attributes["src"].Value,
                        Type = ImageType.Backdrop,
                    });
                }
            }

            return result;
        }
    }
}
