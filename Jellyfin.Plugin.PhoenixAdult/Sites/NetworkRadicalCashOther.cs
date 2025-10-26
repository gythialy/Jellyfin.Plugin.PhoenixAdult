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
    public class NetworkRadicalCashOther : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var directSearchResults = new List<string>();
            var searchResults = new List<string>();
            var siteXPath = GetXPathMap(siteNum);

            string searchUrl = Helper.GetSearchSearchURL(siteNum) + searchTitle.ToLower();
            if (siteNum[0] >= 1851 && siteNum[0] <= 1859)
            {
                searchUrl = $"{searchUrl}?_lang=en";
            }

            var httpResult = await HTTP.Request(searchUrl, cancellationToken);
            if (httpResult.IsOK)
            {
                var searchPageElements = HTML.ElementFromString(httpResult.Content);
                var searchNodes = searchPageElements.SelectNodes(siteXPath["searchResults"]);
                if (searchNodes != null)
                {
                    foreach (var searchResult in searchNodes)
                    {
                        string sceneUrl = searchResult.SelectSingleNode(siteXPath["searchURL"]).GetAttributeValue("href", string.Empty).Split('?')[0];
                        directSearchResults.Add(sceneUrl);

                        string titleNoFormatting = searchResult.SelectSingleNode(siteXPath["searchTitle"]).InnerText.Trim();
                        string curId = Helper.Encode(sceneUrl);

                        string releaseDate = string.Empty;
                        var dateNode = searchResult.SelectSingleNode(siteXPath["searchDate"]);
                        if (dateNode != null)
                        {
                            string cleanDate = Regex.Replace(dateNode.InnerText.Split(':').Last().Trim(), @"(\d)(st|nd|rd|th)", "$1");
                            if (DateTime.TryParseExact(cleanDate, siteXPath["searchDateFormat"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                            {
                                releaseDate = parsedDate.ToString("yyyy-MM-dd");
                            }
                        }
                        else if (searchDate.HasValue)
                        {
                            releaseDate = searchDate.Value.ToString("yyyy-MM-dd");
                        }

                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                            Name = $"{releaseDate} [{Helper.GetSearchSiteName(siteNum)}] {Helper.ParseTitle(titleNoFormatting, siteNum)}",
                            SearchProviderName = Plugin.Instance.Name,
                        });
                    }
                }
            }

            var googleResults = await WebSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            foreach (var sceneURL in googleResults)
            {
                var url = sceneURL.Split('?')[0].Replace("dev.", string.Empty);
                if ((url.Contains("/view/") || url.Contains("/model/")) && !url.Contains("photoset") && !searchResults.Contains(url) && !directSearchResults.Contains(url))
                {
                    searchResults.Add(url);
                }
            }

            foreach (var sceneURL in searchResults)
            {
                RemoteSearchResult resultData = null;
                var url = sceneURL;
                if (siteNum[0] >= 1851 && siteNum[0] <= 1859)
                {
                    url = $"{sceneURL}?_lang=en";
                }

                if (url.Contains("/model/"))
                {
                    var actorHttp = await HTTP.Request(url, cancellationToken);
                    if (actorHttp.IsOK)
                    {
                        var actorPageElements = HTML.ElementFromString(actorHttp.Content);
                        var actorSearchNodes = actorPageElements.SelectNodes(siteXPath["actorSearchResults"]);
                        if (actorSearchNodes != null)
                        {
                            foreach (var searchResult in actorSearchNodes)
                            {
                                var actorSceneUrl = searchResult.SelectSingleNode(siteXPath["searchURL"]).GetAttributeValue("href", string.Empty).Split('?')[0].Replace("dev.", string.Empty);
                                if (!actorSceneUrl.Contains("/join") && !directSearchResults.Contains(actorSceneUrl))
                                {
                                    string titleNoFormatting = searchResult.SelectSingleNode(siteXPath["searchTitle"]).InnerText.Trim();
                                    string curId = Helper.Encode(actorSceneUrl);

                                    string releaseDate = string.Empty;
                                    var dateNode = searchResult.SelectSingleNode(siteXPath["searchDate"]);
                                    if (dateNode != null)
                                    {
                                        string cleanDate = Regex.Replace(dateNode.InnerText.Split(':').Last().Trim(), @"(\d)(st|nd|rd|th)", "$1");
                                        if (DateTime.TryParseExact(cleanDate, siteXPath["searchDateFormat"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                                        {
                                            releaseDate = parsedDate.ToString("yyyy-MM-dd");
                                        }
                                    }
                                    else if (searchDate.HasValue)
                                    {
                                        releaseDate = searchDate.Value.ToString("yyyy-MM-dd");
                                    }

                                    resultData = new RemoteSearchResult
                                    {
                                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                                        Name = $"{releaseDate} [{Helper.GetSearchSiteName(siteNum)}] {Helper.ParseTitle(titleNoFormatting, siteNum)}",
                                        SearchProviderName = Plugin.Instance.Name,
                                    };
                                }

                                if (resultData != null)
                                {
                                    result.Add(resultData);
                                }
                            }
                        }
                    }
                }
                else
                {
                    var sceneHttp = await HTTP.Request(url, cancellationToken);
                    if (sceneHttp.IsOK)
                    {
                        if (!directSearchResults.Contains(sceneURL.Split('?')[0]))
                        {
                            var detailsPageElements = HTML.ElementFromString(sceneHttp.Content);
                            string titleNoFormatting = detailsPageElements.SelectSingleNode(siteXPath["title"]).InnerText.Trim();
                            string curId = Helper.Encode(sceneURL.Split('?')[0]);

                            string releaseDate = string.Empty;
                            var dateNode = detailsPageElements.SelectSingleNode(siteXPath["date"]);
                            if (dateNode != null)
                            {
                                string cleanDate = Regex.Replace(dateNode.InnerText.Split(':').Last().Trim(), @"(\d)(st|nd|rd|th)", "$1");
                                if (DateTime.TryParseExact(cleanDate, siteXPath["dateFormat"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                                {
                                    releaseDate = parsedDate.ToString("yyyy-MM-dd");
                                }
                            }
                            else if (searchDate.HasValue)
                            {
                                releaseDate = searchDate.Value.ToString("yyyy-MM-dd");
                            }

                            resultData = new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                                Name = $"{releaseDate} [{Helper.GetSearchSiteName(siteNum)}] {Helper.ParseTitle(titleNoFormatting, siteNum)}",
                                SearchProviderName = Plugin.Instance.Name,
                            };
                        }

                        if (resultData != null)
                        {
                            result.Add(resultData);
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

            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            if (siteNum[0] >= 1851 && siteNum[0] <= 1859)
            {
                sceneUrl = $"{sceneUrl}?_lang=en";
            }

            var httpResult = await HTTP.Request(sceneUrl, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);
            var siteXPath = GetXPathMap(siteNum);

            var movie = (Movie)result.Item;
            movie.Name = Helper.ParseTitle(detailsPageElements.SelectSingleNode(siteXPath["title"]).InnerText.Trim(), siteNum);

            string description = string.Empty;
            foreach (var desc in detailsPageElements.SelectNodes(siteXPath["summary"]))
            {
                description += desc.InnerText.Trim() + "\n\n";
            }
            movie.Overview = description;

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

            string tagline;
            if (siteNum[0] == 1066)
            {
                tagline = $"{Helper.GetSearchSiteName(siteNum)}: {detailsPageElements.SelectSingleNode("//p[@class='series']").InnerText.Trim()}";
            }
            else
            {
                tagline = Helper.GetSearchSiteName(siteNum);
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
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes[0].InnerText.Split(','))
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
                    string actorName = actor.SelectSingleNode(siteXPath["actor"]).InnerText.Trim();
                    string actorPhotoUrl = actor.SelectSingleNode(siteXPath["actorPhoto"]).GetAttributeValue("src", string.Empty);
                    if (siteNum[0] >= 1860 && siteNum[0] <= 1862)
                    {
                        var actorHttp = await HTTP.Request(actorPhotoUrl, cancellationToken);
                        if (actorHttp.IsOK)
                        {
                            var modelPageElements = HTML.ElementFromString(actorHttp.Content);
                            actorPhotoUrl = modelPageElements.SelectSingleNode("//div[@class='model-photo']//@src").GetAttributeValue("src", string.Empty);
                        }
                    }
                    result.People.Add(new PersonInfo { Name = actorName, ImageUrl = actorPhotoUrl, Type = PersonKind.Actor });
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

            var httpResult = await HTTP.Request(sceneUrl, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var xpaths = new[] { "//div[@class='photo-wrap']//@href", "//div[@id='photo-carousel']//@href", "//video/@poster" };
            foreach (var xpath in xpaths)
            {
                var imageNodes = detailsPageElements.SelectNodes(xpath);
                if (imageNodes != null)
                {
                    foreach (var image in imageNodes)
                    {
                        string imageUrl = image.GetAttributeValue("href", string.Empty) ?? image.GetAttributeValue("poster", string.Empty);
                        if (!imageUrl.StartsWith("http"))
                        {
                            imageUrl = Helper.GetSearchBaseURL(siteNum) + imageUrl;
                        }

                        if (!images.Any(i => i.Url == imageUrl))
                        {
                            images.Add(new RemoteImageInfo { Url = imageUrl });
                        }
                    }
                }
            }

            return images;
        }

        private Dictionary<string, string> GetXPathMap(int[] siteNum)
        {
            if (siteNum[0] == 1066)
            {
                return new Dictionary<string, string>
                {
                    { "searchResults", "//div[contains(@class,'content-item')]" },
                    { "actorSearchResults", "//div[contains(@class, 'content-item')]" },
                    { "searchTitle", ".//h3" },
                    { "searchURL", ".//h3//@href" },
                    { "searchDate", ".//span[@class='pub-date']" },
                    { "title", "//h1" },
                    { "date", "//span[@class='date']" },
                    { "summary", "//div[@class='description']//p" },
                    { "genres", "//meta[@name='keywords']/@content" },
                    { "actors", "//div[@class='model-wrap']//li" },
                    { "actor", ".//h5/text()" },
                    { "actorPhoto", ".//img/@src" },
                    { "searchDateFormat", "MMM dd, yyyy" },
                    { "dateFormat", "dddd MMMM dd, yyyy" },
                };
            }
            else if (siteNum[0] >= 1851 && siteNum[0] <= 1859)
            {
                return new Dictionary<string, string>
                {
                    { "searchResults", "//div[@class='content-metadata']" },
                    { "actorSearchResults", "//div[contains(@class, 'video-description')]" },
                    { "searchTitle", ".//h1" },
                    { "searchURL", ".//h1//@href" },
                    { "searchDate", ".//p[@class='content-date']/strong[1]" },
                    { "title", "//h1" },
                    { "date", "//span[@class='meta-value'][2]" },
                    { "summary", "//div[@class='content-description']//p" },
                    { "genres", "//meta[@name='keywords']/@content" },
                    { "actors", "//div[./div[@class='model-name']]" },
                    { "actor", "./div[@class='model-name']/text()" },
                    { "actorPhoto", ".//img/@src" },
                    { "searchDateFormat", "dd/MM/yyyy" },
                    { "dateFormat", "dd/MM/yyyy" },
                };
            }
            else if (siteNum[0] == 1860)
            {
                return new Dictionary<string, string>
                {
                    { "searchResults", "//div[@class='col-sm-3']" },
                    { "actorSearchResults", "//div[contains(@class, 'content-item')]" },
                    { "searchTitle", ".//h5" },
                    { "searchURL", ".//a//@href" },
                    { "searchDate", ".//div[@class='pull-right'][./i[contains(@class,'calendar')]]" },
                    { "title", "//h2" },
                    { "date", "//span[@class='post-date']" },
                    { "summary", "//div[@class='desc']//p" },
                    { "genres", "//meta[@name='keywords']/@content" },
                    { "actors", "//div[@class='content-meta']//h4[@class='models']//a" },
                    { "actor", "./text()" },
                    { "actorPhoto", ".//@href" },
                    { "searchDateFormat", "dd MMM yyyy" },
                    { "dateFormat", "dd MMM yyyy" },
                };
            }
            else if (siteNum[0] >= 1861 && siteNum[0] <= 1862)
            {
                return new Dictionary<string, string>
                {
                    { "searchResults", "//div[contains(@class, 'content-item-medium')]" },
                    { "actorSearchResults", "//div[contains(@class, 'content-item-large')]" },
                    { "searchTitle", ".//h3" },
                    { "searchURL", ".//a//@href" },
                    { "searchDate", ".//div[@class='date']" },
                    { "title", "//h2" },
                    { "date", "//span[@class='post-date']" },
                    { "summary", "//div[@class='desc']//p" },
                    { "genres", "//meta[@name='keywords']/@content" },
                    { "actors", "//div[@class='content-meta']//h4[@class='models']//a" },
                    { "actor", "./text()" },
                    { "actorPhoto", ".//@href" },
                    { "searchDateFormat", "dd MMM yyyy" },
                    { "dateFormat", "dd MMM yyyy" },
                };
            }

            return null;
        }
    }
}
