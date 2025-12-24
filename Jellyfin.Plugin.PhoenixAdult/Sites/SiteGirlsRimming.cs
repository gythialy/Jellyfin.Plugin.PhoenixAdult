using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
    public class SiteGirlsRimming : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string directUrl = $"{Helper.GetSearchSearchURL(siteNum)}{searchTitle.ToLower().Replace(' ', '-')}.html";
            var searchResults = new List<string> { directUrl };

            var googleResults = await WebSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            searchResults.AddRange(googleResults.Where(u => u.Contains("/trailers/")));

            foreach (var sceneUrl in searchResults.Distinct())
            {
                var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK && !httpResult.Content.Contains("Page not found"))
                {
                    var searchResult = HTML.ElementFromString(httpResult.Content);
                    string titleNoFormatting = searchResult.SelectSingleNode("//h2[@class='title']")?.InnerText;
                    string curId = Helper.Encode(sceneUrl);
                    string releaseDate = searchDate?.ToString("yyyy-MM-dd") ?? string.Empty;

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{releaseDate}" } },
                        Name = $"{titleNoFormatting} [Girls Rimming]",
                        SearchProviderName = Plugin.Instance.Name,
                    });
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
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            string sceneDate = providerIds.Length > 1 ? providerIds[1] : null;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneUrl;
            movie.Name = detailsPageElements.SelectSingleNode("//h2[@class='title']")?.InnerText;
            movie.Overview = detailsPageElements.SelectSingleNode("//meta[@name='description']")?.GetAttributeValue("content", string.Empty);
            movie.AddStudio("Girls Rimming");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddStudio(tagline);

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genres = detailsPageElements.SelectSingleNode("//meta[@name='keywords']")?.GetAttributeValue("content", string.Empty).Split(',');
            if (genres != null)
            {
                foreach (var genre in genres)
                {
                    if (genre.Contains(" Id "))
                    {
                        string actorName = genre.Split(new[] { " Id " }, StringSplitOptions.None)[0].Trim();
                        string actorPageUrl = $"{Helper.GetSearchBaseURL(siteNum)}/tour/models/{actorName.ToLower().Replace(' ', '-')}.html";
                        var actorHttp = await HTTP.Request(actorPageUrl, HttpMethod.Get, cancellationToken);
                        if (!actorHttp.IsOK)
                        {
                            var googleResults = await WebSearch.GetSearchResults(actorName, siteNum, cancellationToken);
                            actorPageUrl = googleResults.FirstOrDefault(u => u.Contains("/models/"));
                            if (actorPageUrl != null)
                            {
                                actorHttp = await HTTP.Request(actorPageUrl, HttpMethod.Get, cancellationToken);
                            }
                        }

                        string actorPhotoUrl = string.Empty;
                        if (actorHttp.IsOK)
                        {
                            var actorPage = HTML.ElementFromString(actorHttp.Content);
                            actorPhotoUrl = actorPage.SelectSingleNode("//div[contains(@class, 'model_picture')]//img")?.GetAttributeValue("src0_3x", string.Empty);
                        }

                        ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                    }
                    else
                    {
                        movie.AddGenre(genre.Trim().Capitalize());
                    }
                }
            }

            movie.AddGenre("Rim Job");

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
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

            var posterNode = detailsPageElements.SelectSingleNode("//div[@id='fakeplayer']//img");
            if (posterNode != null)
            {
                images.Add(new RemoteImageInfo { Url = posterNode.GetAttributeValue("src0_3x", string.Empty), Type = ImageType.Primary });
            }

            return images;
        }
    }
}
