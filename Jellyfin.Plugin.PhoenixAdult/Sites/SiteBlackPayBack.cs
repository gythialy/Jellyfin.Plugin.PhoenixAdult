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

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class SiteBlackPayBack : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string searchData = Regex.Replace(searchTitle, @"\bE\d+\b", "").Trim();
            string encoded = searchData.ToLower().Replace(' ', '-');
            string directUrl = $"{Helper.GetSearchSearchURL(siteNum)}{encoded}.html";
            var searchResults = new List<string> { directUrl };

            var googleResults = await GoogleSearch.Search(searchTitle, siteNum, cancellationToken);
            searchResults.AddRange(googleResults.Where(u => u.Contains("/trailers/")));

            foreach (var sceneUrl in searchResults.Distinct())
            {
                var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);
                    string titleNoFormatting = detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim();
                    if (titleNoFormatting != null && !titleNoFormatting.Contains("404 Error"))
                    {
                        string curId = Helper.Encode(sceneUrl);
                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                            Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}]",
                            SearchProviderName = Plugin.Instance.Name
                        });
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
            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return result;
            var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);

            var movie = (Movie)result.Item;
            string title = detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim();
            movie.Name = title;
            movie.Overview = detailsPageElements.SelectSingleNode("//div[@class='videoDetails clear']/p")?.InnerText.Trim();

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddStudio(tagline);
            movie.AddCollection(new[] { tagline });

            var genreNodes = detailsPageElements.SelectNodes("//div[@class='featuring clear']//li/a");
            if(genreNodes != null)
            {
                foreach(var genre in genreNodes)
                    movie.AddGenre(genre.InnerText.Trim());
            }

            var iafdHttp = await HTTP.Request("https://www.iafd.com/studio.rme/studio=9856/blackpayback.com.htm", HttpMethod.Get, cancellationToken);
            if (iafdHttp.IsOK)
            {
                var iafdStudioElements = await HTML.ElementFromString(iafdHttp.Content, cancellationToken);
                var sceneNode = iafdStudioElements.SelectNodes("//table[@id='studio']/tbody/tr")
                    ?.FirstOrDefault(tr => tr.SelectSingleNode(".//a")?.InnerText.Split('(')[0].Trim().Equals(title, StringComparison.OrdinalIgnoreCase) ?? false);

                if (sceneNode != null)
                {
                    string iafdUrl = "https://www.iafd.com" + sceneNode.SelectSingleNode(".//a")?.GetAttributeValue("href", "");
                    var iafdSceneHttp = await HTTP.Request(iafdUrl, HttpMethod.Get, cancellationToken);
                    if (iafdSceneHttp.IsOK)
                    {
                        var iafdSceneElements = await HTML.ElementFromString(iafdSceneHttp.Content, cancellationToken);
                        var dateNode = iafdSceneElements.SelectSingleNode("//p[contains(., 'Release Date')]/following-sibling::p[@class='biodata']");
                        if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                        {
                            movie.PremiereDate = parsedDate;
                            movie.ProductionYear = parsedDate.Year;
                        }

                        var actorNodes = iafdSceneElements.SelectNodes("//div[@class='castbox']");
                        if(actorNodes != null)
                        {
                            foreach(var actor in actorNodes)
                            {
                                string actorName = actor.InnerText.Trim();
                                string actorPhotoUrl = actor.SelectSingleNode(".//img")?.GetAttributeValue("src", "");
                                result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                            }
                        }
                    }
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return images;

            var scriptNode = (await HTML.ElementFromString(httpResult.Content, cancellationToken)).SelectSingleNode("//div[@class='player']/script");
            if(scriptNode != null)
            {
                var match = Regex.Match(scriptNode.InnerText, "(?<=poster=\").*?(?=\")");
                if (match.Success)
                {
                    string imageUrl = match.Value;
                    if (!imageUrl.StartsWith("http"))
                        imageUrl = Helper.GetSearchBaseURL(siteNum) + imageUrl;
                    images.Add(new RemoteImageInfo { Url = imageUrl, Type = ImageType.Primary });
                }
            }

            return images;
        }
    }
}
