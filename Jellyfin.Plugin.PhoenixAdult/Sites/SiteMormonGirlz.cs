using System;
using System.Collections.Generic;
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
    public class SiteMormonGirlz : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var searchURL = Helper.GetSearchSearchURL(siteNum) + searchTitle.Replace(" ", "+");
            var http = await HTTP.Request(searchURL, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                foreach (var searchResult in doc.DocumentNode.SelectNodes("//*[contains(@class, ' post ')]"))
                {
                    var titleNoFormatting = searchResult.SelectSingleNode(".//h1[@class='entry-title']/a").InnerText.Trim();
                    var sceneURL = searchResult.SelectSingleNode(".//h1[@class='entry-title']/a").GetAttributeValue("href", string.Empty);
                    var curID = Helper.Encode(sceneURL);

                    var date = searchResult.SelectSingleNode(".//time[@class='entry-date']").GetAttributeValue("datetime", string.Empty).Trim();
                    var releaseDate = string.Empty;
                    if (DateTime.TryParse(date, out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = $"{titleNoFormatting} [MormonGirlz] {releaseDate}",
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
            var movie = (Movie)result.Item;
            var sceneURL = Helper.Decode(sceneID[0]);
            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);
            movie.ExternalId = sceneURL;
            movie.Name = doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']").GetAttributeValue("content", string.Empty).Replace(" - Mormon Girlz", string.Empty).Trim();
            movie.Overview = doc.DocumentNode.SelectSingleNode("//*[contains(@id, 'post-')]/aside[2]/div/div[1]").InnerText.Trim();
            movie.AddStudio("MormonGirlz");
            movie.AddCollection("MormonGirlz");

            var dateNode = doc.DocumentNode.SelectSingleNode("//meta[@property='article:published_time']");
            if (dateNode != null && DateTime.TryParse(dateNode.GetAttributeValue("content", string.Empty).Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            foreach (var genreLink in doc.DocumentNode.SelectNodes("//h1[contains(text(), 'more of') and not(contains(text(), 'Mormon Girls'))]"))
            {
                var genreName = genreLink.InnerText.Replace("more of", string.Empty).Trim();
                movie.AddGenre(genreName);
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var sceneURL = Helper.Decode(sceneID[0]);
            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                foreach (var poster in doc.DocumentNode.SelectNodes("//*[@class='ngg-gallery-thumbnail']/a"))
                {
                    var posterUrl = poster.GetAttributeValue("href", string.Empty);
                    images.Add(new RemoteImageInfo { Url = posterUrl });
                }
            }

            return images;
        }
    }
}
