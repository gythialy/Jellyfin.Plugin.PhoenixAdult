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
    public class NetworkCaramelCash : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string sceneId = null;
            var parts = searchTitle.Split(' ');
            if (int.TryParse(parts[0], out _))
            {
                sceneId = parts[0];
                searchTitle = string.Join(" ", parts.Skip(1));
            }

            var searchResults = new List<string>();
            if (sceneId != null)
            {
                searchResults.Add(Helper.GetSearchSearchURL(siteNum) + sceneId);
            }

            var googleResults = await WebSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            foreach (var sceneURL in googleResults)
            {
                if ((sceneURL.Contains("/video/") || sceneURL.Contains("/videos/")) && !sceneURL.Contains("/page/") && !searchResults.Contains(sceneURL))
                {
                    searchResults.Add(sceneURL.Split('?')[0]);
                }
            }

            foreach (var sceneUrl in searchResults)
            {
                var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var detailsPageElements = HTML.ElementFromString(httpResult.Content);
                    string titleNoFormatting = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//h1").InnerText, siteNum);
                    string curId = Helper.Encode(sceneUrl);
                    string releaseDate = string.Empty;
                    var dateNode = detailsPageElements.SelectSingleNode("//div[@class='content-date']");
                    if (dateNode != null && DateTime.TryParseExact(dateNode.InnerText.Trim(), "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDate))
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
                        Name = $"[{Helper.GetSearchSiteName(siteNum)}] {titleNoFormatting} {releaseDate}",
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
            string sceneDate = providerIds.Length > 1 ? providerIds[1] : null;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneUrl;
            movie.Name = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//div[contains(@class, 'content-title')]").InnerText.Trim(), siteNum);
            movie.Overview = detailsPageElements.SelectNodes("//div[contains(@class, 'content-desc')]")[1].InnerText.Trim();
            movie.AddStudio("Caramel Cash");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddStudio(tagline);
            movie.AddCollection(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//div[contains(@class, 'content-date')]");
            if (dateNode != null)
            {
                string dateText = dateNode.InnerText.Trim();
                if (siteNum[0] >= 1041 && siteNum[0] <= 1042)
                {
                    dateText = Regex.Replace(dateText.Split(':').Last().Trim(), @"(\d)(st|nd|rd|th)", "$1");
                    if (DateTime.TryParseExact(dateText, "d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDate))
                    {
                        movie.PremiereDate = parsedDate;
                        movie.ProductionYear = parsedDate.Year;
                    }
                }
                else
                {
                    if (DateTime.TryParseExact(dateText, "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDate))
                    {
                        movie.PremiereDate = parsedDate;
                        movie.ProductionYear = parsedDate.Year;
                    }
                }
            }
            else if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//div[@class='content-tags']/a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//section[@class='content-sec backdrop']//div[@class='main__models']/a");
            if (actorNodes != null)
            {
                foreach (var actor in actorNodes)
                {
                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actor.InnerText.Trim(), Type = PersonKind.Actor });
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

            var imageNodes = detailsPageElements.SelectNodes("//section[@class='content-gallery-sec']//a[@data-lightbox='gallery']");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    images.Add(new RemoteImageInfo { Url = img.GetAttributeValue("href", string.Empty) });
                }
            }

            if (images.Any())
            {
                images.First().Type = ImageType.Primary;
                foreach (var image in images.Skip(1))
                {
                    image.Type = ImageType.Backdrop;
                }
            }

            return images;
        }
    }
}
