using System;
using System.Collections.Generic;
using System.Globalization;
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
    public class SiteAdultEmpire : IProviderBase
    {
        private readonly Dictionary<string, string> _cookies = new Dictionary<string, string> { { "ageConfirmed", "true" } };

        private (DateTime? releaseDate, string displayDate) GetReleaseDateAndDisplayDate(HtmlNode detailsPageElements, DateTime? searchDate = null)
        {
            DateTime? releaseDate = null;
            string displayDate = string.Empty;

            var dateNode = detailsPageElements?.SelectSingleNode("//li[contains(., 'Released:')]/text()");
            string dateStr = dateNode?.InnerText.Trim();

            if (!string.IsNullOrEmpty(dateStr) && dateStr != "unknown")
            {
                if (DateTime.TryParseExact(dateStr, "MMM d yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                {
                    releaseDate = parsedDate;
                    displayDate = releaseDate.Value.ToString("yyyy-MM-dd");
                }
            }
            else if (searchDate.HasValue)
            {
                releaseDate = searchDate;
                displayDate = releaseDate.Value.ToString("yyyy-MM-dd");
            }

            return (releaseDate, displayDate);
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();

            string sceneId = null;
            var parts = searchTitle.Split(' ');
            if (int.TryParse(parts[0], out var id) && id > 100)
            {
                sceneId = parts[0];
                searchTitle = string.Join(" ", parts.Skip(1));
            }

            var searchUrls = new List<string>();
            if (sceneId != null)
            {
                searchUrls.Add($"{Helper.GetSearchBaseURL(siteNum)}/{sceneId}");
            }
            else
            {
                string encodedTitle = searchTitle.Replace("&", "").Replace("'", "").Replace(",", "").Replace("#", "").Replace(" ", "+");
                string searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{encodedTitle}";

                var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken, new Dictionary<string, string> { { "Referer", "http://www.data18.empirestores.co" } }, _cookies);
                if (httpResult.IsOK)
                {
                    var searchPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);
                    var searchResultNodes = searchPageElements.SelectNodes("//div[@class='product-details__item-title']");
                    if (searchResultNodes != null)
                    {
                        foreach (var searchResult in searchResultNodes)
                        {
                            var urlNode = searchResult.SelectSingleNode(".//a");
                            if (urlNode != null)
                            {
                                string movieUrl = $"{Helper.GetSearchBaseURL(siteNum)}/{urlNode.GetAttributeValue("href", "").Split('/')[1]}";
                                if (!searchUrls.Contains(movieUrl))
                                    searchUrls.Add(movieUrl);
                            }
                        }
                    }
                }
            }

            foreach (var movieUrl in searchUrls)
            {
                var httpResult = await HTTP.Request(movieUrl, HttpMethod.Get, cancellationToken, null, _cookies);
                if (!httpResult.IsOK)
                    continue;

                var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);
                if (detailsPageElements == null)
                    continue;

                string urlId = new Uri(movieUrl).Segments.Last();
                string titleNoFormatting = detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim();
                string curId = Helper.Encode(movieUrl);
                var (releaseDate, displayDate) = GetReleaseDateAndDisplayDate(detailsPageElements, searchDate);
                string studio = detailsPageElements.SelectSingleNode("//li[contains(., 'Studio:')]/a")?.InnerText.Trim() ?? "";

                var item = new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate?.ToString("yyyy-MM-dd")}" } },
                    Name = $"{titleNoFormatting} [{studio}] {displayDate}",
                    SearchProviderName = Plugin.Instance.Name,
                };
                result.Add(item);

                var availableScenes = detailsPageElements.SelectNodes("//div[@class='row'][.//h3]");
                if(availableScenes != null)
                {
                    for (int i = 0; i < availableScenes.Count; i++)
                    {
                        var scene = availableScenes[i];
                        string actorNames = string.Join(", ", scene.SelectNodes(".//div/a")?.Select(a => a.InnerText.Trim()) ?? new string[0]);
                        if (string.IsNullOrEmpty(actorNames))
                            actorNames = scene.SelectSingleNode(".//a")?.InnerText.Trim() ?? "";

                        var sceneItem = new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate?.ToString("yyyy-MM-dd")}|{i+1}|{i}" } },
                            Name = $"{titleNoFormatting}/#{i+1}[{actorNames}][{studio}] {displayDate}",
                            SearchProviderName = Plugin.Instance.Name,
                        };
                        result.Add(sceneItem);
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
            DateTime? sceneDate = providerIds.Length > 2 && DateTime.TryParse(providerIds[2], out var parsedDate) ? parsedDate : (DateTime?)null;
            bool isSplitScene = providerIds.Length > 3;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken, null, _cookies);
            if (!httpResult.IsOK) return result;

            var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);
            if (detailsPageElements == null) return result;

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim();
            if (isSplitScene)
                movie.Name = $"{movie.Name} [Scene {providerIds[3]}]";

            movie.Overview = detailsPageElements.SelectSingleNode("//div[@class='container'][.//h2]//parent::p")?.InnerText.Trim();

            string studio = detailsPageElements.SelectSingleNode("//li[contains(., 'Studio:')]/a")?.InnerText.Trim() ?? "";
            movie.AddStudio(studio);

            var taglineNode = detailsPageElements.SelectSingleNode("//h2/a[@label='Series']");
            if (taglineNode != null)
            {
                string tagline = Regex.Replace(taglineNode.InnerText.Trim().Split('"')[1], @"\(.*\)", "").Trim();
                movie.AddTag(tagline);
                movie.AddCollection(new[] {tagline});
            }

            if (sceneDate.HasValue)
            {
                movie.PremiereDate = sceneDate.Value;
                movie.ProductionYear = sceneDate.Value.Year;
            }
            else
            {
                var (releaseDate, _) = GetReleaseDateAndDisplayDate(detailsPageElements);
                if (releaseDate.HasValue)
                {
                    movie.PremiereDate = releaseDate.Value;
                    movie.ProductionYear = releaseDate.Value.Year;
                }
            }

            var genreNodes = detailsPageElements.SelectNodes("//li//a[@label='Category']");
            if (genreNodes != null)
            {
                foreach(var genreNode in genreNodes)
                    movie.AddGenre(genreNode.InnerText.Trim());
            }

            var actorNodes = new List<HtmlNode>();
            if (isSplitScene)
            {
                int sceneIndex = int.Parse(providerIds[4]);
                var sceneNode = detailsPageElements.SelectNodes("//div[@class='row'][.//h3]")?[sceneIndex];
                if (sceneNode != null)
                    actorNodes.AddRange(sceneNode.SelectNodes(".//div/a") ?? new HtmlNodeCollection(null));
            }

            if (!actorNodes.Any())
                actorNodes.AddRange(detailsPageElements.SelectNodes("//div[contains(., 'Starring')][1]/a[contains(@href, 'pornstars')]") ?? new HtmlNodeCollection(null));

            foreach (var actorNode in actorNodes)
            {
                string actorName = actorNode.InnerText.Split('(')[0].Trim();
                string actorPhotoUrl = detailsPageElements.SelectSingleNode($"//div[contains(., 'Starring')]//img[contains(@title, \"{actorName}\")]")?.GetAttributeValue("src", "");
                if (!string.IsNullOrEmpty(actorName))
                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string[] providerIds = sceneID[0].Split('|');
            string sceneUrl = Helper.Decode(providerIds[0]);
            bool isSplitScene = providerIds.Length > 3;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken, null, _cookies);
            if (!httpResult.IsOK) return images;

            var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);
            if (detailsPageElements == null) return images;

            var posterNodes = detailsPageElements.SelectNodes("//div[@class='boxcover-container']/a/img | //div[@class='boxcover-container']/a");
            if (posterNodes != null)
            {
                foreach (var posterNode in posterNodes)
                {
                    string imageUrl = posterNode.GetAttributeValue("src", "") ?? posterNode.GetAttributeValue("href", "");
                    if (!string.IsNullOrEmpty(imageUrl))
                        images.Add(new RemoteImageInfo { Url = imageUrl, Type = ImageType.Primary });
                }
            }

            if (isSplitScene)
            {
                int sceneIndex = int.Parse(providerIds[4]);
                var sceneImageNodes = detailsPageElements.SelectNodes($"//div[@class='row'][.//div[@class='row']][.//a[@rel='scenescreenshots']][{sceneIndex+1}]//a");
                 if (sceneImageNodes != null)
                 {
                     foreach(var img in sceneImageNodes)
                        images.Add(new RemoteImageInfo { Url = img.GetAttributeValue("href",""), Type = ImageType.Backdrop });
                 }
            }
            else
            {
                var sceneImageNodes = detailsPageElements.SelectNodes("//div[@class='row'][.//div[@class='row']][.//a[@rel='scenescreenshots']]//div[@class='row']//a");
                 if (sceneImageNodes != null)
                 {
                     foreach(var img in sceneImageNodes)
                        images.Add(new RemoteImageInfo { Url = img.GetAttributeValue("href",""), Type = ImageType.Backdrop });
                 }
            }

            return images;
        }
    }
}
