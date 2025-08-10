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
            var scenePageElements = await HTML.ElementFromURL($"https://www.naughtyamerica.com/scene/0{sceneId}", cancellationToken);
            if (scenePageElements == null) return null;

            var photoElements = scenePageElements.SelectNodes("//div[contains(@class, 'contain-scene-images') and contains(@class, 'desktop-only')]/a/@href");
            var photos = photoElements?.Select(photo => "https:" + new Regex(@"images\d+").Replace(photo.GetAttributeValue("href", ""), "images1", 1)).ToList() ?? new List<string>();

            return new NaughtyAmericaScene
            {
                Id = sceneId,
                Title = scenePageElements.SelectSingleNode("//div[contains(@class, 'scene-info')]//h1")?.InnerText,
                Site = scenePageElements.SelectSingleNode("//a[@class='site-title grey-text link']")?.InnerText,
                PublishedAt = DateTime.Parse(scenePageElements.SelectSingleNode("//div[contains(@class, 'date-tags')]//span")?.InnerText),
                Fantasies = scenePageElements.SelectNodes("//div[contains(@class, 'categories') and contains(@class, 'grey-text')]/a")?.Select(n => n.InnerText).ToList() ?? new List<string>(),
                Performers = scenePageElements.SelectNodes("//div[contains(@class, 'performer-list')]/a")?.Select(n => n.InnerText).ToList() ?? new List<string>(),
                Synopsis = scenePageElements.SelectSingleNode("//div[contains(@class, 'synopsis') and contains(@class, 'grey-text')]//h2")?.NextSibling.InnerText.Trim(),
                Photos = photos
            };
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle)) return result;

            string sceneID = searchTitle.Split(' ')[0];
            if (!int.TryParse(sceneID, out _))
                sceneID = null;

            if (!string.IsNullOrEmpty(sceneID))
            {
                var scenePageElements = await GetNaughtyAmerica(sceneID, cancellationToken);
                if (scenePageElements != null)
                {
                    string releaseDate = scenePageElements.PublishedAt.ToString("yyyy-MM-dd");
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{scenePageElements.Id}|{siteNum[0]}" } },
                        Name = $"{scenePageElements.Title} [{scenePageElements.Site}] {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name
                    });
                }
            }
            else
            {
                string searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{searchTitle.Slugify()}&_gl=1";
                var searchResultsNode = await HTML.ElementFromURL(searchUrl, cancellationToken);
                if (searchResultsNode == null) return result;

                var lastPageMatch = new Regex(@"\d+(?=#)").Match(searchResultsNode.SelectSingleNode("//li/a[./i[contains(@class, 'double')]]")?.GetAttributeValue("href", "") ?? string.Empty);
                int pagination = lastPageMatch.Success ? int.Parse(lastPageMatch.Value) + 2 : 3;

                string searchXPath = searchUrl.Contains("pornstar") ? "//div[contains(@class, 'scene-item')]" : "//div[@class='scene-grid-item']";

                for (int i = 2; i < pagination; i++)
                {
                    var searchResults = searchResultsNode.SelectNodes(searchXPath);
                    if (searchResults != null)
                    {
                        foreach (var searchResult in searchResults)
                        {
                            string titleNoFormatting = searchResult.SelectSingleNode("./a")?.GetAttributeValue("title", "").Trim();
                            int curID = int.Parse(searchResult.SelectSingleNode("./a")?.GetAttributeValue("data-scene-id", ""));
                            string releaseDate = DateTime.Parse(searchResult.SelectSingleNode("./p[@class='entry-date']")?.InnerText).ToString("yyyy-MM-dd");
                            string siteName = searchResult.SelectSingleNode(".//a[@class='site-title']")?.InnerText;
                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}" } },
                                Name = $"{titleNoFormatting} [{siteName}] {releaseDate}",
                                SearchProviderName = Plugin.Instance.Name
                            });
                        }
                    }

                    if (pagination > 1 && pagination != i + 1)
                    {
                        string nextUrl = searchUrl.Contains("pornstar") ? $"{Helper.GetSearchBaseURL(siteNum)}/pornstar/{searchTitle.Slugify()}?related_page={i}" : $"{Helper.GetSearchSearchURL(siteNum)}{searchTitle.Slugify()}&page={i}";
                        searchResultsNode = await HTML.ElementFromURL(nextUrl, cancellationToken);
                    }
                }
            }
            return result;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem>() { Item = new Movie(), People = new List<PersonInfo>() };
            string sceneId = sceneID[0].Split('|')[0];
            var details = await GetNaughtyAmerica(sceneId, cancellationToken);
            if (details == null) return result;

            var movie = (Movie)result.Item;
            movie.Name = details.Title;
            movie.Overview = details.Synopsis;
            movie.AddStudio("Naughty America");
            movie.AddTag(details.Site);

            if (details.PublishedAt != default)
            {
                movie.PremiereDate = details.PublishedAt;
                movie.ProductionYear = details.PublishedAt.Year;
            }

            foreach(var genre in details.Fantasies)
                movie.AddGenre(genre);

            foreach(var actor in details.Performers)
            {
                string actorPageURL = $"https://www.naughtyamerica.com/pornstar/{actor.ToLower().Replace(' ', '-').Replace("'", "")}";
                var actorPage = await HTML.ElementFromURL(actorPageURL, cancellationToken);
                string actorPhotoURL = actorPage?.SelectSingleNode("//img[contains(@class, 'performer-pic')]")?.GetAttributeValue("data-src", "");
                if(!string.IsNullOrEmpty(actorPhotoURL))
                    actorPhotoURL = "https:" + actorPhotoURL;
                result.People.Add(new PersonInfo { Name = actor, ImageUrl = actorPhotoURL, Type = PersonType.Actor });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            string sceneId = sceneID[0].Split('|')[0];
            var details = await GetNaughtyAmerica(sceneId, cancellationToken);
            return details?.Photos.Select((url, index) => new RemoteImageInfo { Url = url, Type = index == 0 ? ImageType.Primary : ImageType.Backdrop }) ?? new List<RemoteImageInfo>();
        }
    }
}
