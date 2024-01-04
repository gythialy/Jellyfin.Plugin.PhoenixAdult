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
    public class SiteAbbyWinters : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            var rootUrl = Helper.GetSearchSearchURL(siteNum);
            var searchUrls = new List<string>
            {
                rootUrl + "girls_and_their_boys",
                rootUrl + "girl_girl",
                rootUrl + "video_masturbation",
                rootUrl + "nude_girls",
            };
            var searchResultsURLs = new List<string>();

            var searchResults = await GoogleSearch.GetSearchResults(searchTitle, siteNum, cancellationToken).ConfigureAwait(false);
            foreach (var searchResult in searchResults)
            {
                foreach (var searchUrl in searchUrls)
                {
                    if (searchResult.StartsWith(searchUrl))
                    {
                        searchResultsURLs.Add(searchResult);
                    }
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
            result.Item.AddStudio("Abby Winters");

            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken).ConfigureAwait(false);

            var title = sceneData.SelectSingleText("//div[contains(@class, 'page-heading')]//h1").Trim();
            result.Item.Name = title;

            var section = sceneData.SelectSingleText("//section[@class='section-intro']//tr[th[text() = 'Section']]/td/a");
            result.Item.AddStudio(section);

            if (string.IsNullOrEmpty(sceneDate))
            {
                var date = sceneData.SelectSingleText("//section[@class='section-intro']//tr[th[text() = 'Release date']]/td");
                if (DateTime.TryParseExact(date, "dd MMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
                {
                    result.Item.PremiereDate = sceneDateObj;
                }
            }
            else
            {
                result.Item.PremiereDate = DateTime.Parse(sceneDate);
            }

            var description = sceneData.SelectSingleText("//section[@class='section-intro']//div[contains(@class, 'description')]").Trim();
            result.Item.Overview = description;

            // performers
            var performerNodes = sceneData.SelectNodesSafe("//section[@class='section-intro']//tr[th[text() = 'Girls in this Scene']]/td/a");

            foreach (var performerNode in performerNodes)
            {
                var name = performerNode.InnerText;
                var performerUrl = performerNode.Attributes["href"].Value;
                var performerPage = await HTML.ElementFromURL(performerUrl, cancellationToken).ConfigureAwait(false);
                var performerImageNode = performerPage.SelectSingleNode("//div[@class='feature-image']//img");
                result.People.Add(new PersonInfo
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
            var posterNode = sceneData.SelectSingleNode("//section[@class='section-intro']//div[@class='feature-image']//img");
            if (posterNode != null)
            {
                result.Add(new RemoteImageInfo
                {
                    Url = posterNode.Attributes["src"].Value,
                    Type = ImageType.Primary,
                });
                result.Add(new RemoteImageInfo
                {
                    Url = posterNode.Attributes["src"].Value,
                    Type = ImageType.Backdrop,
                });
            }
            else
            {
                posterNode = sceneData.SelectSingleNode("//section[@class='section-intro']//div[@class='feature-image']//div[contains(@class, 'video-player-container')]");
                var posterUrl = posterNode.Attributes["data-poster"].Value;
                posterUrl = posterUrl.Replace("&amp;", "&");
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
            }

            var galleryImages = sceneData.SelectNodesSafe("//section[contains(@class, 'section-images')]//div[contains(@class, 'tile-image')]/img");
            foreach (var image in galleryImages)
            {
                result.Add(new RemoteImageInfo
                {
                    Url = image.Attributes["src"].Value,
                    Type = ImageType.Primary,
                });
                result.Add(new RemoteImageInfo
                {
                    Url = image.Attributes["src"].Value,
                    Type = ImageType.Backdrop,
                });
            }

            return result;
        }
    }
}
