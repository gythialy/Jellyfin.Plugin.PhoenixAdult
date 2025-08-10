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
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    public class SiteData18Empire : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var searchResults = new HashSet<string>();

            string sceneID = null;
            var parts = searchTitle.Split(' ');
            if (int.TryParse(parts[0], out var parsedId) && parsedId > 100)
            {
                sceneID = parts[0];
                searchTitle = searchTitle.Replace(sceneID, "").Trim();
                searchResults.Add($"{Helper.GetSearchBaseURL(siteNum)}/{sceneID}");
            }

            string encodedTitle = searchTitle.Trim().Replace(" ", "+");
            string searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{encodedTitle}";
            var req = await HTTP.Request(searchUrl, cancellationToken, new Dictionary<string, string> { { "Referer", "http://www.data18.empirestores.co" } });
            var searchPageElements = HTML.Document(req.Content);

            if (string.IsNullOrEmpty(sceneID))
            {
                var searchResultNodes = searchPageElements.SelectNodes("//a[@class='boxcover']");
                if (searchResultNodes != null)
                {
                    foreach (var searchResult in searchResultNodes)
                    {
                        string movieURL = $"{Helper.GetSearchBaseURL(siteNum)}{searchResult.GetAttributeValue("href", "")}";
                        string urlID = searchResult.GetAttributeValue("href", "").Split('/')[1];
                        if (movieURL.Contains("movies") && !searchResults.Contains(movieURL))
                        {
                            string titleNoFormatting = searchResult.SelectSingleNode("./span/span/text()")?.InnerText.Trim();
                            string curID = Helper.Encode(movieURL);

                            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken);
                            if (detailsPageElements == null) continue;

                            var dateNode = detailsPageElements.SelectSingleNode("//div[@class='release-date' and ./span[contains(., 'Released:')]]/text()");
                            string releaseDate = dateNode != null && DateTime.TryParseExact(dateNode.InnerText.Trim(), "MMM dd, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate) ? parsedDate.ToString("yyyy-MM-dd") : "";
                            string studio = detailsPageElements.SelectSingleNode("//div[@class='studio']/a/text()")?.InnerText.Trim();
                            int score = sceneID == urlID ? 100 : (searchDate.HasValue && !string.IsNullOrEmpty(releaseDate) ? 80 - LevenshteinDistance.Compute(searchDate.Value.ToString("yyyy-MM-dd"), releaseDate) : 80 - LevenshteinDistance.Compute(searchTitle.ToLower(), titleNoFormatting.ToLower()));
                            result.Add(new RemoteSearchResult { ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}|{releaseDate}" } }, Name = $"{titleNoFormatting} [{studio}] {releaseDate}", Score = score });

                            var scenes = detailsPageElements.SelectNodes("//div[@class='item-grid item-grid-scene']/div/a/@href");
                            if (scenes != null)
                            {
                                for (int i = 0; i < scenes.Count; i++)
                                {
                                    string section = "Scene " + (i + 1);
                                    result.Add(new RemoteSearchResult { ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}|{releaseDate}|{i}" } }, Name = $"{titleNoFormatting} [{section}][{studio}] {releaseDate}", Score = score });
                                }
                            }
                        }
                    }
                }
            }
            // ... (rest of search logic including Google fallback would go here)
            return result;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem> { Item = new Movie(), People = new List<PersonInfo>() };
            string[] providerIds = sceneID[0].Split('|');
            string sceneURL = Helper.Decode(providerIds[0]);
            string sceneDate = providerIds.Length > 2 ? providerIds[2] : null;

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (detailsPageElements == null) return result;

            var movie = (Movie)result.Item;
            string title = detailsPageElements.SelectSingleNode("//h1[@class='description']/text()")?.InnerText.Trim();
            movie.Name = title;
            if (providerIds.Length > 3)
                movie.Name = $"{title} [Scene {providerIds[3]}]";

            movie.Overview = string.Join("\n\n", detailsPageElements.SelectNodes("//div[@class='synopsis']//text()").Select(n => n.InnerText));

            var studio = detailsPageElements.SelectSingleNode("//div[@class='studio']/a/text()")?.InnerText.Trim();
            if(!string.IsNullOrEmpty(studio))
                movie.AddStudio(studio);

            var tagline = detailsPageElements.SelectSingleNode("//p[contains(text(), 'A scene from')]/a/text()")?.InnerText.Trim()
                ?? detailsPageElements.SelectSingleNode("//a[@data-label='Series List']/h2/text()")?.InnerText.Trim().Replace("Series:", "").Replace($"({studio})", "").Trim();
            movie.Tags.Add(tagline ?? studio);

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//div[@class='categories']/a");
            if(genreNodes != null)
            {
                foreach(var genre in genreNodes)
                    movie.AddGenre(genre.InnerText.Trim());
            }

            var actorNodes = providerIds.Length > 3
                ? detailsPageElements.SelectNodes($"//div[@class='item-grid item-grid-scene']/div[@class='grid-item'][{providerIds[3]}]/div/div[@class='scene-cast-list']/a/text()")
                : detailsPageElements.SelectNodes("//div[@class='video-performer']/a/span/span");
            if (actorNodes != null)
            {
                foreach(var actor in actorNodes)
                    result.People.Add(new PersonInfo { Name = actor.InnerText.Trim(), Type = PersonType.Actor });
            }

            var directorNode = detailsPageElements.SelectSingleNode("//div[@class='director']/a/text()");
            if(directorNode != null && directorNode.InnerText.Split(':').Last().Trim() != "Unknown")
                result.People.Add(new PersonInfo { Name = directorNode.InnerText.Split(':').Last().Trim(), Type = PersonType.Director });

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            // ... (Image logic would be ported here) ...
            return result;
        }
    }
}
