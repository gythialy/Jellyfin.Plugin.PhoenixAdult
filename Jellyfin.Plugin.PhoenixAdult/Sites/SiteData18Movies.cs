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

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class SiteData18Movies : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var searchResults = new HashSet<string>();
            var siteResults = new HashSet<string>();
            var temp = new List<RemoteSearchResult>();

            string sceneID = null;
            var parts = searchTitle.Split(' ');
            if (int.TryParse(parts[0], out var parsedId) && parsedId > 100)
            {
                sceneID = parts[0];
                searchTitle = searchTitle.Replace(sceneID, string.Empty).Trim();
                searchResults.Add($"{Helper.GetSearchBaseURL(siteNum)}/movies/{sceneID}");
            }

            string encodedTitle = searchTitle.Replace("'", string.Empty).Replace(",", string.Empty).Replace("& ", string.Empty).Replace("#", string.Empty);
            string searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{encodedTitle}&key2={encodedTitle}";
            var req = await HTTP.Request(searchUrl, cancellationToken, new Dictionary<string, string> { { "Referer", "https://www.data18.com" } }, new Dictionary<string, string> { { "data_user_captcha", "1" } });
            var searchPageElements = HTML.ElementFromString(req.Content);

            var searchPagesMatch = Regex.Match(req.Content, @"(?<=pages:\s).*(?=])");
            int numSearchPages = searchPagesMatch.Success ? Math.Min(int.Parse(searchPagesMatch.Value), 10) : 1;

            for (int i = 0; i < numSearchPages; i++)
            {
                foreach (var searchResult in searchPageElements.SelectNodes("//a"))
                {
                    string movieURL = searchResult.GetAttributeValue("href", string.Empty).Split('-')[0];
                    if (movieURL.Contains("/movies/") && !searchResults.Contains(movieURL))
                    {
                        string urlID = Regex.Replace(movieURL, ".*/", string.Empty);
                        string studio = searchResult.SelectSingleNode(".//i")?.InnerText.Trim();
                        string titleNoFormatting = searchResult.SelectSingleNode(".//p[@class='gen12 bold']")?.InnerText;
                        string curID = Helper.Encode(movieURL);

                        if (titleNoFormatting?.Contains("...") == true)
                        {
                            searchResults.Add(movieURL);
                        }
                        else
                        {
                            siteResults.Add(movieURL);
                            string date = searchResult.SelectSingleNode(".//span[@class='gen11']/text()")?.InnerText.Trim();
                            string releaseDate = !string.IsNullOrEmpty(date) && date != "unknown" ? DateTime.ParseExact(date, "MMMM, yyyy", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd") : (searchDate.HasValue ? searchDate.Value.ToString("yyyy-MM-dd") : string.Empty);

                            var detailsPageElements = await HTML.ElementFromURL(curID, cancellationToken, new Dictionary<string, string> { { "Referer", "https://www.data18.com" } }, new Dictionary<string, string> { { "data_user_captcha", "1" } });
                            studio = detailsPageElements?.SelectSingleNode("//b[contains(., 'Studio') or contains(., 'Network')]//following-sibling::b | //b[contains(., 'Studio') or contains(., 'Network')]//following-sibling::a | //p[contains(., 'Site:')]//following-sibling::a[@class='bold']")?.InnerText.Trim() ?? studio;

                            result.Add(new RemoteSearchResult { ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}|{releaseDate}" } }, Name = $"{titleNoFormatting} [{studio}] {releaseDate}" });

                            var sceneCountMatch = detailsPageElements?.SelectSingleNode("//div[@id='relatedscenes']//span")?.InnerText.Split(' ')[0].Trim();
                            int sceneCount = !string.IsNullOrEmpty(sceneCountMatch) && int.TryParse(sceneCountMatch, out var countVal) ? countVal : 0;

                            for (int j = 1; j <= sceneCount; j++)
                            {
                                string section = "Scene " + j;
                                string scene = Helper.Encode(detailsPageElements.SelectSingleNode($"//a[contains(., '{section}')]/@href").GetAttributeValue("href", string.Empty));
                                result.Add(new RemoteSearchResult { ProviderIds = { { Plugin.Instance.Name, $"{scene}|{siteNum[0]}|{releaseDate}|{titleNoFormatting}|{j}" } }, Name = $"{titleNoFormatting} [{section}][{studio}] {releaseDate}" });
                            }
                        }
                    }
                }
                if (numSearchPages > 1 && i + 1 != numSearchPages)
                {
                    searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{encodedTitle}&key2={encodedTitle}&next=1&page={i + 1}";
                    req = await HTTP.Request(searchUrl, cancellationToken, new Dictionary<string, string> { { "Referer", "https://www.data18.com" } }, new Dictionary<string, string> { { "data_user_captcha", "1" } });
                    searchPageElements = HTML.ElementFromString(req.Content);
                }
            }

            // ... (rest of search logic including Google fallback would go here)
            return result;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem> { Item = new Movie(), People = new List<PersonInfo>() };
            string[] providerIds = sceneID[0].Split('|');

            if (providerIds.Length > 3) // This is a scene, delegate to SiteData18Scenes
            {
                var sceneProvider = new SiteData18Scenes();
                return await sceneProvider.Update(siteNum, sceneID, cancellationToken);
            }

            string sceneURL = Helper.Decode(providerIds[0]);
            string sceneDate = providerIds.Length > 2 ? providerIds[2] : null;
            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken, new Dictionary<string, string> { { "Referer", "https://www.data18.com" } }, new Dictionary<string, string> { { "data_user_captcha", "1" } });
            if (detailsPageElements == null)
            {
                return result;
            }

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//h1").InnerText;
            var summaryNode = detailsPageElements.SelectSingleNode("//div[@class='gen12']//div[contains(., 'Description')]");
            movie.Overview = summaryNode?.InnerText.Split(new[] { "---", "Description -" }, StringSplitOptions.RemoveEmptyEntries).Last().Trim();

            var studioNode = detailsPageElements.SelectSingleNode("//b[contains(., 'Network')]//following-sibling::b | //b[contains(., 'Studio')]//following-sibling::b | //p[contains(., 'Site:')]//following-sibling::a[@class='bold']");
            if (studioNode != null)
            {
                movie.AddStudio(studioNode.InnerText.Trim());
            }

            var taglineNode = detailsPageElements.SelectSingleNode("//p[contains(., 'Movie Series')]//a[@title]");
            if (taglineNode != null)
            {
                movie.AddTag(taglineNode.InnerText.Trim());
            }

            var dateNode = detailsPageElements.SelectSingleNode("//@datetime");
            if (dateNode != null && DateTime.TryParse(dateNode.GetAttributeValue("datetime", string.Empty).Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }
            else if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out parsedDate))
            {
                 movie.PremiereDate = parsedDate;
                 movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//p[./b[contains(., 'Categories')]]//a");
            if(genreNodes != null)
            {
                foreach(var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//b[contains(., 'Cast')]//following::div//a[contains(@href, '/pornstars/')]//img | //b[contains(., 'Cast')]//following::div//img[contains(@data-original, 'user')] | //h3[contains(., 'Cast')]//following::div[@style]//img");
            if(actorNodes != null)
            {
                foreach(var actor in actorNodes)
                {
                    result.People.Add(new PersonInfo { Name = actor.GetAttributeValue("alt", string.Empty).Trim(), ImageUrl = actor.GetAttributeValue("data-src", string.Empty), Type = PersonKind.Actor });
                }
            }

            var directorNode = detailsPageElements.SelectSingleNode("//p[./b[contains(., 'Director')]]")?.InnerText.Split(':').Last().Split('-')[0].Trim();
            if(directorNode != "Unknown")
            {
                result.People.Add(new PersonInfo { Name = directorNode, Type = PersonKind.Director });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            // ... (Image logic would be ported here) ...
            return result;
        }
    }
}
