using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
    public class NetworkThickCashOther : IProviderBase
    {
        private string Slugify(string phrase)
        {
            string str = phrase.ToLowerInvariant();
            str = System.Text.Encoding.ASCII.GetString(System.Text.Encoding.GetEncoding("Cyrillic").GetBytes(str));
            str = System.Text.RegularExpressions.Regex.Replace(str, @"[^a-z0-9\s-]", string.Empty);
            str = System.Text.RegularExpressions.Regex.Replace(str, @"\s+", " ").Trim();
            str = str.Substring(0, str.Length <= 45 ? str.Length : 45).Trim();
            str = System.Text.RegularExpressions.Regex.Replace(str, @"\s", "-");
            return str;
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string directUrl = $"{Helper.GetSearchBaseURL(siteNum)}/videos/{Slugify(searchTitle)}.html";
            var searchResults = new List<string> { directUrl };

            var googleResults = await WebSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            searchResults.AddRange(googleResults.Where(u => u.Contains("/videos/")));

            foreach (var sceneUrl in searchResults.Distinct())
            {
                try
                {
                    var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                    if (httpResult.IsOK)
                    {
                        var scenePageElements = HTML.ElementFromString(httpResult.Content);
                        string titleNoFormatting = scenePageElements.SelectSingleNode("//h3[@class='top-title']")?.InnerText.Trim();
                        string curId = Helper.Encode(sceneUrl);
                        string releaseDate = searchDate?.ToString("yyyy-MM-dd") ?? string.Empty;

                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}" } },
                            Name = $"{titleNoFormatting} [Thick Cash/{Helper.GetSearchSiteName(siteNum)}]",
                            SearchProviderName = Plugin.Instance.Name,
                        });
                    }
                }
                catch { }
            }

            string modelId;
            try
            {
                modelId = string.Join("-", searchTitle.Split(' ').Take(2));
            }
            catch
            {
                modelId = searchTitle.Split(' ').First();
            }

            string url = $"{Helper.GetSearchSearchURL(siteNum)}{modelId}.html";
            var modelHttp = await HTTP.Request(url, HttpMethod.Get, cancellationToken);
            if (modelHttp.IsOK)
            {
                var modelPageElements = HTML.ElementFromString(modelHttp.Content);
                var modelNodes = modelPageElements.SelectNodes("//div[@class='model-grid']//a");
                if (modelNodes != null)
                {
                    foreach (var node in modelNodes)
                    {
                        string sceneUrl = node.GetAttributeValue("href", string.Empty);
                        if (!searchResults.Contains(sceneUrl))
                        {
                            string titleNoFormatting = node.SelectSingleNode(".//h5")?.InnerText.Trim();
                            string curId = Helper.Encode(sceneUrl);
                            string releaseDate = searchDate?.ToString("yyyy-MM-dd") ?? string.Empty;
                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}" } },
                                Name = $"{titleNoFormatting} [Thick Cash/{Helper.GetSearchSiteName(siteNum)}]",
                                SearchProviderName = Plugin.Instance.Name,
                            });
                        }
                    }
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

            string[] providerIds = sceneID[0].Split('|');
            string sceneUrl = Helper.Decode(providerIds[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            string sceneDate = providerIds.Length > 2 ? providerIds[2] : null;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//h3[@class='top-title']")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//div[@class='player-box']//p")?.InnerText.Trim();
            movie.AddStudio("Thick Cash");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var actorNodes = detailsPageElements.SelectNodes("//a[@class='tag'][contains(@href, 'models')]");
            if (actorNodes != null)
            {
                foreach (var actor in actorNodes)
                {
                    result.People.Add(new PersonInfo { Name = actor.InnerText.Trim(), Type = PersonKind.Actor });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var posterNode = detailsPageElements.SelectSingleNode("//video");
            if (posterNode != null)
            {
                images.Add(new RemoteImageInfo { Url = posterNode.GetAttributeValue("poster", string.Empty), Type = ImageType.Primary });
            }

            return images;
        }
    }
}
