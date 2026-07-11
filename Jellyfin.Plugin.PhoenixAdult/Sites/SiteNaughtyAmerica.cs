using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
using Jellyfin.Data.Enums;

namespace PhoenixAdult.Sites
{
    public class SiteNaughtyAmerica : IProviderBase
    {
        private class NaughtyAmericaScene
        {
            public string Id { get; set; }

            public string Title { get; set; }

            public string Site { get; set; }

            public DateTime PublishedAt { get; set; }

            public List<string> Fantasies { get; set; }

            public List<string> Performers { get; set; }

            public string Synopsis { get; set; }

            public List<string> Photos { get; set; }
        }

        private async Task<NaughtyAmericaScene> GetNaughtyAmerica(string sceneId, CancellationToken cancellationToken)
        {
            var scenePageElements = await HTML.ElementFromURL($"https://www.naughtyamerica.com/scene/0{sceneId}", cancellationToken, forceFlareSolverr: true);
            if (scenePageElements == null)
            {
                return null;
            }

            var photoElements = scenePageElements.SelectNodes("//div[contains(@class, 'contain-scene-images') and contains(@class, 'desktop-only')]/a/@href");
            var photos = photoElements?.Select(photo => "https:" + new Regex(@"images\d+").Replace(photo.GetAttributeValue("href", string.Empty), "images1", 1)).ToList() ?? new List<string>();

            string dateStr = scenePageElements.SelectSingleNode("//div[contains(@class, 'date-tags')]//span")?.InnerText?.Trim();
            DateTime.TryParse(dateStr, out var publishedAt);

            return new NaughtyAmericaScene
            {
                Id = sceneId,
                Title = scenePageElements.SelectSingleNode("//div[contains(@class, 'scene-info')]//h1")?.InnerText?.Trim(),
                Site = scenePageElements.SelectSingleNode("//a[@class='site-title grey-text link']")?.InnerText?.Trim(),
                PublishedAt = publishedAt,
                Fantasies = scenePageElements.SelectNodes("//div[contains(@class, 'categories')]//a")?.Select(n => n.InnerText.Trim()).ToList() ?? new List<string>(),
                Performers = scenePageElements.SelectNodes("//div[contains(@class, 'performer-list')]//a")?.Select(n => n.InnerText.Trim()).ToList() ?? new List<string>(),
                Synopsis = scenePageElements.SelectSingleNode("//div[contains(@class, 'synopsis')]")?.InnerText?.Replace("Synopsis:", string.Empty).Trim(),
                Photos = photos,
            };
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            string sceneID = searchTitle.Split(' ')[0];
            if (!int.TryParse(sceneID, out _))
            {
                sceneID = null;
            }

            if (!string.IsNullOrEmpty(sceneID))
            {
                var scenePageElements = await GetNaughtyAmerica(sceneID, cancellationToken);
                if (scenePageElements != null)
                {
                    string releaseDate = scenePageElements.PublishedAt != default ? scenePageElements.PublishedAt.ToString("yyyy-MM-dd") : string.Empty;
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{scenePageElements.Id}" } },
                        Name = $"{scenePageElements.Title} [{scenePageElements.Site?.Replace("&#039;", "'", StringComparison.OrdinalIgnoreCase) ?? string.Empty}] {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }
            }
            else
            {
                string searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{searchTitle.Slugify()}&_gl=1";
                var searchResultsNode = await HTML.ElementFromURL(searchUrl, cancellationToken, forceFlareSolverr: true);
                if (searchResultsNode == null)
                {
                    return result;
                }

                var lastPageMatch = new Regex(@"\d+(?=#)").Match(searchResultsNode.SelectSingleNode("//li/a[./i[contains(@class, 'double')]]")?.GetAttributeValue("href", string.Empty) ?? string.Empty);
                int pagination = lastPageMatch.Success ? int.Parse(lastPageMatch.Value) + 2 : 3;

                string searchXPath = searchUrl.Contains("pornstar") ? "//div[contains(@class, 'scene-item')]" : "//div[@class='scene-grid-item']";

                for (int i = 2; i < pagination; i++)
                {
                    var searchResults = searchResultsNode.SelectNodes(searchXPath);
                    if (searchResults != null)
                    {
                        foreach (var searchResult in searchResults)
                        {
                            string titleNoFormatting = searchResult.SelectSingleNode("./a")?.GetAttributeValue("title", string.Empty).Trim();
                            int curID = int.Parse(searchResult.SelectSingleNode("./a")?.GetAttributeValue("data-scene-id", string.Empty));
                            string releaseDate = string.Empty;
                            string entryDateStr = searchResult.SelectSingleNode("./p[@class='entry-date']")?.InnerText;
                            if (DateTime.TryParse(entryDateStr, out var parsedDate))
                            {
                                releaseDate = parsedDate.ToString("yyyy-MM-dd");
                            }
                            string siteName = searchResult.SelectSingleNode(".//a[@class='site-title']")?.InnerText;
                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, $"{curID}" } },
                                Name = $"{titleNoFormatting} [{siteName}] {releaseDate}",
                                SearchProviderName = Plugin.Instance.Name,
                            });
                        }
                    }

                    if (pagination > 1 && pagination != i + 1)
                    {
                        string nextUrl = searchUrl.Contains("pornstar") ? $"{Helper.GetSearchBaseURL(siteNum)}/pornstar/{searchTitle.Slugify()}?related_page={i}" : $"{Helper.GetSearchSearchURL(siteNum)}{searchTitle.Slugify()}&page={i}";
                        searchResultsNode = await HTML.ElementFromURL(nextUrl, cancellationToken, forceFlareSolverr: true);
                    }
                }
            }

            return result;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem>() { Item = new Movie(), People = new List<PersonInfo>() };
            string sceneId = sceneID[0];
            var details = await GetNaughtyAmerica(sceneId, cancellationToken);
            if (details == null)
            {
                return result;
            }

            var movie = (Movie)result.Item;
            movie.Name = details.Title;
            movie.Overview = details.Synopsis;
            movie.AddStudio("Naughty America");
            if (!string.IsNullOrEmpty(details.Site))
            {
                movie.AddStudio(details.Site.Replace("&#039;", "'", StringComparison.OrdinalIgnoreCase));
            }
            movie.ExternalId = $"https://www.naughtyamerica.com/scene/0{sceneId}";

            if (details.PublishedAt != default)
            {
                movie.PremiereDate = details.PublishedAt;
                movie.ProductionYear = details.PublishedAt.Year;
            }

            foreach (var genre in details.Fantasies)
            {
                movie.AddGenre(genre);
            }

            foreach (var actor in details.Performers)
            {
                string actorPageURL = $"https://www.naughtyamerica.com/pornstar/{actor.ToLower().Replace(' ', '-').Replace("'", string.Empty)}";
                var actorPage = await HTML.ElementFromURL(actorPageURL, cancellationToken, forceFlareSolverr: true);
                string actorPhotoURL = actorPage?.SelectSingleNode("//img[contains(@class, 'performer-pic')]")?.GetAttributeValue("data-src", string.Empty);
                if (!string.IsNullOrEmpty(actorPhotoURL))
                {
                    actorPhotoURL = "https:" + actorPhotoURL;
                }

                ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actor, ImageUrl = actorPhotoURL, Type = PersonKind.Actor });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            string sceneId = sceneID[0];
            var details = await GetNaughtyAmerica(sceneId, cancellationToken);
            return details?.Photos.Select((url, index) => new RemoteImageInfo { Url = url, Type = index == 0 ? ImageType.Primary : ImageType.Backdrop }) ?? new List<RemoteImageInfo>();
        }
    }
}
