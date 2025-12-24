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
    public class SiteDickDrainers : IProviderBase
    {
        private static readonly Dictionary<string, List<string>> actorsDB = new Dictionary<string, List<string>>
        {
            { "big-tit-mindfuck", new List<string> { "Anna Blaze" } },
            { "issa-test-on-bbc-today", new List<string> { "Tristan Summers" } },
            { "your-husband-isnt-here-but-i-am", new List<string> { "Penny Pax" } },
            { "his-wife-got-some-scary-big-titties", new List<string> { "Mya Blair" } },
        };

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{searchTitle.Replace(' ', '+').ToLower()}";
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var searchPageElements = HTML.ElementFromString(httpResult.Content);
            var searchNodes = searchPageElements.SelectNodes("//div[@class='item-video hover']");
            if (searchNodes != null)
            {
                foreach (var node in searchNodes)
                {
                    string titleNoFormatting = Helper.ParseTitle(node.SelectSingleNode(".//h4")?.InnerText.Trim(), siteNum);
                    string sceneUrl = node.SelectSingleNode(".//h4//@href")?.GetAttributeValue("href", string.Empty);
                    string curId = Helper.Encode(sceneUrl);
                    string releaseDate = string.Empty;
                    var dateNode = node.SelectSingleNode(".//div[@class='date']");
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
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{releaseDate}" } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }
            }

            var googleResults = await WebSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            foreach (var sceneUrl in googleResults)
            {
                if (sceneUrl.Contains("trailers") && result.All(r => r.ProviderIds[Plugin.Instance.Name].Split('|')[0] != Helper.Encode(sceneUrl)))
                {
                    var http = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                    if (http.IsOK)
                    {
                        var detailsPageElements = HTML.ElementFromString(http.Content);
                        string titleNoFormatting = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//h3")?.InnerText.Trim(), siteNum);
                        string curId = Helper.Encode(sceneUrl);
                        string releaseDate = string.Empty;
                        var dateNode = detailsPageElements.SelectSingleNode("//div[@class='videoInfo clear']/p");
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
                            ProviderIds = { { Plugin.Instance.Name, $"{curId}|{releaseDate}" } },
                            Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                            SearchProviderName = Plugin.Instance.Name,
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

            string[] providerIds = sceneID[0].Split('|');
            string sceneUrl = Helper.Decode(providerIds[0]);
            string sceneDate = providerIds.Length > 1 ? providerIds[1] : null;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneUrl;
            movie.Name = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//h3")?.InnerText.Trim(), siteNum);
            movie.Overview = string.Join(" ", detailsPageElements.SelectNodes("//div[@class='videoDetails clear']//p/span//text()")?.Select(t => t.InnerText) ?? new string[0]).Replace("FULL VIDEO", string.Empty);

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddStudio(tagline);

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//li[contains(., 'Tags')]//parent::ul//a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//li[@class='update_models']");
            if (actorNodes != null && actorNodes.Any())
            {
                foreach (var actor in actorNodes)
                {
                    string actorName = actor.InnerText.Trim();
                    string modelUrl = actor.SelectSingleNode(".//@href")?.GetAttributeValue("href", string.Empty);
                    string actorPhotoUrl = string.Empty;
                    var actorHttp = await HTTP.Request(modelUrl, HttpMethod.Get, cancellationToken);
                    if (actorHttp.IsOK)
                    {
                        var actorPage = HTML.ElementFromString(actorHttp.Content);
                        actorPhotoUrl = actorPage.SelectSingleNode("//div[@class='profile-pic']//@src0_3x")?.GetAttributeValue("src0_3x", string.Empty);
                        if (!string.IsNullOrEmpty(actorPhotoUrl) && !actorPhotoUrl.StartsWith("http"))
                        {
                            actorPhotoUrl = Helper.GetSearchBaseURL(siteNum) + actorPhotoUrl;
                        }
                    }

                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }
            else
            {
                var match = Regex.Match(sceneUrl, @"(?<=s/).*(?=\.html)");
                if (match.Success)
                {
                    var key = match.Value.ToLower();
                    if (actorsDB.ContainsKey(key))
                    {
                        foreach (var actorName in actorsDB[key])
                        {
                            ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
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
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var imageNodes = detailsPageElements.SelectNodes("//div[@class='player_thumbs']//@src0_3x | //div[@class='player full_width']/script");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    string imageUrl = img.GetAttributeValue("src0_3x", string.Empty);
                    if (string.IsNullOrEmpty(imageUrl))
                    {
                        var match = Regex.Match(img.InnerText, @"(?<=src0_3x="")(.*?(?=""))");
                        if (match.Success)
                        {
                            imageUrl = match.Groups[1].Value;
                        }
                    }

                    if (!string.IsNullOrEmpty(imageUrl) && !imageUrl.StartsWith("http"))
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
