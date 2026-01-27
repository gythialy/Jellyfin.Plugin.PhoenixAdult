using System;
using System.Collections.Generic;
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
using Jellyfin.Data.Enums;

namespace PhoenixAdult.Sites
{
    public class NetworkBellaPass : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string searchUrl = $"{Helper.GetSearchSearchURL(siteNum).Replace("/search.php?query=", "/trailers/")}{searchTitle.ToLower().Replace(' ', '-')}.html";
            var directResults = new List<string> { searchUrl };

            var googleResults = await WebSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            foreach (var sceneURL in googleResults)
            {
                if (sceneURL.Contains("/trailers/"))
                {
                    directResults.Add(sceneURL);
                }
            }

            string searchPageUrl = Helper.GetSearchSearchURL(siteNum) + searchTitle.ToLower().Replace(' ', '-');
            var httpResult = await HTTP.Request(searchPageUrl, HttpMethod.Get, cancellationToken);
            if (httpResult.IsOK)
            {
                var searchPageElements = HTML.ElementFromString(httpResult.Content);
                var searchNodes = searchPageElements.SelectNodes("//div[contains(@class, 'item-video')]");
                if (searchNodes != null)
                {
                    foreach (var node in searchNodes)
                    {
                        var timeNode = node.SelectSingleNode(".//div[contains(@class, 'time')]");
                        if (timeNode != null && timeNode.InnerText.Trim().Replace(":", string.Empty).All(char.IsDigit))
                        {
                            string sceneUrl = node.SelectSingleNode("./div[1]//a")?.GetAttributeValue("href", string.Empty);
                            if (!string.IsNullOrEmpty(sceneUrl))
                            {
                                if (!sceneUrl.StartsWith("http"))
                                {
                                    sceneUrl = "http:" + sceneUrl;
                                }

                                directResults.Add(sceneUrl);
                            }
                        }
                    }
                }
            }

