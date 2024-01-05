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
    public class SiteFamilyTherapyXXX : IProviderBase
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

            var searchResults = data.SelectNodesSafe("//article[contains(@class, 'post')]");
            foreach (var searchResult in searchResults)
            {
                var sceneLink = searchResult.SelectSingleNode(".//h2[@class='entry-title']//a");
                var sceneURL = sceneLink.Attributes["href"].Value;
                var sceneName = sceneLink.InnerText.Trim();
                var curID = Helper.Encode(sceneURL);
                var image = searchResult.SelectSingleNode(".//a[@class='entry-featured-image-url']/img");
                var scenePoster = image.Attributes["src"].Value;
                var date = searchResult.SelectSingleNode(".//p[@class='post-meta']//span[@class='published']");
                var sceneDate = date.InnerText.Trim();

                var res = new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, curID } },
                    Name = sceneName,
                    ImageUrl = scenePoster,
                };

                if (DateTime.TryParseExact(sceneDate, "MMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
                {
                    res.PremiereDate = sceneDateObj;
                }

                result.Add(res);
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
            result.Item.AddStudio("Family Therapy XXX");

            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken).ConfigureAwait(false);

            result.Item.Name = sceneData.SelectSingleText("//h1[@class='entry-title']");

            var dateNode = sceneData.SelectSingleText("//p[@class='post-meta']//span[@class='published']");
            if (DateTime.TryParseExact(dateNode, "MMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
            {
                result.Item.PremiereDate = sceneDateObj;
            }

            var genreNodes = sceneData.SelectNodesSafe("//p[@class='post-meta']//a[@rel='category tag']");
            foreach (var genreNode in genreNodes)
            {
                result.Item.AddGenre(genreNode.InnerText.Trim());
            }

            var descriptionNodes = sceneData.SelectNodesSafe("//div[@class='entry-content']//p");
            var overview = descriptionNodes[0].InnerText.Trim();
            result.Item.Overview = overview;

            // performers
            // example ***Starring Sidney Summers & Susie Stellar***
            var performersText = descriptionNodes[1].InnerText.Trim();
            performersText = performersText.Trim('*');
            performersText = performersText.Substring("Starring".Length + 1);

            var performerNames = performersText.Split(" & ");

            foreach (var performerName in performerNames)
            {
                result.AddPerson(new PersonInfo
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

            var sceneName = sceneData.SelectSingleText("//h1[@class='entry-title']");

            // unable to get video poster from scene page, so performing search to get that poster
            var searchResults = await this.Search(siteNum, sceneName, null, cancellationToken).ConfigureAwait(false);
            var sceneResult = searchResults.First(x => x.Name == sceneName);

            result.Add(new RemoteImageInfo
            {
                Url = sceneResult.ImageUrl,
                Type = ImageType.Primary,
            });

            return result;
        }
    }
}
