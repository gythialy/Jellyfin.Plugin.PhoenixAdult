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
    public class SiteAlettaOceanLive : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var searchResults = new List<HtmlNode>();

            for (int page = 1; page < 100; page++)
            {
                string url = string.Format(Helper.GetSearchSearchURL(siteNum), page);
                var httpResult = await HTTP.Request(url, HttpMethod.Get, cancellationToken);
                if (!httpResult.IsOK)
                {
                    break;
                }

                var searchPageElements = HTML.ElementFromString(httpResult.Content);
                var movieSet = searchPageElements.SelectNodes("//div[contains(@class, 'movie-set-list-item')]");
                if (movieSet == null)
                {
                    break;
                }

                var searchResult = movieSet.FirstOrDefault(n => n.InnerText.Contains(searchTitle));
                if (searchResult != null)
                {
                    searchResults.Add(searchResult);
                    break;
                }
            }

            foreach (var searchResult in searchResults)
            {
                string sceneUrl = searchResult.SelectSingleNode(".//a").GetAttributeValue("href", string.Empty);
                string curId = Helper.Encode(sceneUrl);
                string titleNoFormatting = searchResult.SelectSingleNode(".//div[contains(@class, 'movie-set-list-item__title')]").InnerText.Trim();
                string date = searchResult.SelectSingleNode(".//div[contains(@class, 'movie-set-list-item__date')]").InnerText.Trim();
                string releaseDate = DateTime.Parse(date).ToString("yyyy-MM-dd");

                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, $"{curId}|{releaseDate}" } },
                    Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                    SearchProviderName = Plugin.Instance.Name,
                });
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
            string sceneDate = providerIds[1];

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);
            if (detailsPageElements == null)
            {
                return result;
            }

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//h1").InnerText.Trim();
            movie.AddStudio(Helper.GetSearchSiteName(siteNum));
            movie.AddTag(Helper.GetSearchSiteName(siteNum));

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = "Aletta Ocean", Type = PersonKind.Actor });

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string[] providerIds = sceneID[0].Split('|');
            string sceneUrl = Helper.Decode(providerIds[0]);

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);
            if (detailsPageElements == null)
            {
                return images;
            }

            var scriptNode = detailsPageElements.SelectSingleNode("//script[contains(., '/trailers/')]");
            if (scriptNode != null)
            {
                var match = Regex.Match(scriptNode.InnerText, @"src=\""https://.*\.com/trailers/(\d+)\.");
                if (match.Success)
                {
                    string id = match.Groups[1].Value;
                    images.Add(new RemoteImageInfo { Url = $"https://alettaoceanlive.com/tour/content/{id}/0.jpg", Type = ImageType.Primary });
                }
            }

            return images;
        }
    }
}
