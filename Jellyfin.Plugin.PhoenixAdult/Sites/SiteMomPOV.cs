using System;
using System.Collections.Generic;
using System.Globalization;
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
    public class SiteMomPOV : IProviderBase
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
                foreach (var searchResult in doc.DocumentNode.SelectNodes("//div[@id='inner_content']/div[@class='entry']"))
                {
                    var titleNoFormatting = searchResult.SelectSingleNode(".//div[@class='title_holder']/h1/a").InnerText.Trim();
                    var sceneURL = searchResult.SelectSingleNode(".//div[@class='title_holder']/h1/a").GetAttributeValue("href", string.Empty);
                    var curID = Helper.Encode(sceneURL);

                    var day = searchResult.SelectSingleNode(".//div[@class='date_holder']/span[2]").InnerText.Trim();
                    var month = searchResult.SelectSingleNode(".//div[@class='date_holder']/span[1]").InnerText.Substring(0, 3).Trim();
                    var year = searchResult.SelectSingleNode(".//div[@class='date_holder']/span[1]/span").InnerText.Trim();
                    var date = $"{month} {day}, {year}";
                    var releaseDate = string.Empty;
                    if (DateTime.TryParse(date, out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = $"{titleNoFormatting} [MomPOV] {releaseDate}",
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
            var titleNode = doc.DocumentNode.SelectSingleNode("//a[@class='title']") ?? doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']");
            movie.Name = titleNode.InnerText.Trim();
            movie.Overview = doc.DocumentNode.SelectSingleNode("//div[@class='entry_content']/p").InnerText.Trim();
            movie.AddStudio("MomPOV");
            movie.AddCollection("MomPOV");
            movie.AddGenre("MILF");

            var dateNode = doc.DocumentNode.SelectSingleNode("//div[@class='date_holder']");
            if (dateNode != null && DateTime.TryParseExact(dateNode.InnerText.Trim(), "MMM yyyy d", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
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
                var posterNode = doc.DocumentNode.SelectSingleNode("//div[@id='inner_content']/div[1]/a/img");
                if (posterNode != null)
                {
                    var posterUrl = posterNode.GetAttributeValue("src", string.Empty);
                    if (!string.IsNullOrEmpty(posterUrl))
                    {
                        images.Add(new RemoteImageInfo { Url = posterUrl, Type = ImageType.Primary });
                    }
                }
            }

            return images;
        }
    }
}
