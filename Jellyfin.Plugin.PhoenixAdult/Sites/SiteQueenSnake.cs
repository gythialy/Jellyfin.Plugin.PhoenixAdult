using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    public class SiteQueenSnake : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var searchURL = $"{Helper.GetSearchSearchURL(siteNum)}{searchTitle.Replace(" ", "-").ToLower()}/";
            var http = await HTTP.Request(searchURL, cancellationToken, null, new Dictionary<string, string> { { "cLegalAge", "true" } });
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                foreach (var searchResult in doc.DocumentNode.SelectNodes("//div[@class='contentBlock']"))
                {
                    if (searchResult.SelectSingleNode("//div[@class='pagerWrapper']/a")?.GetAttributeValue("href", string.Empty).Contains("/previewmovies/0") == true)
                    {
                        continue;
                    }

                    var titleNoFormatting = searchResult.SelectSingleNode(".//span[@class='contentFilmName']").InnerText.Trim();
                    var date = searchResult.SelectSingleNode(".//span[@class='contentFileDate']").InnerText.Trim().Split('•')[0].Trim();
                    if (DateTime.TryParse(date, out var parsedDate))
                    {
                        var releaseDate = parsedDate.ToString("yyyy-MM-dd");
                        var curID = Helper.Encode(searchTitle.Replace(" ", "-").ToLower());
                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, curID } },
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
            var movie = (Movie)result.Item;
            var sceneURL = $"{Helper.GetSearchSearchURL(siteNum)}{Helper.Decode(sceneID[0])}";
            var http = await HTTP.Request(sceneURL, cancellationToken, null, new Dictionary<string, string> { { "cLegalAge", "true" } });
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);

            movie.Name = doc.DocumentNode.SelectSingleNode("//span[@class='contentFilmName']").InnerText.Trim();
            movie.Overview = doc.DocumentNode.SelectSingleNode("//div[@class='contentPreviewDescription']")?.InnerText.Trim();
            movie.AddStudio(Helper.GetSearchSiteName(siteNum));
            movie.AddCollection(Helper.GetSearchSiteName(siteNum));
            movie.AddGenre("BDSM");
            movie.AddGenre("S&M");

            var date = doc.DocumentNode.SelectSingleNode("//span[@class='contentFileDate']").InnerText.Trim().Split('•')[0].Trim();
            if (DateTime.TryParseExact(date, "yyyy MMMM d", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            foreach (var genreLink in doc.DocumentNode.SelectNodes("//div[@class='contentPreviewTags']/a"))
            {
                var name = genreLink.InnerText.Trim();
                if (SiteActors.Contains(name.ToLower()))
                {
                    result.AddPerson(new PersonInfo { Name = name, Type = PersonKind.Actor });
                }
                else
                {
                    movie.AddGenre(name);
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var sceneURL = $"{Helper.GetSearchSearchURL(siteNum)}{Helper.Decode(sceneID[0])}";
            var http = await HTTP.Request(sceneURL, cancellationToken, null, new Dictionary<string, string> { { "cLegalAge", "true" } });
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                foreach (var poster in doc.DocumentNode.SelectNodes("//div[@class='contentBlock']//img[contains(@src, 'preview')]/@src"))
                {
                    var posterUrl = $"{Helper.GetSearchBaseURL(siteNum)}{poster.GetAttributeValue("src", string.Empty).Split('?')[0]}";
                    images.Add(new RemoteImageInfo { Url = posterUrl });
                }
            }

            return images;
        }

        private static readonly HashSet<string> SiteActors = new HashSet<string>
        {
            "abby", "briana", "david", "diamond", "greta", "hellia",
            "hilda", "holly", "jade", "jeby", "jessica", "keya", "lilith",
            "luna", "marc", "micha", "misty", "nastee", "nazryana",
            "pearl", "qs", "queensnake", "rachel", "ruby", "sharon",
            "suzy", "tanita", "tracy", "zara",
        };
    }
}
