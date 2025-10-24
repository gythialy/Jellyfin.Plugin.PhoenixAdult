using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Helpers.Utils;
using MediaBrowser.Model.Entities;


#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class SiteJacquieEtMichel : IProviderBase
    {
        private const string SiteName = "Jacquie Et Michel";
        private const string BaseUrl = "https://www.jacquieetmichel.net";

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var sceneID = string.Empty;
            var parts = searchTitle.Split(' ');
            if (int.TryParse(parts[0], out _))
            {
                sceneID = parts[0];
            }

            var searchUrl = $"{BaseUrl}/en/videos/search/{searchTitle.Replace(" ", "+")}";
            var doc = await HTML.ElementFromURL(searchUrl, cancellationToken);

            var searchResults = new List<RemoteSearchResult>();
            var nodes = doc.SelectNodes("//a[@class='content-card content-card--video']");
            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    var titleNoFormatting = node.SelectSingleNode(".//h2[@class='content-card__title']").InnerText.Trim();
                    var curID = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(node.GetAttributeValue("href", string.Empty)));
                    var date = node.SelectSingleNode(".//div[@class='content-card__date']").InnerText.Replace("Added on", string.Empty).Trim();
                    var releaseDate = DateTime.Parse(date).ToString("yyyy-MM-dd");

                    searchResults.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}" } },
                        Name = $"{titleNoFormatting} [{SiteName}] {releaseDate}",
                    });
                }
            }

            if (!string.IsNullOrEmpty(sceneID))
            {
                var sceneUrl = $"{BaseUrl}/en/content/{sceneID}";
                var sceneDoc = await HTML.ElementFromURL(sceneUrl, cancellationToken);

                var titleNoFormatting = sceneDoc.SelectSingleNode("//h1[@class='content-detail__title']").InnerText.Trim();
                var curID = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(sceneUrl));

                searchResults.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}" } },
                    Name = $"{titleNoFormatting} [{SiteName}]",
                });
            }

            return searchResults;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var metadataId = sceneID[0].Split('|');
            var sceneUrl = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(metadataId[0]));
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = $"{BaseUrl}{sceneUrl}";
            }

            var doc = await HTML.ElementFromURL(sceneUrl, cancellationToken);

            var metadataResult = new MetadataResult<BaseItem>
            {
                Item = new Movie(),
                HasMetadata = true,
            };

            metadataResult.Item.Name = doc.SelectSingleNode("//h1[@class='content-detail__title']").InnerText.Trim();
            metadataResult.Item.Overview = doc.SelectSingleNode("//div[@class='content-detail__description']").InnerText.Trim();
            metadataResult.Item.OfficialRating = "XXX";
            metadataResult.Item.SetProviderId(Plugin.Instance.Name, sceneUrl);

            var date = doc.SelectNodes("//div[@class='content-detail__infos__row']//p[@class='content-detail__description content-detail__description--link']")[1].InnerText.Trim();
            metadataResult.Item.PremiereDate = DateTime.Parse(date);

            var genres = doc.SelectNodes("//div[@class='content-detail__row']//li[@class='content-detail__tag']");
            if (genres != null)
            {
                foreach (var genre in genres)
                {
                    var genreName = genre.InnerText.Replace(",", string.Empty).Trim();
                    if (genreName == "Sodomy")
                    {
                        genreName = "Anal";
                    }

                    metadataResult.Item.AddGenre(genreName);
                }
            }

            metadataResult.Item.AddGenre("French porn");

            foreach (var actorName in GetJmtvActors(sceneUrl))
            {
                metadataResult.AddPerson(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
            }

            return metadataResult;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var metadataId = sceneID[0].Split('|');
            var sceneUrl = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(metadataId[0]));
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = $"{BaseUrl}{sceneUrl}";
            }

            var doc = await HTML.ElementFromURL(sceneUrl, cancellationToken);

            var img = doc.SelectSingleNode("//video").GetAttributeValue("poster", string.Empty);

            var list = new List<RemoteImageInfo>
            {
                new RemoteImageInfo
                {
                    Url = img,
                    Type = ImageType.Primary,
                },
            };

            return list;
        }

        private static int LevenshteinDistance(string source, string target)
        {
            if (string.IsNullOrEmpty(source))
            {
                return string.IsNullOrEmpty(target) ? 0 : target.Length;
            }

            if (string.IsNullOrEmpty(target))
            {
                return source.Length;
            }

            var distance = new int[source.Length + 1, target.Length + 1];

            for (var i = 0; i <= source.Length; distance[i, 0] = i++)
            {
            }

            for (var j = 0; j <= target.Length; distance[0, j] = j++)
            {
            }

            for (var i = 1; i <= source.Length; i++)
            {
                for (var j = 1; j <= target.Length; j++)
                {
                    var cost = (target[j - 1] == source[i - 1]) ? 0 : 1;
                    distance[i, j] = Math.Min(Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1), distance[i - 1, j - 1] + cost);
                }
            }

            return distance[source.Length, target.Length];
        }

        private static IEnumerable<string> GetJmtvActors(string url)
        {
            var scenes = new Dictionary<string, string[]>
            {
                { "4554/ibiza-1-crumb-in-the-mouth", new[] { "Alexis Crystal", "Cassie Del Isla", "Dorian Del Isla" } },
                { "4558/orgies-in-ibiza-2-lucys-surprise", new[] { "Alexis Crystal", "Cassie Del Isla", "Lucy Heart", "Dorian Del Isla", "James Burnett Klein", "Vlad Castle" } },
                { "4564/orgies-in-ibiza-3-overheated-orgy-by-the-pool", new[] { "Alexis Crystal", "Cassie Del Isla", "Lucy Heart", "Dorian Del Isla", "James Burnett Klein", "Vlad Castle" } },
                { "4570/orgies-in-ibiza-4-orgy-with-a-bang-for-the-last-night", new[] { "Alexis Crystal", "Cassie Del Isla", "Lucy Heart", "Dorian Del Isla", "James Burnett Klein", "Vlad Castle" } },
            };

            return scenes.Where(scene => url.Contains(scene.Key)).Select(scene => scene.Value).FirstOrDefault();
        }
    }
}
