using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class NetworkFemdomEmpire : IProviderBase
    {
        private static readonly Dictionary<string, (string curID, string name)> ManualMatch = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            { "Extreme Strap on Training", ("https://femdomempire.com/tour/trailers/EXTREMEStrap-OnTraining.html", "EXTREME Strap-On Training [Femdom Empire] 2012-04-11") },
            { "Cock Locked", ("https://femdomempire.com/tour/trailers/CockLocked.html", "Cock Locked [Femdom Empire] 2012-04-20") },
            { "Oral Servitude", ("https://femdomempire.com/tour/trailers/OralServitude.html", "Oral Servitude [Femdom Empire] 2012-04-08") },
        };

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null)
            {
                return result;
            }

            if (ManualMatch.TryGetValue(searchTitle, out var match))
            {
                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, Helper.Encode(match.curID) } },
                    Name = match.name,
                    SearchProviderName = Plugin.Instance.Name,
                });
                return result;
            }

            var url = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(searchTitle);
            var data = await HTML.ElementFromURL(url, cancellationToken);
            if (data == null)
            {
                return result;
            }

            var searchResults = data.SelectNodes("//div[contains(@class, 'item-info')]");
            if (searchResults == null)
            {
                return result;
            }

            foreach (var searchResult in searchResults)
            {
                var sceneURLNode = searchResult.SelectSingleNode(".//a");
                var sceneDateNode = searchResult.SelectSingleNode(".//span[@class='date']");
                if (sceneURLNode == null || sceneDateNode == null)
                {
                    continue;
                }

                string curID = Helper.Encode(sceneURLNode.GetAttributeValue("href", string.Empty));
                string sceneName = sceneURLNode.InnerText.Trim();

                if (DateTime.TryParseExact(sceneDateNode.InnerText.Trim(), "MMMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var releaseDate))
                {
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = $"{sceneName} [Femdom Empire] {releaseDate:yyyy-MM-dd}",
                        SearchProviderName = Plugin.Instance.Name,
                        PremiereDate = releaseDate,
                    });
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

            string sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (sceneData == null)
            {
                return result;
            }

            var movie = (Movie)result.Item;
            movie.Name = sceneData.SelectSingleNode("//div[contains(@class, 'videoDetails')]//h3")?.InnerText.Trim();
            movie.Overview = sceneData.SelectSingleNode("//div[contains(@class, 'videoDetails')]//p")?.InnerText.Trim();
            movie.AddStudio("Femdom Empire");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);

            var dateNode = sceneData.SelectSingleNode("//div[@class='videoInfo clear']//p")?.InnerText.Replace("Date Added:", string.Empty).Trim();
            if (DateTime.TryParseExact(dateNode, "MMMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
            {
                movie.PremiereDate = sceneDateObj;
                movie.ProductionYear = sceneDateObj.Year;
            }

            var genreNodes = sceneData.SelectNodes("//div[contains(@class, 'featuring')][2]//ul//li");
            if (genreNodes != null)
            {
                foreach (var genreLink in genreNodes)
                {
                    movie.AddGenre(Helper.ParseTitle(genreLink.InnerText.Trim().ToLower().Replace("categories:", string.Empty).Replace("tags:", string.Empty), siteNum));
                }
            }

            movie.AddGenre("Femdom");

            var actorsNode = sceneData.SelectNodes("//div[contains(@class, 'featuring')][1]/ul/li/a");
            if (actorsNode != null)
            {
                foreach (var actorLink in actorsNode)
                {
                    result.People.Add(new PersonInfo { Name = actorLink.InnerText.Trim(), Type = PersonKind.Actor });
                }
            }

            if (movie.Name.Equals("Owned by Alexis", StringComparison.OrdinalIgnoreCase))
            {
                result.People.Add(new PersonInfo { Name = "Alexis Monroe", Type = PersonKind.Actor });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            string sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (sceneData == null)
            {
                return result;
            }

            var imageNode = sceneData.SelectSingleNode("//a[@class='fake_trailer']//img");
            if (imageNode != null)
            {
                string image = imageNode.GetAttributeValue("src0_1x", string.Empty);
                if (!string.IsNullOrEmpty(image))
                {
                    if (!image.StartsWith("http"))
                    {
                        image = Helper.GetSearchBaseURL(siteNum) + image;
                    }

                    result.Add(new RemoteImageInfo { Url = image, Type = ImageType.Primary });
                    result.Add(new RemoteImageInfo { Url = image, Type = ImageType.Backdrop });
                }
            }

            return result;
        }
    }
}
