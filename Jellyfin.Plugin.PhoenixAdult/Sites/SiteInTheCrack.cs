using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    public class SiteInTheCrack : IProviderBase
    {
        private const string SiteName = "InTheCrack";
        private const string BaseUrl = "https://www.inthecrack.com";

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var sceneID = string.Empty;
            var searchResults = new List<RemoteSearchResult>();
            var all = string.Empty.PadRight(127, (char)0);
            var nodigs = all.Where(c => !char.IsDigit(c)).ToString();

            try
            {
                var sceneTitle = searchTitle.Split(' ')[0];
                sceneID = new string(sceneTitle.Where(c => !char.IsDigit(c)).ToArray());
                if (!string.IsNullOrEmpty(sceneID))
                {
                    searchTitle = searchTitle.Split(' ')[1];
                }

                searchTitle = searchTitle.ToLower();
            }
            catch
            {
                // ignored
            }

            var doc = await HTML.ElementFromURL($"{BaseUrl}/collections/{searchTitle[0]}", cancellationToken);

            var nodes = doc.SelectNodes("//ul[@class='collectionGridLayout']/li");
            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    var discoveredName = node.SelectSingleNode(".//span").InnerText.Trim().ToLower();
                    if (discoveredName.Contains(searchTitle))
                    {
                        var modelLink = node.SelectSingleNode(".//a").GetAttributeValue("href", string.Empty);
                        var modelDoc = await HTML.ElementFromURL($"{BaseUrl}{modelLink}", cancellationToken);

                        var modelNodes = modelDoc.SelectNodes("//ul[@class='Models']/li");
                        if (modelNodes != null)
                        {
                            foreach (var modelNode in modelNodes)
                            {
                                var titleNoFormatting = modelNode.SelectSingleNode(".//figure/p[1]").InnerText.Replace("Collection: ", "").Trim();
                                var titleNoFormattingID = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(titleNoFormatting));

                                var date = modelNode.SelectSingleNode(".//figure/p[2]").InnerText.Replace("Release Date:", "").Trim();
                                var releaseDate = !string.IsNullOrEmpty(date) ? DateTime.Parse(date).ToString("yyyy-MM-dd") : string.Empty;

                                var curID = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(modelNode.SelectSingleNode(".//a").GetAttributeValue("href", string.Empty)));

                                var score = 100 - LevenshteinDistance(sceneID, titleNoFormatting.ToLower());

                                searchResults.Add(new RemoteSearchResult
                                {
                                    Id = $"{curID}|{siteNum[0]}|{titleNoFormattingID}|{releaseDate}",
                                    Name = $"{titleNoFormatting} {releaseDate} [{SiteName}]",
                                    Score = score
                                });
                            }
                        }
                    }
                }
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
                Item = new BaseItem(),
                HasMetadata = true
            };

            metadataResult.Item.Name = doc.SelectSingleNode("//h2//span").InnerText.Trim();

            var summaryNode = doc.SelectSingleNode("//p[@id='CollectionDescription']");
            if (summaryNode != null)
            {
                metadataResult.Item.Overview = summaryNode.InnerText.Trim();
            }

            metadataResult.Item.OfficialRating = "XXX";
            metadataResult.Item.SetProviderId(Plugin.Instance.Name, sceneUrl);

            var date = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(metadataId[3]));
            if (!string.IsNullOrEmpty(date))
            {
                metadataResult.Item.PremiereDate = DateTime.Parse(date);
            }

            var actorStr = doc.SelectSingleNode("//title").InnerText.Split('#')[1];
            actorStr = new string(actorStr.Where(c => !char.IsDigit(c)).ToArray()).Trim();
            actorStr = actorStr.Replace(",", "&");
            var actorList = actorStr.Split('&');

            foreach (var actorLink in actorList)
            {
                var actorName = actorLink.Trim();
                metadataResult.AddPerson(new PersonInfo { Name = actorName, Type = PersonType.Actor });
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

            var scenePic = $"{BaseUrl}{doc.SelectSingleNode("//style").InnerText.Split('\'')[1].Trim()}";

            var list = new List<RemoteImageInfo>
            {
                new RemoteImageInfo
                {
                    Url = scenePic,
                    Type = ImageType.Primary
                }
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
    }
}
