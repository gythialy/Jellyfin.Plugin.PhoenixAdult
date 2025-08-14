using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
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
    public class NetworkDirtyFlix : IProviderBase
    {
        private static readonly Dictionary<string, List<string>> genresDB = new Dictionary<string, List<string>>
        {
            {"Trick Your GF", new List<string> {"Girlfriend", "Revenge"}},
            {"Make Him Cuckold", new List<string> {"Cuckold"}},
            {"She Is Nerdy", new List<string> {"Glasses", "Nerd"}},
            {"Tricky Agent", new List<string> {"Agent", "Casting"}},
        };

        private static readonly Dictionary<string[], string[]> xPathDB = new Dictionary<string[], string[]>
        {
            {new[] {"Trick Your GF", "Make Him Cuckold"}, new[] {".//a[contains(@class, 'link')]", ".//div[@class='description']"}},
            {new[] {"She Is Nerdy"}, new[] {".//a[contains(@class, 'title')]", ".//div[@class='description']"}},
            {new[] {"Tricky Agent"}, new[] {".//h3", ".//div[@class='text']"}},
        };

        private static readonly Dictionary<string, Tuple<int, int>> siteDB = new Dictionary<string, Tuple<int, int>>
        {
            {"Trick Your GF", new Tuple<int, int>(7, 4)},
            {"Make Him Cuckold", new Tuple<int, int>(9, 5)},
            {"She Is Nerdy", new Tuple<int, int>(10, 12)},
            {"Tricky Agent", new Tuple<int, int>(11, 4)},
        };

        private static readonly Dictionary<string, List<string>> sceneActorsDB = new Dictionary<string, List<string>>
        {
            {"snc162", new List<string> {"Adele Hotness"}},
            {"darygf050", new List<string> {"Adina"}},
            {"wrygf726", new List<string> {"Aggie"}},
            {"wtag728", new List<string> {"Aggie"}},
            {"pfc070", new List<string> {"Aimee Ryan"}},
        };

        public Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            // The search logic is very complex and seems to rely on iterating through all pages of the site.
            // This is not practical for a Jellyfin provider.
            // A direct search by title is not available on the site.
            // Returning an empty list.
            return Task.FromResult(new List<RemoteSearchResult>());
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem>()
            {
                Item = new Movie(),
                People = new List<PersonInfo>(),
            };

            string[] providerIds = sceneID[0].Split('|');
            string sceneId = Helper.Decode(providerIds[0]);
            string sceneDate = providerIds.Length > 2 ? providerIds[2] : null;
            string searchPageUrl = providerIds.Length > 3 ? Helper.Decode(providerIds[3]) : null;

            var httpResult = await HTTP.Request(searchPageUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return result;
            var detailsPageElements = (await HTML.ElementFromString(httpResult.Content, cancellationToken))
                .SelectSingleNode($"//div[@class='movie-block'][.//*[contains(@src, \"{sceneId}\")]]");
            if (detailsPageElements == null) return result;

            var xPath = xPathDB.FirstOrDefault(kvp => kvp.Key.Contains(Helper.GetSearchSiteName(siteNum))).Value;

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode(xPath[0])?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectSingleNode(xPath[1])?.InnerText.Trim();
            movie.AddStudio("Dirty Flix");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);
            movie.AddCollection(new[] { tagline });

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            if (genresDB.ContainsKey(tagline))
            {
                foreach(var genre in genresDB[tagline])
                    movie.AddGenre(genre);
            }

            if (sceneActorsDB.Values.Any(v => v.Contains(sceneId)))
            {
                var actorName = sceneActorsDB.FirstOrDefault(kvp => kvp.Value.Contains(sceneId)).Key;
                result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string[] providerIds = sceneID[0].Split('|');
            string sceneId = Helper.Decode(providerIds[0]);
            string searchPageUrl = providerIds.Length > 3 ? Helper.Decode(providerIds[3]) : null;

            var httpResult = await HTTP.Request(searchPageUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return images;
            var detailsPageElements = (await HTML.ElementFromString(httpResult.Content, cancellationToken))
                .SelectSingleNode($"//div[@class='movie-block'][.//*[contains(@src, \"{sceneId}\")]]");
            if (detailsPageElements == null) return images;

            var imageNode = detailsPageElements.SelectSingleNode(".//img");
            if (imageNode != null)
                images.Add(new RemoteImageInfo { Url = imageNode.GetAttributeValue("src", "") });

            if (images.Any())
                images.First().Type = ImageType.Primary;

            return images;
        }
    }
}