            foreach (var sceneUrl in directResults.Distinct())
            {
                try
                {
                    var sceneHttp = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                    if (sceneHttp.IsOK)
                    {
                        var detailsPageElements = HTML.ElementFromString(sceneHttp.Content);
                        string titleNoFormatting = detailsPageElements.SelectSingleNode("//h1 | //h3")?.InnerText.Trim();
                        string curId = Helper.Encode(sceneUrl);
                        string releaseDate = string.Empty;
                        var dateNode = detailsPageElements.SelectSingleNode("//div[contains(@class, 'videoInfo')]/p");
                        if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                        {
                            releaseDate = parsedDate.ToString("yyyy-MM-dd");
                        }
                        else if (searchDate.HasValue)
                        {
                            releaseDate = searchDate.Value.ToString("yyyy-MM-dd");
                        }

                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, $"{curId}" } },
                            Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                            SearchProviderName = Plugin.Instance.Name,
                        });
                    }
                }
                catch
                {
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

            string sceneUrl = Helper.Decode(sceneID[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneUrl;
            if (siteNum[1] == 16 || siteNum[1] == 18)
            {
                movie.Name = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim(), siteNum);
            }
            else
            {
                movie.Name = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//h3")?.InnerText.Trim(), siteNum);
            }

            var description = detailsPageElements.SelectSingleNode("//div[contains(@class, 'videoDetails')]//p");
            if (description != null)
            {
                movie.Overview = description.InnerText.Trim();
            }

            string siteName = Helper.GetSearchSiteName(siteNum);
            if (siteName == "Hussie Pass" || siteName == "Babe Archives" || siteName == "See Him Fuck")
            {
                movie.AddStudio(siteName);
            }
            else
            {
                movie.AddStudio("BellaPass");
                movie.AddStudio(siteName);
            }

            var genreNodes = detailsPageElements.SelectNodes("//div[contains(@class, 'featuring')]//a[contains(@href, '/categories/')]");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//div[contains(@class, 'featuring')]//a[contains(@href, '/models/')]");
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
                    string actorName = Regex.Replace(actor.InnerText.Trim(), @" *?[^\w\s]+", string.Empty).Trim();
                    string actorPageUrl = actor.GetAttributeValue("href", string.Empty);
                    if (actorPageUrl.StartsWith("//"))
                    {
                        actorPageUrl = "https:" + actorPageUrl;
                    }
                    else if (!actorPageUrl.StartsWith("http"))
                    {
                        actorPageUrl = Helper.GetSearchBaseURL(siteNum) + actorPageUrl;
                    }

                    string actorPhotoUrl = string.Empty;
                    var actorHttp = await HTTP.Request(actorPageUrl, HttpMethod.Get, cancellationToken);
                    if (actorHttp.IsOK)
                    {
                        var actorPage = HTML.ElementFromString(actorHttp.Content);
                        actorPhotoUrl = Helper.GetSearchBaseURL(siteNum) + actorPage.SelectSingleNode("//div[@class='profile-pic']/img")?.GetAttributeValue("src0_3x", string.Empty);
                    }

                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            var dateNode = detailsPageElements.SelectSingleNode("//div[contains(@class, 'videoInfo')]/p");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0]);
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

            var imageNodes = detailsPageElements.SelectNodes("//img[contains(@class, 'thumbs')] | //div[contains(@class, 'item-thumb')]//img");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    string imageUrl = img.GetAttributeValue("src0_3x", string.Empty);
                    if (!imageUrl.StartsWith("http"))
                    {
                        imageUrl = Helper.GetSearchBaseURL(siteNum) + imageUrl;
                    }

                    images.Add(new RemoteImageInfo { Url = imageUrl });
                }
            }

            var setIdNode = detailsPageElements.SelectSingleNode("//img[contains(@class, 'thumbs')] | //div[contains(@class, 'item-thumb')]//img");
            if (setIdNode != null)
            {
                string setId = setIdNode.GetAttributeValue("id", string.Empty);
                if (!string.IsNullOrEmpty(setId))
                {
                    string searchPageUrl = Helper.GetSearchSearchURL(siteNum) + item.Name.Replace(" ", "+");
                    var searchHttp = await HTTP.Request(searchPageUrl, HttpMethod.Get, cancellationToken);
                    if (searchHttp.IsOK)
                    {
                        var searchPageElements = HTML.ElementFromString(searchHttp.Content);
                        var cntNode = searchPageElements.SelectSingleNode($"//img[@id='{setId}']");
                        if (cntNode != null && int.TryParse(cntNode.GetAttributeValue("cnt", "0"), out var cnt))
                        {
                            for (int i = 0; i < cnt; i++)
                            {
                                string imageUrl = cntNode.GetAttributeValue($"src{i}_3x", string.Empty);
                                if (!string.IsNullOrEmpty(imageUrl))
                                {
                                    images.Add(new RemoteImageInfo { Url = Helper.GetSearchBaseURL(siteNum) + imageUrl });
                                }
                            }
                        }
                    }

                    string photoPageUrl = sceneUrl.Replace("/trailers/", "/preview/");
                    var photoHttp = await HTTP.Request(photoPageUrl, HttpMethod.Get, cancellationToken);
                    if (photoHttp.IsOK)
                    {
                        var photoPageElements = HTML.ElementFromString(photoHttp.Content);
                        var photoNodes = photoPageElements.SelectNodes($"//img[@id='{setId}']");
                        if (photoNodes != null)
                        {
                            foreach (var img in photoNodes)
                            {
                                string imageUrl = img.GetAttributeValue("src0_3x", string.Empty);
                                if (!string.IsNullOrEmpty(imageUrl))
                                {
                                    images.Add(new RemoteImageInfo { Url = Helper.GetSearchBaseURL(siteNum) + imageUrl });
                                }
                            }
                        }
                    }
                }
            }

            if (images.Any())
            {
                images.First().Type = ImageType.Primary;
            }

            return images.DistinctBy(i => i.Url).ToList();
        }
    }
}
