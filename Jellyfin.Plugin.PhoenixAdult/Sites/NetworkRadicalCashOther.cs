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
    public class NetworkRadicalCashOther : IProviderBase
    {
        private static readonly string[] supported_lang = { "en", "de" };
        private static readonly Dictionary<int[], Dictionary<string, string>> xPathMap = new Dictionary<int[], Dictionary<string, string>>
        {
            {new[] {1066}, new Dictionary<string, string> {
                {"searchResults", "//div[contains(@class,'content-item')]"}, {"actorSearchResults", "//div[contains(@class, 'content-item')]"},
                {"searchTitle", ".//h3"}, {"searchURL", ".//h3//@href"}, {"searchDate", ".//span[@class='pub-date']"},
                {"title", "//h1"}, {"date", "//span[@class='date']"}, {"summary", "//div[@class='description']//p"},
                {"genres", "//meta[@name='keywords']/@content"}, {"actors", "//div[@class='model-wrap']//li"},
                {"actor", ".//h5/text()"}, {"actorPhoto", ".//img/@src"}, {"searchDateFormat", "MMM d, yyyy"},
                {"dateFormat", "dddd MMMM d, yyyy"},
            }                        },
            {new[] {1851, 1852, 1853, 1854, 1855, 1856, 1857, 1858, 1859}, new Dictionary<string, string> {
                {"searchResults", "//div[@class='content-metadata']"}, {"actorSearchResults", "//div[contains(@class, 'video-description')]"},
                {"searchTitle", ".//h1"}, {"searchURL", ".//h1//@href"}, {"searchDate", ".//p[@class='content-date']/strong[1]"},
                {"title", "//h1"}, {"date", "//span[@class='meta-value'][2]"}, {"summary", "//div[@class='content-description']//p"},
                {"genres", "//meta[@name='keywords']/@content"}, {"actors", "//div[./div[@class='model-name']]"},
                {"actor", "./div[@class='model-name']/text()"}, {"actorPhoto", ".//img/@src"},
                {"searchDateFormat", "dd/MM/yyyy"}, {"dateFormat", "dd/MM/yyyy"},
            }                        },
            {new[] {1860}, new Dictionary<string, string> {
                {"searchResults", "//div[@class='col-sm-3']"}, {"actorSearchResults", "//div[contains(@class, 'content-item')]"},
                {"searchTitle", ".//h5"}, {"searchURL", ".//a//@href"}, {"searchDate", ".//div[@class='pull-right'][./i[contains(@class,'calendar')]]"},
                {"title", "//h2"}, {"date", "//span[@class='post-date']"}, {"summary", "//div[@class='desc']//p"},
                {"genres", "//meta[@name='keywords']/@content"}, {"actors", "//div[@class='content-meta']//h4[@class='models']//a"},
                {"actor", "./text()"}, {"actorPhoto", ".//@href"}, {"searchDateFormat", "d MMM yyyy"},
                {"dateFormat", "d MMM yyyy"},
            }                        },
            {new[] {1861, 1862}, new Dictionary<string, string> {
                {"searchResults", "//div[contains(@class, 'content-item-medium')]"}, {"actorSearchResults", "//div[contains(@class, 'content-item-large')]"},
                {"searchTitle", ".//h3"}, {"searchURL", ".//a//@href"}, {"searchDate", ".//div[@class='date']"},
                {"title", "//h2"}, {"date", "//span[@class='post-date']"}, {"summary", "//div[@class='desc']//p"},
                {"genres", "//meta[@name='keywords']/@content"}, {"actors", "//div[@class='content-meta']//h4[@class='models']//a"},
                {"actor", "./text()"}, {"actorPhoto", ".//@href"}, {"searchDateFormat", "d MMM yyyy"},
                {"dateFormat", "d MMM yyyy"},
            }                        },
        };

        private RemoteSearchResult SearchResultBuilder(HtmlNode searchResult, int[] siteNum, DateTime? searchDate, string lang, List<string> directSearchResults)
        {
            var siteXPath = xPathMap.FirstOrDefault(x => x.Key.Contains(siteNum[0])).Value;
            string sceneUrl = searchResult.SelectSingleNode(siteXPath["searchURL"])?.GetAttributeValue("href", string.Empty).Split('?')[0];
            if (directSearchResults.Contains(sceneUrl))
            {
                return null;
            }

            string titleNoFormatting = searchResult.SelectSingleNode(siteXPath["searchTitle"])?.InnerText.Trim();
            string curId = Helper.Encode(sceneUrl);
            string releaseDate = string.Empty;
            var dateNode = searchResult.SelectSingleNode(siteXPath["searchDate"]);
            if (dateNode != null)
            {
                string cleanDate = Regex.Replace(dateNode.InnerText.Split(new[] { ':' }, StringSplitOptions.None).Last().Trim(), @"(\d)(st|nd|rd|th)", "$1");
                if (DateTime.TryParseExact(cleanDate, siteXPath["searchDateFormat"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                {
                    releaseDate = parsedDate.ToString("yyyy-MM-dd");
                }
            }

            return new RemoteSearchResult
            {
                ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                Name = $"{releaseDate} [{Helper.GetSearchSiteName(siteNum)}] {titleNoFormatting}",
                SearchProviderName = Plugin.Instance.Name,
            };
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var directSearchResults = new List<string>();
            var searchResults = new List<string>();
            var siteXPath = xPathMap.FirstOrDefault(x => x.Key.Contains(siteNum[0])).Value;

            string searchUrl = Helper.GetSearchSearchURL(siteNum) + searchTitle.ToLower();
            if (siteNum[0] >= 1851 && siteNum[0] <= 1859)
            {
                searchUrl = $"{searchUrl}?_lang=en";
            }

            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (httpResult.IsOK)
            {
                var searchPageElements = HTML.ElementFromString(httpResult.Content);
                var searchNodes = searchPageElements.SelectNodes(siteXPath["searchResults"]);
                if(searchNodes != null)
                {
                    foreach (var searchResult in searchNodes)
                    {
                        directSearchResults.Add(searchResult.SelectSingleNode(siteXPath["searchURL"])?.GetAttributeValue("href", string.Empty).Split('?')[0]);
                        result.Add(SearchResultBuilder(searchResult, siteNum, searchDate, "en", directSearchResults));
                    }
                }
            }

            var googleResults = await GoogleSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            foreach (var sceneUrl in googleResults)
            {
                string url = sceneUrl.Split('?')[0].Replace("dev.", string.Empty);
                if ((url.Contains("/view/") || url.Contains("/model/")) && !url.Contains("photoset") && !searchResults.Contains(url) && !directSearchResults.Contains(url))
                {
                    searchResults.Add(url);
                }
            }

            foreach (var sceneUrl in searchResults)
            {
                string url = sceneUrl;
                if (siteNum[0] >= 1851 && siteNum[0] <= 1859)
                {
                    url = $"{sceneUrl}?_lang=en";
                }

                if (url.Contains("/model/"))
                {
                    var actorHttp = await HTTP.Request(url, HttpMethod.Get, cancellationToken);
                    if(actorHttp.IsOK)
                    {
                        var actorPageElements = HTML.ElementFromString(actorHttp.Content);
                        var actorSearchResults = actorPageElements.SelectNodes(siteXPath["actorSearchResults"]);
                        if(actorSearchResults != null)
                        {
                            foreach (var searchResult in actorSearchResults)
                            {
                                string scene = searchResult.SelectSingleNode(siteXPath["searchURL"])?.GetAttributeValue("href", string.Empty).Split('?')[0].Replace("dev.", string.Empty);
                                if (!scene.Contains("/join"))
                                {
                                    result.Add(SearchResultBuilder(searchResult, siteNum, searchDate, "en", directSearchResults));
                                }
                            }
                        }
                    }
                }
                else
                {
                    var sceneHttp = await HTTP.Request(url, HttpMethod.Get, cancellationToken);
                    if(sceneHttp.IsOK)
                    {
                        var detailsPageElements = HTML.ElementFromString(sceneHttp.Content);
                        result.Add(SearchResultBuilder(detailsPageElements, siteNum, searchDate, "en", directSearchResults));
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

            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            if (siteNum[0] >= 1851 && siteNum[0] <= 1859)
            {
                sceneUrl = $"{sceneUrl}?_lang=en";
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);
            var siteXPath = xPathMap.FirstOrDefault(x => x.Key.Contains(siteNum[0])).Value;

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode(siteXPath["title"])?.InnerText.Trim();
            movie.Overview = string.Join("\n\n", detailsPageElements.SelectNodes(siteXPath["summary"])?.Select(s => s.InnerText.Trim()) ?? new string[0]);

            if (siteNum[0] >= 1852 && siteNum[0] <= 1859)
            {
                movie.AddStudio("Hitzefrei");
            }
            else if (siteNum[0] >= 1861 && siteNum[0] <= 1862)
            {
                movie.AddStudio("Gonzo Living");
            }
            else
            {
                movie.AddStudio("Radical Cash");
            }

            string tagline = Helper.GetSearchSiteName(siteNum);
            if (siteNum[0] == 1066)
            {
                tagline = $"{tagline}: {detailsPageElements.SelectSingleNode("//p[@class='series']")?.InnerText.Trim()}";
            }

            movie.AddTag(tagline);

            var dateNode = detailsPageElements.SelectSingleNode(siteXPath["date"]);
            if (dateNode != null)
            {
                string cleanDate = Regex.Replace(dateNode.InnerText.Trim(), @"(\d)(st|nd|rd|th)", "$1");
                if (DateTime.TryParseExact(cleanDate, siteXPath["dateFormat"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                {
                    movie.PremiereDate = parsedDate;
                    movie.ProductionYear = parsedDate.Year;
                }
            }

            var genreNodes = detailsPageElements.SelectNodes(siteXPath["genres"]);
            if(genreNodes != null)
            {
                foreach(var genre in genreNodes[0].InnerText.Split(','))
                {
                    movie.AddGenre(genre.Trim());
                }
            }

            var actorNodes = detailsPageElements.SelectNodes(siteXPath["actors"]);
            if (actorNodes != null)
            {
                if (actorNodes.Count == 3)
                {
                    movie.AddGenre("Threesome");
                }

                if (actorNodes.Count == 4)
                {
                    movie.AddGenre("Foursome");
                }

                if (actorNodes.Count > 4)
                {
                    movie.AddGenre("Orgy");
                }

                foreach (var actor in actorNodes)
                {
                    string actorName = actor.SelectSingleNode(siteXPath["actor"])?.InnerText;
                    string actorPhotoUrl = actor.SelectSingleNode(siteXPath["actorPhoto"])?.GetAttributeValue("src", string.Empty);
                    if (siteNum[0] >= 1860 && siteNum[0] <= 1862)
                    {
                        var actorHttp = await HTTP.Request(actorPhotoUrl, HttpMethod.Get, cancellationToken);
                        if(actorHttp.IsOK)
                        {
                            var modelPageElements = HTML.ElementFromString(actorHttp.Content);
                            actorPhotoUrl = modelPageElements.SelectSingleNode("//div[@class='model-photo']//@src")?.GetAttributeValue("src", string.Empty);
                        }
                    }
                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            if (siteNum[0] >= 1851 && siteNum[0] <= 1859)
            {
                sceneUrl = $"{sceneUrl}?_lang=en";
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var imageNodes = detailsPageElements.SelectNodes("//div[@class='photo-wrap']//@href | //div[@id='photo-carousel']//@href | //video/@poster");
            if(imageNodes != null)
            {
                foreach(var img in imageNodes)
                {
                    string imageUrl = img.GetAttributeValue("href", string.Empty) ?? img.GetAttributeValue("poster", string.Empty);
                    if (!imageUrl.StartsWith("http"))
                    {
                        imageUrl = Helper.GetSearchBaseURL(siteNum) + imageUrl;
                    }

                    if (!string.IsNullOrEmpty(imageUrl))
                    {
                        images.Add(new RemoteImageInfo { Url = imageUrl });
                    }
                }
            }

            if (images.Any())
            {
                images.First().Type = ImageType.Primary;
            }

            return images;
        }
    }
}
