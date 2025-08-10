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
using PhoenixAdult.Configuration;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    public class SiteJavLibrary : IProviderBase
    {
        private static readonly Dictionary<string, string[]> ActorsDB = new Dictionary<string, string[]>
        {
            { "Lily Glee", new[] { "ANCI-038" } },
            { "Lana Sharapova", new[] { "ANCI-038" } },
            { "Madi Collins", new[] { "KTKL-112" } },
            { "Tsubomi", new[] { "WA-192" } },
        };

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
                return result;

            string searchJAVID = null;
            var splitSearchTitle = searchTitle.Split(' ');
            if (splitSearchTitle[0].StartsWith("3dsvr", StringComparison.OrdinalIgnoreCase))
                splitSearchTitle[0] = splitSearchTitle[0].Replace("3dsvr", "dsvr", StringComparison.OrdinalIgnoreCase);
            else if (splitSearchTitle[0].StartsWith("13dsvr", StringComparison.OrdinalIgnoreCase))
                splitSearchTitle[0] = splitSearchTitle[0].Replace("13dsvr", "dsvr", StringComparison.OrdinalIgnoreCase);

            if (splitSearchTitle.Length > 1 && int.TryParse(splitSearchTitle[1], out _))
                searchJAVID = $"{splitSearchTitle[0]}-{splitSearchTitle[1]}";

            string encodedSearch = searchJAVID ?? Uri.EscapeDataString(searchTitle);
            var searchUrl = Helper.GetSearchSearchURL(siteNum) + encodedSearch;
            var searchPageElements = await HTML.ElementFromURL(searchUrl, cancellationToken);

            var searchResults = new List<RemoteSearchResult>();
            var searchResultNodes = searchPageElements?.SelectNodes("//div[@class='video']");
            if (searchResultNodes != null)
            {
                foreach (var searchResultNode in searchResultNodes)
                {
                    string titleNoFormatting = searchResultNode.SelectSingleNode("./a")?.GetAttributeValue("title", "").Trim();
                    string javid = titleNoFormatting?.Split(' ')[0];
                    string sceneURL = $"{Helper.GetSearchBaseURL(siteNum)}/en/{searchResultNode.SelectSingleNode("./a")?.GetAttributeValue("href", "").Split('.').Last().Trim()}";
                    string curID = Helper.Encode(sceneURL);

                    int score = 100 - LevenshteinDistance.Compute(searchJAVID.ToLower(), javid.ToLower());
                    searchResults.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}" } },
                        Name = $"[{javid}] {titleNoFormatting}",
                        Score = score,
                        SearchProviderName = Plugin.Instance.Name
                    });
                }
            }

            if (!searchResults.Any())
            {
                var googleResults = await GoogleSearch.GetSearchResults($"{splitSearchTitle[0]} {splitSearchTitle[1]}", siteNum, cancellationToken);
                foreach(var sceneURL in googleResults)
                {
                    if (sceneURL.Contains("?v=jav") && !sceneURL.Contains("videoreviews"))
                    {
                        string englishSceneURL = sceneURL.Replace("/ja/", "/en/").Replace("/tw/", "/en/").Replace("/cn/", "/en/");
                        if (!englishSceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            englishSceneURL = "http:" + englishSceneURL;

                        var searchResultPage = await HTML.ElementFromURL(englishSceneURL, cancellationToken);
                        if (searchResultPage != null)
                        {
                            string titleNoFormatting = searchResultPage.SelectSingleNode("//h3[@class='post-title text']/a")?.InnerText.Trim().Split(new[] { ' ' }, 2)[1];
                            string javid = searchResultPage.SelectSingleNode("//td[contains(text(), 'ID:')]/following-sibling::td")?.InnerText.Trim();
                            string curID = Helper.Encode(searchResultPage.SelectSingleNode("//meta[@property='og:url']")?.GetAttributeValue("content", "").Replace("//www", "https://www"));
                            int score = 100 - LevenshteinDistance.Compute(searchJAVID.ToLower(), javid.ToLower());
                            searchResults.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}" } },
                                Name = $"[{javid}] {titleNoFormatting}",
                                Score = score,
                                SearchProviderName = Plugin.Instance.Name
                            });
                        }
                    }
                }
            }

            return searchResults;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem>()
            {
                Item = new Movie(),
                People = new List<PersonInfo>(),
            };

            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            var detailsPageElements = await HTML.ElementFromURL(sceneUrl, cancellationToken);
            if (detailsPageElements == null) return result;

            var movie = (Movie)result.Item;
            var ogTitle = detailsPageElements.SelectSingleNode("//meta[@property='og:title']")?.GetAttributeValue("content", "").Trim();
            string javID = ogTitle?.Split(' ')[0];
            string title = ogTitle?.Split(new[] { ' ' }, 2).Last().Replace(" - JAVLibrary", "").Replace(javID, "").Trim();

            movie.Name = $"[{javID.ToUpper()}] {title}";
            if(title.Length > 80)
                movie.Overview = title;

            var studio = detailsPageElements.SelectSingleNode("//td[contains(text(), 'Maker:')]/following-sibling::td/span/a")?.InnerText.Trim();
            if(!string.IsNullOrEmpty(studio))
                movie.AddStudio(studio);

            var tagline = detailsPageElements.SelectSingleNode("//td[contains(text(), 'Label:')]/following-sibling::td/span/a")?.InnerText.Trim();
            if (!string.IsNullOrEmpty(tagline))
                movie.Tags.Add(tagline);
            else if (!string.IsNullOrEmpty(studio))
                movie.Tags.Add(studio);
            else
                movie.Tags.Add("Japan Adult Video");

            var director = detailsPageElements.SelectSingleNode("//td[contains(text(), 'Director:')]/following-sibling::td/span/a")?.InnerText.Trim();
            if(!string.IsNullOrEmpty(director))
                result.People.Add(new PersonInfo { Name = director, Type = PersonType.Director });

            var dateNode = detailsPageElements.SelectSingleNode("//td[contains(text(), 'Release Date:')]/following-sibling::td");
            if (dateNode != null && DateTime.TryParseExact(dateNode.InnerText.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            foreach(var actor in ActorsDB)
            {
                if (actor.Value.Contains(javID, StringComparer.OrdinalIgnoreCase))
                    result.People.Add(new PersonInfo { Name = actor.Key, Type = PersonType.Actor });
            }

            var actorNodes = detailsPageElements.SelectNodes("//span[@class='star']/a");
            if (actorNodes != null)
            {
                foreach(var actor in actorNodes)
                {
                    string actorName = actor.InnerText.Trim();
                    if (Plugin.Instance.Configuration.JAVActorNamingStyle == JAVActorNamingStyle.WesternStyle)
                        actorName = string.Join(" ", actorName.Split().Reverse());
                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonType.Actor });
                }
            }

            var genreNodes = detailsPageElements.SelectNodes("//a[@rel='category tag']");
            if (genreNodes != null)
            {
                foreach(var genre in genreNodes)
                    movie.AddGenre(genre.InnerText.Trim());
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            var detailsPageElements = await HTML.ElementFromURL(sceneUrl, cancellationToken);
            if (detailsPageElements == null) return images;

            var posterURL = detailsPageElements.SelectSingleNode("//img[@id='video_jacket_img']")?.GetAttributeValue("src", "");
            if (!string.IsNullOrEmpty(posterURL))
            {
                if (!posterURL.StartsWith("https"))
                    posterURL = "https:" + posterURL;
                images.Add(new RemoteImageInfo { Url = posterURL, Type = ImageType.Primary });
            }

            var imageNodes = detailsPageElements.SelectNodes("//div[@class='previewthumbs']/img");
            if (imageNodes != null)
            {
                var urlRegEx = new Regex(@"-([1-9]+)\.jpg");
                foreach(var image in imageNodes)
                {
                    string thumbnailURL = image.GetAttributeValue("src", "");
                    var idxSearch = urlRegEx.Match(thumbnailURL);
                    if (idxSearch.Success)
                    {
                        string imageURL = $"{thumbnailURL.Substring(0, idxSearch.Index)}jp{thumbnailURL.Substring(idxSearch.Index)}";
                        images.Add(new RemoteImageInfo { Url = imageURL, Type = ImageType.Backdrop });
                    }
                    else
                    {
                        images.Add(new RemoteImageInfo { Url = thumbnailURL, Type = ImageType.Backdrop });
                    }
                }
            }

            return images;
        }
    }
}
